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

  // Phase 4 — CSV import / categorization rules / import profiles ----------

  it('listImportProfiles GETs /api/financial/import-profiles', async () => {
    const promise = service.listImportProfiles();
    const req = httpMock.expectOne('/api/financial/import-profiles');
    expect(req.request.method).toBe('GET');
    req.flush([]);
    expect(await promise).toEqual([]);
  });

  it('listCategorizationRules GETs /api/financial/categorization-rules', async () => {
    const promise = service.listCategorizationRules();
    const req = httpMock.expectOne('/api/financial/categorization-rules');
    expect(req.request.method).toBe('GET');
    req.flush([]);
    await promise;
  });

  it('reorderRules PUTs to /api/financial/categorization-rules/reorder', async () => {
    const promise = service.reorderRules({ ruleIds: ['a', 'b'] });
    const req = httpMock.expectOne('/api/financial/categorization-rules/reorder');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ ruleIds: ['a', 'b'] });
    req.flush(null);
    await promise;
  });

  it('reapplyRules POSTs to .../reapply with query params', async () => {
    const promise = service.reapplyRules({
      categoryId: 'cat-1',
      from: '2026-01-01',
      to: '2026-01-31',
      onlyUncategorized: true,
    });
    const req = httpMock.expectOne((r) => r.url === '/api/financial/categorization-rules/reapply');
    expect(req.request.method).toBe('POST');
    expect(req.request.params.get('categoryId')).toBe('cat-1');
    expect(req.request.params.get('from')).toBe('2026-01-01');
    expect(req.request.params.get('to')).toBe('2026-01-31');
    expect(req.request.params.get('onlyUncategorized')).toBe('true');
    req.flush({ totalProcessed: 0, categorizedCount: 0, unchangedCount: 0 });
    await promise;
  });

  it('uploadCsv POSTs FormData to /api/financial/imports/upload', async () => {
    const file = new File(['Data;Valor\n01-01;10'], 'test.csv', { type: 'text/csv' });
    const promise = service.uploadCsv(file);
    const req = httpMock.expectOne('/api/financial/imports/upload');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    req.flush({ batchId: 'b1', headers: [], rows: [], totalRowCount: 0, truncated: false });
    await promise;
  });

  it('uploadCsv appends importProfileId as query param when provided', async () => {
    const file = new File(['x'], 'test.csv', { type: 'text/csv' });
    const promise = service.uploadCsv(file, 'profile-1');
    const req = httpMock.expectOne('/api/financial/imports/upload?importProfileId=profile-1');
    expect(req.request.method).toBe('POST');
    req.flush({ batchId: 'b1', headers: [], rows: [], totalRowCount: 0, truncated: false });
    await promise;
  });

  it('confirmImport POSTs includeDuplicates list', async () => {
    const promise = service.confirmImport('batch-1', ['tx-1', 'tx-2']);
    const req = httpMock.expectOne('/api/financial/imports/batch-1/confirm');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ includeDuplicates: ['tx-1', 'tx-2'] });
    req.flush({ importedCount: 0, autoCategorizedCount: 0, errorCount: 0 });
    await promise;
  });

  it('listImportBatches GETs /api/financial/imports', async () => {
    const promise = service.listImportBatches();
    const req = httpMock.expectOne('/api/financial/imports');
    expect(req.request.method).toBe('GET');
    req.flush([]);
    await promise;
  });
});
