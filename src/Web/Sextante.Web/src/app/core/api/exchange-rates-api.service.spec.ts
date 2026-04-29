import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ExchangeRatesApiService } from './exchange-rates-api.service';

describe('ExchangeRatesApiService', () => {
  let service: ExchangeRatesApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ExchangeRatesApiService,
      ],
    });
    service = TestBed.inject(ExchangeRatesApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list hits /api/admin/exchange-rates with optional params', async () => {
    const promise = service.list({
      from: '2026-04-01',
      to: '2026-04-28',
      currencies: ['USD', 'BRL'],
    });
    const req = httpMock.expectOne((r) => r.url === '/api/admin/exchange-rates');
    expect(req.request.params.get('from')).toBe('2026-04-01');
    expect(req.request.params.get('to')).toBe('2026-04-28');
    expect(req.request.params.getAll('currencies')).toEqual(['USD', 'BRL']);
    req.flush([]);
    await promise;
  });

  it('getState hits /api/admin/exchange-rates/state', async () => {
    const promise = service.getState();
    const req = httpMock.expectOne('/api/admin/exchange-rates/state');
    expect(req.request.method).toBe('GET');
    req.flush({ lastRunAt: null, lastSuccessAt: null, lastError: null });
    await promise;
  });

  it('insertManual posts to /api/admin/exchange-rates', async () => {
    const promise = service.insertManual({
      rateDate: '2026-04-28',
      toCurrency: 'USD',
      rate: 1.1,
    });
    const req = httpMock.expectOne('/api/admin/exchange-rates');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.toCurrency).toBe('USD');
    req.flush({});
    await promise;
  });

  it('runSnapshot posts to /api/admin/exchange-rates/snapshot/run', async () => {
    const promise = service.runSnapshot();
    const req = httpMock.expectOne('/api/admin/exchange-rates/snapshot/run');
    expect(req.request.method).toBe('POST');
    req.flush(null);
    await promise;
  });
});
