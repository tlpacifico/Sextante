import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { FinancialApiService } from './financial-api.service';

describe('FinancialApiService', () => {
  let service: FinancialApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), FinancialApiService],
    });
    service = TestBed.inject(FinancialApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listAccounts hits /api/financial/accounts', async () => {
    const promise = service.listAccounts();
    const req = httpMock.expectOne('/api/financial/accounts');
    expect(req.request.method).toBe('GET');
    req.flush([]);
    const result = await promise;
    expect(result).toEqual([]);
  });

  it('createTransaction posts to /api/financial/transactions', async () => {
    const promise = service.createTransaction({
      accountId: 'a',
      categoryId: 'c',
      occurredAt: '2026-04-26T12:00:00.000Z',
      amount: 30,
    });
    const req = httpMock.expectOne('/api/financial/transactions');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.amount).toBe(30);
    req.flush({});
    await promise;
  });

  it('listTransactions includes filter params', async () => {
    const promise = service.listTransactions({
      dateFrom: '2026-04-01T00:00:00Z',
      categoryIds: ['c1'],
      accountIds: ['a1'],
      pageSize: 25,
    });
    const req = httpMock.expectOne((r) => r.url === '/api/financial/transactions');
    expect(req.request.params.get('dateFrom')).toBe('2026-04-01T00:00:00Z');
    expect(req.request.params.get('pageSize')).toBe('25');
    expect(req.request.params.getAll('categoryIds')).toEqual(['c1']);
    expect(req.request.params.getAll('accountIds')).toEqual(['a1']);
    req.flush({ items: [], nextCursor: null });
    await promise;
  });

  it('archiveCategory issues DELETE', async () => {
    const promise = service.archiveCategory('cid');
    const req = httpMock.expectOne('/api/financial/categories/cid');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    await promise;
  });
});
