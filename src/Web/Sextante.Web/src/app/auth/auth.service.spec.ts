import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), AuthService],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('signup posts to /api/auth/signup with the form payload', async () => {
    const promise = service.signup({
      email: 'tester@example.com',
      password: 'PasswordSegura1!extra',
      tenantName: 'Test Tenant',
    });

    const req = httpMock.expectOne('/api/auth/signup');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      email: 'tester@example.com',
      password: 'PasswordSegura1!extra',
      tenantName: 'Test Tenant',
    });
    req.flush({
      userId: '11111111-1111-1111-1111-111111111111',
      tenantId: '22222222-2222-2222-2222-222222222222',
    });

    const result = await promise;
    expect(result.userId).toBe('11111111-1111-1111-1111-111111111111');
  });

  it('login populates the auth state from /me response', async () => {
    const promise = service.login({
      email: 'tester@example.com',
      password: 'PasswordSegura1!extra',
    });

    const loginReq = httpMock.expectOne('/api/auth/login');
    expect(loginReq.request.method).toBe('POST');
    expect(loginReq.request.withCredentials).toBeTrue();
    loginReq.flush({
      tokenType: 'Bearer',
      accessToken: 'access-1',
      expiresIn: 900,
      refreshToken: 'refresh-1',
    });

    await waitForMicrotasks();
    const meReq = httpMock.expectOne('/api/auth/me');
    expect(meReq.request.method).toBe('GET');
    expect(meReq.request.headers.get('Authorization')).toBe('Bearer access-1');
    meReq.flush({
      userId: 'u1',
      email: 'tester@example.com',
      tenantId: 't1',
      tenantName: 'Tenant Login Spec',
      tenantRole: 'Owner',
    });

    await promise;
    expect(service.isAuthenticated()).toBeTrue();
    expect(service.tenantName()).toBe('Tenant Login Spec');
    expect(service.tenantRole()).toBe('Owner');
    expect(service.userEmail()).toBe('tester@example.com');
    expect(service.getAccessToken()).toBe('access-1');
  });

  it('logout clears local state and posts to /api/auth/logout', async () => {
    await primeSession(service, httpMock);
    expect(service.isAuthenticated()).toBeTrue();

    const promise = service.logout();
    const logoutReq = httpMock.expectOne('/api/auth/logout');
    expect(logoutReq.request.method).toBe('POST');
    expect(logoutReq.request.withCredentials).toBeTrue();
    logoutReq.flush({}, { status: 204, statusText: 'No Content' });

    await promise;
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.snapshot()).toBeNull();
  });

  it('refresh updates only accessToken on a populated state', async () => {
    await primeSession(service, httpMock);
    const before = service.snapshot();
    expect(before).not.toBeNull();

    const promise = service.refresh();
    const req = httpMock.expectOne('/api/auth/refresh');
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush({
      tokenType: 'Bearer',
      accessToken: 'access-2',
      expiresIn: 900,
      refreshToken: 'refresh-2',
    });

    const newToken = await promise;
    expect(newToken).toBe('access-2');
    const after = service.snapshot()!;
    expect(after.accessToken).toBe('access-2');
    expect(after.email).toBe(before!.email, 'email kept');
    expect(after.tenantId).toBe(before!.tenantId, 'tenantId kept');
    expect(after.tenantName).toBe(before!.tenantName, 'tenantName kept');
  });

  it('refresh storm: 5 concurrent calls produce a single HTTP request', async () => {
    await primeSession(service, httpMock);

    const calls = [
      service.refresh(),
      service.refresh(),
      service.refresh(),
      service.refresh(),
      service.refresh(),
    ];
    const req = httpMock.expectOne('/api/auth/refresh');
    req.flush({
      tokenType: 'Bearer',
      accessToken: 'access-storm',
      expiresIn: 900,
      refreshToken: 'refresh-storm',
    });

    const tokens = await Promise.all(calls);
    expect(tokens.every((t) => t === 'access-storm')).toBeTrue();
  });

  it('refresh failure clears state', async () => {
    await primeSession(service, httpMock);

    const promise = service.refresh();
    const req = httpMock.expectOne('/api/auth/refresh');
    req.flush({}, { status: 401, statusText: 'Unauthorized' });

    let caught: unknown;
    try {
      await promise;
    } catch (e) {
      caught = e;
    }
    expect(caught).toBeDefined();
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.snapshot()).toBeNull();
  });
});

async function primeSession(service: AuthService, http: HttpTestingController) {
  const promise = service.login({
    email: 'tester@example.com',
    password: 'PasswordSegura1!extra',
  });
  http.expectOne('/api/auth/login').flush({
    tokenType: 'Bearer',
    accessToken: 'access-1',
    expiresIn: 900,
    refreshToken: 'refresh-1',
  });
  await waitForMicrotasks();
  http.expectOne('/api/auth/me').flush({
    userId: 'u1',
    email: 'tester@example.com',
    tenantId: 't1',
    tenantName: 'Tenant Spec',
    tenantRole: 'Owner',
  });
  await promise;
}

function waitForMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
