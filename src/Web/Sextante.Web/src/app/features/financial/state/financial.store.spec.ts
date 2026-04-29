import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { FinancialStore } from './financial.store';

describe('FinancialStore', () => {
  let store: FinancialStore;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), FinancialStore],
    });
    store = TestBed.inject(FinancialStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loadTransactions(reset=true) replaces transactions and stores cursor', async () => {
    const p = store.loadTransactions(true);
    const req = httpMock.expectOne((r) => r.url === '/api/financial/transactions');
    req.flush({
      items: [
        { id: '1', accountId: 'a', categoryId: 'c', occurredAt: 't', amount: { amount: 10, currency: 'EUR' }, description: null, tags: [], createdAt: 't', updatedAt: 't' },
      ],
      nextCursor: 'cursor-1',
    });
    await p;

    expect(store.transactions().length).toBe(1);
    expect(store.transactionsCursor()).toBe('cursor-1');
    expect(store.hasMoreTransactions()).toBeTrue();
  });

  it('loadTransactions(reset=false) appends to existing list', async () => {
    const first = store.loadTransactions(true);
    const r1 = httpMock.expectOne((r) => r.url === '/api/financial/transactions');
    r1.flush({
      items: [
        { id: '1', accountId: 'a', categoryId: 'c', occurredAt: 't', amount: { amount: 10, currency: 'EUR' }, description: null, tags: [], createdAt: 't', updatedAt: 't' },
      ],
      nextCursor: 'cursor-1',
    });
    await first;

    const second = store.loadTransactions(false);
    const r2 = httpMock.expectOne((r) => r.url === '/api/financial/transactions');
    r2.flush({
      items: [
        { id: '2', accountId: 'a', categoryId: 'c', occurredAt: 't', amount: { amount: 20, currency: 'EUR' }, description: null, tags: [], createdAt: 't', updatedAt: 't' },
      ],
      nextCursor: null,
    });
    await second;

    expect(store.transactions().length).toBe(2);
    expect(store.transactionsCursor()).toBeNull();
    expect(store.hasMoreTransactions()).toBeFalse();
  });

  it('expenseCategories filters by Expense kind', async () => {
    const p = store.loadCategories();
    const req = httpMock.expectOne('/api/financial/categories');
    req.flush([
      { id: '1', name: 'A', kind: 'Expense', iconName: 'pi-tag', colorHex: '#000000', createdAt: 't', updatedAt: 't' },
      { id: '2', name: 'B', kind: 'Income', iconName: 'pi-tag', colorHex: '#000000', createdAt: 't', updatedAt: 't' },
    ]);
    await p;

    expect(store.expenseCategories().length).toBe(1);
    expect(store.incomeCategories().length).toBe(1);
  });

  it('loadCurrencies caches active currencies', async () => {
    const p = store.loadCurrencies();
    const req = httpMock.expectOne('/api/currencies');
    expect(req.request.method).toBe('GET');
    req.flush([
      { code: 'EUR', name: 'Euro', symbol: '€', minorUnits: 2, isActive: true },
      { code: 'USD', name: 'Dollar', symbol: '$', minorUnits: 2, isActive: true },
    ]);
    await p;

    expect(store.currencies().length).toBe(2);

    // Second call without force is a no-op (cached).
    await store.loadCurrencies();
    httpMock.expectNone('/api/currencies');
  });

  it('loadTenantSettings caches tenant settings', async () => {
    const p = store.loadTenantSettings();
    const req = httpMock.expectOne('/api/tenants/me');
    req.flush({ id: 't1', name: 'Tenant', primaryCurrency: 'EUR' });
    await p;

    expect(store.tenantSettings()?.primaryCurrency).toBe('EUR');
    expect(store.primaryCurrency()).toBe('EUR');
  });

  it('setViewMode toggles and reloads summary + byCategory', async () => {
    expect(store.viewMode()).toBe('converted');

    const p = store.setViewMode('original');

    const summaryReq = httpMock.expectOne((r) =>
      r.url === '/api/financial/transactions/summary',
    );
    expect(summaryReq.request.params.get('viewMode')).toBe('original');
    summaryReq.flush({
      income: { amount: 0, currency: 'EUR' },
      expense: { amount: 0, currency: 'EUR' },
      net: { amount: 0, currency: 'EUR' },
      viewMode: 'original',
      perCurrency: [
        {
          currency: 'USD',
          income: { amount: 300, currency: 'USD' },
          expense: { amount: 0, currency: 'USD' },
          net: { amount: 300, currency: 'USD' },
        },
      ],
    });

    const byCatReq = httpMock.expectOne((r) =>
      r.url === '/api/financial/transactions/by-category',
    );
    expect(byCatReq.request.params.get('viewMode')).toBe('original');
    byCatReq.flush([]);

    await p;
    expect(store.viewMode()).toBe('original');
    expect(store.summary().perCurrency?.length).toBe(1);
  });
});
