import { TestBed } from '@angular/core/testing';
import { AvailabilityStatus, ProfileRole, VerificationStatus } from '../models/domain';
import { authResult, profile, session } from '../../testing/fixtures';
import { SessionStore } from './session.store';

const TOKEN_KEY = 'saakh.tokens';

/**
 * The store decides the tab lock and who a profile is shown, once, for the whole
 * app. These are the two rules the API enforces independently — if the client
 * copy drifts, a locked account sees tabs it cannot use.
 */
describe('SessionStore', () => {
  let store: SessionStore;

  beforeEach(() => {
    localStorage.removeItem(TOKEN_KEY);
    TestBed.configureTestingModule({});
    store = TestBed.inject(SessionStore);
  });

  afterEach(() => localStorage.removeItem(TOKEN_KEY));

  it('unlocks the feature tabs only once an account is Active', () => {
    for (const status of [
      VerificationStatus.NeedsApproval,
      VerificationStatus.Pending,
      VerificationStatus.Rejected,
    ]) {
      store.setSession(session({ profile: profile({ verificationStatus: status }) }));
      expect(store.featuresUnlocked())
        .withContext(`status ${VerificationStatus[status]} must stay locked`)
        .toBeFalse();
    }

    store.setSession(session({ profile: profile({ verificationStatus: VerificationStatus.Active }) }));
    expect(store.featuresUnlocked()).toBeTrue();
  });

  it('shows a Lender the Seekers and a Seeker the Lenders', () => {
    store.setSession(session({ profile: profile({ role: ProfileRole.Lender }) }));
    expect(store.counterpartyRole()).toBe(ProfileRole.Seeker);

    store.setSession(session({ profile: profile({ role: ProfileRole.Seeker }) }));
    expect(store.counterpartyRole()).toBe(ProfileRole.Lender);
  });

  it('has no counterparty role when nobody is signed in', () => {
    expect(store.counterpartyRole()).toBeNull();
  });

  it('reads an admin as an admin and not as a trader', () => {
    store.setSession(
      session({ profile: null, admin: { id: 'admin-1', name: 'Reviewer One' }, roles: ['Admin'] }),
    );

    expect(store.isAdmin()).toBeTrue();
    expect(store.isTrader()).toBeFalse();
    // An admin has no trading profile, so the gate must not read as unlocked.
    expect(store.featuresUnlocked()).toBeFalse();
  });

  it('reports a suspended and a removed profile distinctly', () => {
    store.setSession(
      session({ profile: profile({ availabilityStatus: AvailabilityStatus.Suspended }) }),
    );
    expect(store.isSuspended()).toBeTrue();
    expect(store.isRemoved()).toBeFalse();

    store.setSession(
      session({ profile: profile({ availabilityStatus: AvailabilityStatus.Removed }) }),
    );
    expect(store.isRemoved()).toBeTrue();
  });

  it('persists tokens so a page reload can restore the session', () => {
    store.apply(authResult());

    expect(store.accessToken()).toBe('access-token-1');
    expect(JSON.parse(localStorage.getItem(TOKEN_KEY) ?? '{}').refreshToken).toBe('refresh-token-1');
  });

  it('restores from a stored token on a cold load, before any API call', () => {
    localStorage.setItem(
      TOKEN_KEY,
      JSON.stringify({ accessToken: 'a', refreshToken: 'r', accessTokenExpiresAt: 'z' }),
    );

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const cold = TestBed.inject(SessionStore);

    expect(cold.hasToken()).toBeTrue();
    // The session itself is not known yet — the guard fetches it.
    expect(cold.session()).toBeNull();
  });

  it('survives a cold load when site data is unreadable', () => {
    localStorage.setItem(TOKEN_KEY, 'not json');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    expect(TestBed.inject(SessionStore).hasToken()).toBeFalse();
  });

  it('clears the stored token on sign-out', () => {
    store.apply(authResult());
    store.clear();

    expect(store.session()).toBeNull();
    expect(store.hasToken()).toBeFalse();
    expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
  });
});
