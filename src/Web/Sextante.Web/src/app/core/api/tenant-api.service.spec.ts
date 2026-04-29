import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TenantApiService } from './tenant-api.service';

describe('TenantApiService', () => {
  let service: TenantApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        TenantApiService,
      ],
    });
    service = TestBed.inject(TenantApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getSettings hits /api/tenants/me', async () => {
    const promise = service.getSettings();
    const req = httpMock.expectOne('/api/tenants/me');
    expect(req.request.method).toBe('GET');
    req.flush({ id: 't1', name: 'Tenant', primaryCurrency: 'EUR' });
    const result = await promise;
    expect(result.primaryCurrency).toBe('EUR');
  });

  it('updateSettings puts to /api/tenants/me', async () => {
    const promise = service.updateSettings({ primaryCurrency: 'BRL' });
    const req = httpMock.expectOne('/api/tenants/me');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.primaryCurrency).toBe('BRL');
    req.flush({ id: 't1', name: 'Tenant', primaryCurrency: 'BRL' });
    await promise;
  });
});
