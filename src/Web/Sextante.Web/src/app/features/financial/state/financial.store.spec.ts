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
});
