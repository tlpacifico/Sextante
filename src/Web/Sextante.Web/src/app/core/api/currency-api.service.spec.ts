import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { CurrencyApiService } from './currency-api.service';

describe('CurrencyApiService', () => {
  let service: CurrencyApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        CurrencyApiService,
      ],
    });
    service = TestBed.inject(CurrencyApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listActive hits /api/currencies', async () => {
    const promise = service.listActive();
    const req = httpMock.expectOne('/api/currencies');
    expect(req.request.method).toBe('GET');
    req.flush([
      { code: 'EUR', name: 'Euro', symbol: '€', minorUnits: 2, isActive: true },
    ]);
    const result = await promise;
    expect(result.length).toBe(1);
    expect(result[0].code).toBe('EUR');
  });

  it('listAll hits /api/admin/currencies', async () => {
    const promise = service.listAll();
    const req = httpMock.expectOne('/api/admin/currencies');
    expect(req.request.method).toBe('GET');
    req.flush([]);
    await promise;
  });

  it('create posts to /api/admin/currencies', async () => {
    const promise = service.create({
      code: 'XYZ',
      name: 'Test',
      symbol: 'X',
      minorUnits: 2,
      isActive: true,
    });
    const req = httpMock.expectOne('/api/admin/currencies');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.code).toBe('XYZ');
    req.flush({
      code: 'XYZ',
      name: 'Test',
      symbol: 'X',
      minorUnits: 2,
      isActive: true,
    });
    await promise;
  });

  it('update puts to /api/admin/currencies/{code}', async () => {
    const promise = service.update('USD', {
      name: 'US Dollar',
      symbol: '$',
      minorUnits: 2,
      isActive: false,
    });
    const req = httpMock.expectOne('/api/admin/currencies/USD');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.isActive).toBe(false);
    req.flush({
      code: 'USD',
      name: 'US Dollar',
      symbol: '$',
      minorUnits: 2,
      isActive: false,
    });
    await promise;
  });
});
