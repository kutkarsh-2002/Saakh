import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authResult } from '../../testing/fixtures';
import { SessionStore } from './session.store';
import { authInterceptor } from './auth.interceptor';

/**
 * Access tokens are short-lived by design, so a vendor mid-negotiation will hit
 * a 401 during an ordinary session. The interceptor is what makes that
 * invisible: rotate the refresh token, replay the request, and only fall back to
 * the sign-in screen when the refresh itself is refused.
 */
describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let store: SessionStore;
  let navigate: jasmine.Spy;

  beforeEach(() => {
    localStorage.removeItem('saakh.tokens');

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    store = TestBed.inject(SessionStore);
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });

  afterEach(() => {
    backend.verify();
    store.clear();
  });

  it('attaches the access token to an authenticated call', () => {
    store.apply(authResult());

    http.get('/api/deals/open').subscribe();

    const request = backend.expectOne('/api/deals/open');
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-token-1');
    request.flush([]);
  });

  it('leaves the sign-in call unauthenticated, so a stale token cannot poison it', () => {
    store.apply(authResult());

    http.post('/api/auth/login', { email: 'a@b.c' }).subscribe();

    const request = backend.expectOne('/api/auth/login');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush(authResult());
  });

  it('rotates the token on a 401 and replays the original request', () => {
    store.apply(authResult());

    let body: unknown;
    http.get('/api/deals/open').subscribe((value) => (body = value));

    backend.expectOne('/api/deals/open').flush(null, { status: 401, statusText: 'Unauthorized' });

    const refresh = backend.expectOne('/api/auth/refresh');
    expect(refresh.request.body).toEqual({ refreshToken: 'refresh-token-1' });
    refresh.flush(authResult({ accessToken: 'access-token-2', refreshToken: 'refresh-token-2' }));

    const replay = backend.expectOne('/api/deals/open');
    expect(replay.request.headers.get('Authorization')).toBe('Bearer access-token-2');
    replay.flush([{ id: 'deal-1' }]);

    expect(body).toEqual([{ id: 'deal-1' }]);
    expect(store.accessToken()).toBe('access-token-2');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('refreshes once for several requests that fail together', () => {
    store.apply(authResult());

    http.get('/api/deals/open').subscribe();
    http.get('/api/interests').subscribe();

    const failures = backend.match((request) => request.url.startsWith('/api/') && request.method === 'GET');
    expect(failures.length).toBe(2);
    failures.forEach((request) => request.flush(null, { status: 401, statusText: 'Unauthorized' }));

    // One refresh for both, not one each: two rotations would invalidate the
    // first new token and sign the user straight back out.
    backend
      .expectOne('/api/auth/refresh')
      .flush(authResult({ accessToken: 'access-token-2', refreshToken: 'refresh-token-2' }));

    const replays = backend.match((request) => request.method === 'GET');
    expect(replays.length).toBe(2);
    replays.forEach((request) => {
      expect(request.request.headers.get('Authorization')).toBe('Bearer access-token-2');
      request.flush([]);
    });
  });

  it('clears the session and returns to sign-in when the refresh is refused', () => {
    store.apply(authResult());

    http.get('/api/deals/open').subscribe({ error: () => undefined });

    backend.expectOne('/api/deals/open').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(store.hasToken()).toBeFalse();
    expect(store.session()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/sign-in']);
  });

  it('does not attempt a refresh it has no token for', () => {
    // A half-cleared session: no refresh token to rotate with.
    store.setTokens(null);

    http.get('/api/deals/open').subscribe({ error: () => undefined });

    backend.expectOne('/api/deals/open').flush(null, { status: 401, statusText: 'Unauthorized' });

    backend.expectNone('/api/auth/refresh');
    expect(navigate).toHaveBeenCalledWith(['/sign-in']);
  });

  it('passes a business-rule rejection straight through without touching the session', () => {
    store.apply(authResult());

    let status = 0;
    http.post('/api/deals/x/halt', {}).subscribe({
      error: (error: { status: number }) => (status = error.status),
    });

    backend
      .expectOne('/api/deals/x/halt')
      .flush({ message: 'This deal is frozen.' }, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
    backend.expectNone('/api/auth/refresh');
    expect(store.hasToken()).toBeTrue();
  });
});
