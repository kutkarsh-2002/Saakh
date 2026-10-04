import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, catchError, filter, switchMap, take, throwError } from 'rxjs';
import { AuthResult } from '../models/domain';
import { apiUrl } from '../api/api.config';
import { SessionStore } from './session.store';

/** Shared across concurrent 401s, so a page issuing four requests refreshes once. */
let refreshing = false;
const refreshed$ = new BehaviorSubject<string | null>(null);

const PUBLIC_PATHS = [
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/refresh',
  '/api/auth/otp',
  '/api/auth/gstin',
  '/api/profiles/taxonomy',
];

/**
 * Attaches the JWT, and on a 401 rotates the refresh token once and replays the
 * request. A refresh that itself fails clears the session and returns the user
 * to sign-in rather than leaving them on a half-broken screen.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const store = inject(SessionStore);
  const router = inject(Router);
  const http = inject(HttpClient);

  const isPublic = PUBLIC_PATHS.some((path) => req.url.includes(path));
  const token = store.accessToken();

  const authorized = token && !isPublic ? withToken(req, token) : req;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || isPublic) {
        return throwError(() => error);
      }

      const refreshToken = store.refreshToken();

      if (!refreshToken) {
        store.clear();
        void router.navigate(['/sign-in']);
        return throwError(() => error);
      }

      if (refreshing) {
        // Wait for the in-flight refresh, then replay with the new token.
        return refreshed$.pipe(
          filter((value): value is string => value !== null),
          take(1),
          switchMap((fresh) => next(withToken(req, fresh))),
        );
      }

      refreshing = true;
      refreshed$.next(null);

      return http.post<AuthResult>(apiUrl('/api/auth/refresh'), { refreshToken }).pipe(
        switchMap((result) => {
          store.apply(result);
          refreshing = false;
          refreshed$.next(result.accessToken);
          return next(withToken(req, result.accessToken));
        }),
        catchError((refreshError: unknown) => {
          refreshing = false;
          store.clear();
          void router.navigate(['/sign-in']);
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};

function withToken(
  req: Parameters<HttpInterceptorFn>[0],
  token: string,
): Parameters<HttpInterceptorFn>[0] {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

export type AuthInterceptorReturn = Observable<unknown>;
