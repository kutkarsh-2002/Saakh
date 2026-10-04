import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthApi } from '../api/auth.api';
import { SessionStore } from './session.store';

/**
 * Restores the session from a stored token on a cold load, so a refresh on
 * /dashboard does not bounce the user back to sign-in.
 */
const ensureSession = () => {
  const store = inject(SessionStore);
  const auth = inject(AuthApi);

  if (store.session()) {
    return of(true);
  }

  if (!store.hasToken()) {
    return of(false);
  }

  return auth.session().pipe(
    map(() => true),
    catchError(() => {
      store.clear();
      return of(false);
    }),
  );
};

export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);

  return ensureSession().pipe(
    map((signedIn) =>
      signedIn ? true : router.createUrlTree(['/sign-in'], { queryParams: { next: state.url } }),
    ),
  );
};

/** Lender and Seeker screens. Admins have no trading profile and no dashboard. */
export const traderGuard: CanActivateFn = (route, state) => {
  const router = inject(Router);
  const store = inject(SessionStore);

  return ensureSession().pipe(
    map((signedIn) => {
      if (!signedIn) {
        return router.createUrlTree(['/sign-in'], { queryParams: { next: state.url } });
      }
      return store.isTrader() ? true : router.createUrlTree(['/admin']);
    }),
  );
};

export const adminGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const store = inject(SessionStore);

  return ensureSession().pipe(
    map((signedIn) => {
      if (!signedIn) {
        return router.createUrlTree(['/sign-in'], { queryParams: { next: state.url } });
      }
      return store.isAdmin() ? true : router.createUrlTree(['/dashboard']);
    }),
  );
};

/**
 * The tab lock from the spec: a Needs Approval, Pending or Rejected account can
 * reach nothing but its own Profile. Redirecting here (rather than only hiding
 * the tab) means a typed URL is blocked too, and the API enforces the same rule
 * independently.
 */
export const verifiedGuard: CanActivateFn = (route, state) => {
  const router = inject(Router);
  const store = inject(SessionStore);

  return ensureSession().pipe(
    map((signedIn) => {
      if (!signedIn) {
        return router.createUrlTree(['/sign-in'], { queryParams: { next: state.url } });
      }

      if (store.isAdmin()) {
        return router.createUrlTree(['/admin']);
      }

      if (!store.featuresUnlocked()) {
        return router.createUrlTree(['/profile'], { queryParams: { locked: state.url } });
      }

      return true;
    }),
  );
};

/** Keeps a signed-in user off the sign-in and sign-up screens. */
export const guestGuard: CanActivateFn = () => {
  const router = inject(Router);
  const store = inject(SessionStore);

  return ensureSession().pipe(
    map((signedIn) => {
      if (!signedIn) {
        return true;
      }
      return store.isAdmin()
        ? router.createUrlTree(['/admin'])
        : router.createUrlTree(['/dashboard']);
    }),
  );
};
