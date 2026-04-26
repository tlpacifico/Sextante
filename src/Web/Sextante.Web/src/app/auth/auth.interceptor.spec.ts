import { TestBed } from '@angular/core/testing';
import {
  HTTP_INTERCEPTORS,
  HttpClient,
  HttpContext,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { AuthService, SKIP_AUTH } from './auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => httpMock.verify());

  it('attaches Authorization header to /api requests when authenticated', async () => {
    await primeSession(auth, httpMock);

    http.get('/api/financial/categories').subscribe();
    const req = httpMock.expectOne('/api/financial/categories');
    expect(req.request.headers.get('Authorization')).toBe('Bearer access-1');
    req.flush([]);
  });

  it('does not attach Authorization to /api/auth/login', async () => {
    await primeSession(auth, httpMock);

    http
      .post(
        '/api/auth/login',
        { email: 'x@y.com', password: 'p' },
        { context: new HttpContext().set(SKIP_AUTH, true) },
      )
      .subscribe();
    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });

  it('does not attach Authorization to non-/api URLs', async () => {
    await primeSession(auth, httpMock);

    http.get('/assets/version.json').subscribe();
    const req = httpMock.expectOne('/assets/version.json');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });

  it('on 401 it refreshes and retries the original request once', async () => {
    await primeSession(auth, httpMock);

    let received: unknown = null;
    http.get<{ ok: boolean }>('/api/financial/categories').subscribe({
      next: (value) => (received = value),
    });

    const first = httpMock.expectOne('/api/financial/categories');
    expect(first.request.headers.get('Authorization')).toBe('Bearer access-1');
    first.flush({}, { status: 401, statusText: 'Unauthorized' });
    await waitForMicrotasks();

    const refreshReq = httpMock.expectOne('/api/auth/refresh');
    refreshReq.flush({
      tokenType: 'Bearer',
      accessToken: 'access-2',
      expiresIn: 900,
      refreshToken: 'refresh-2',
    });
    await waitForMicrotasks();

    const retry = httpMock.expectOne('/api/financial/categories');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
    retry.flush({ ok: true });

    await waitForMicrotasks();
    expect(received).toEqual({ ok: true });
  });

  it('refresh storm: 5 concurrent 401s share a single refresh call', async () => {
    await primeSession(auth, httpMock);

    const subscriptions = Array.from({ length: 5 }, (_, i) =>
      http.get(`/api/financial/${i}`).subscribe({ error: () => {} }),
    );

    const firstAttempts = httpMock.match((req) =>
      req.url.startsWith('/api/financial/'),
    );
    expect(firstAttempts.length).toBe(5);
    firstAttempts.forEach((r) =>
      r.flush({}, { status: 401, statusText: 'Unauthorized' }),
    );
    await waitForMicrotasks();

    const refreshReqs = httpMock.match('/api/auth/refresh');
    expect(refreshReqs.length).toBe(1, 'storm guard must collapse to one call');
    refreshReqs[0].flush({
      tokenType: 'Bearer',
      accessToken: 'access-after-storm',
      expiresIn: 900,
      refreshToken: 'r2',
    });
    await waitForMicrotasks();

    const retries = httpMock.match((req) =>
      req.url.startsWith('/api/financial/'),
    );
    expect(retries.length).toBe(5);
    retries.forEach((r) => r.flush({}));
    subscriptions.forEach((s) => s.unsubscribe());
  });

  it('on refresh failure logs out and propagates 401', async () => {
    await primeSession(auth, httpMock);
    expect(auth.isAuthenticated()).toBeTrue();

    let caught: unknown;
    http.get('/api/financial/categories').subscribe({
      error: (err) => (caught = err),
    });

    httpMock
      .expectOne('/api/financial/categories')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await waitForMicrotasks();

    httpMock
      .expectOne('/api/auth/refresh')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await waitForMicrotasks();

    // logout fires a POST /api/auth/logout — drain it.
    const logoutMatches = httpMock.match('/api/auth/logout');
    logoutMatches.forEach((r) =>
      r.flush({}, { status: 204, statusText: 'No Content' }),
    );

    await waitForMicrotasks();
    expect(caught).toBeDefined();
    expect(auth.isAuthenticated()).toBeFalse();
  });
});

async function primeSession(
  service: AuthService,
  http: HttpTestingController,
): Promise<void> {
  const promise = service.login({ email: 'a@b.com', password: 'pw' });
  http.expectOne('/api/auth/login').flush({
    tokenType: 'Bearer',
    accessToken: 'access-1',
    expiresIn: 900,
    refreshToken: 'refresh-1',
  });
  await waitForMicrotasks();
  http.expectOne('/api/auth/me').flush({
    userId: 'u1',
    email: 'a@b.com',
    tenantId: 't1',
    tenantName: 'Tenant Spec',
    tenantRole: 'Owner',
  });
  await promise;
}

function waitForMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
