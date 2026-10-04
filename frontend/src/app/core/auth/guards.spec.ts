import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  GuardResult,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { Observable, firstValueFrom, of, throwError } from 'rxjs';
import { VerificationStatus } from '../models/domain';
import { AuthApi } from '../api/auth.api';
import { adminSession, profile, session } from '../../testing/fixtures';
import { SessionStore } from './session.store';
import { adminGuard, authGuard, guestGuard, traderGuard, verifiedGuard } from './guards';

const TOKEN_KEY = 'saakh.tokens';

/**
 * The route guards are the client half of the spec's access rules. The API
 * enforces the same rules on every endpoint, so a guard that is wrong here does
 * not leak data — but it does put a user in front of a screen that can only
 * fail, which is the worst version of this product's first impression.
 */
describe('route guards', () => {
  let store: SessionStore;
  let router: Router;
  let restore: jasmine.Spy;

  const seedToken = () =>
    localStorage.setItem(
      TOKEN_KEY,
      JSON.stringify({ accessToken: 'a', refreshToken: 'r', accessTokenExpiresAt: 'z' }),
    );

  const configure = () => {
    store = TestBed.inject(SessionStore);
    router = TestBed.inject(Router);
  };

  const run = (guard: CanActivateFn, url = '/deals'): Promise<GuardResult> =>
    firstValueFrom(
      TestBed.runInInjectionContext(() =>
        guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
      ) as Observable<GuardResult>,
    );

  const redirectOf = (result: GuardResult) => router.serializeUrl(result as UrlTree);

  beforeEach(() => {
    localStorage.removeItem(TOKEN_KEY);
    restore = jasmine.createSpy('session');

    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthApi, useValue: { session: restore } }],
    });
  });

  afterEach(() => localStorage.removeItem(TOKEN_KEY));

  describe('authGuard', () => {
    it('sends a signed-out visitor to sign-in, remembering where they were going', async () => {
      configure();
      restore.and.returnValue(of(session()));

      const result = await run(authGuard, '/deals/open');

      expect(redirectOf(result)).toBe('/sign-in?next=%2Fdeals%2Fopen');
      // No token means no reason to call the API at all.
      expect(restore).not.toHaveBeenCalled();
    });

    it('restores the session from a stored token rather than bouncing a reload', async () => {
      seedToken();
      configure();
      restore.and.callFake(() => {
        store.setSession(session());
        return of(session());
      });

      await expectAsync(run(authGuard, '/dashboard')).toBeResolvedTo(true);
      expect(restore).toHaveBeenCalledTimes(1);
    });

    it('clears a stale token and returns to sign-in when the restore fails', async () => {
      seedToken();
      configure();
      restore.and.returnValue(throwError(() => new Error('401')));

      const result = await run(authGuard, '/dashboard');

      expect(redirectOf(result)).toBe('/sign-in?next=%2Fdashboard');
      expect(store.hasToken()).toBeFalse();
    });

    it('does not re-fetch a session it already holds', async () => {
      configure();
      store.setSession(session());

      await expectAsync(run(authGuard)).toBeResolvedTo(true);
      expect(restore).not.toHaveBeenCalled();
    });
  });

  describe('verifiedGuard', () => {
    it('holds a locked account on its own profile, and says which URL was blocked', async () => {
      configure();

      for (const status of [
        VerificationStatus.NeedsApproval,
        VerificationStatus.Pending,
        VerificationStatus.Rejected,
      ]) {
        store.setSession(session({ profile: profile({ verificationStatus: status }) }));

        const result = await run(verifiedGuard, '/opportunity');

        expect(redirectOf(result))
          .withContext(`status ${VerificationStatus[status]}`)
          .toBe('/profile?locked=%2Fopportunity');
      }
    });

    it('lets an Active account through', async () => {
      configure();
      store.setSession(session());

      await expectAsync(run(verifiedGuard, '/opportunity')).toBeResolvedTo(true);
    });

    it('sends an admin to the admin console, which is where their work is', async () => {
      configure();
      store.setSession(adminSession());

      expect(redirectOf(await run(verifiedGuard))).toBe('/admin');
    });
  });

  describe('role guards', () => {
    it('keeps a trader out of the admin console', async () => {
      configure();
      store.setSession(session());

      expect(redirectOf(await run(adminGuard, '/admin/queue'))).toBe('/dashboard');
    });

    it('lets an admin into the admin console', async () => {
      configure();
      store.setSession(adminSession());

      await expectAsync(run(adminGuard, '/admin/queue')).toBeResolvedTo(true);
    });

    it('keeps an admin out of the trading screens, which they have no profile for', async () => {
      configure();
      store.setSession(adminSession());

      expect(redirectOf(await run(traderGuard, '/dashboard'))).toBe('/admin');
    });

    it('lets a trader into the trading screens even while locked', async () => {
      // traderGuard answers "is this a trading account", not "is it verified" —
      // a locked account still needs to reach its own profile to get unlocked.
      configure();
      store.setSession(
        session({ profile: profile({ verificationStatus: VerificationStatus.Pending }) }),
      );

      await expectAsync(run(traderGuard, '/profile')).toBeResolvedTo(true);
    });
  });

  describe('guestGuard', () => {
    it('lets a signed-out visitor reach sign-in', async () => {
      configure();

      await expectAsync(run(guestGuard, '/sign-in')).toBeResolvedTo(true);
    });

    it('sends a signed-in trader to their dashboard instead of the sign-in form', async () => {
      configure();
      store.setSession(session());

      expect(redirectOf(await run(guestGuard, '/sign-in'))).toBe('/dashboard');
    });

    it('sends a signed-in admin to the console', async () => {
      configure();
      store.setSession(adminSession());

      expect(redirectOf(await run(guestGuard, '/sign-in'))).toBe('/admin');
    });
  });
});
