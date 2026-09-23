import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MessageService } from 'primeng/api';
import { TransactionsPage } from './transactions.page';

describe('TransactionsPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TransactionsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        MessageService,
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('renders sign and category from the transaction direction, including uncategorized rows', async () => {
    const fixture = TestBed.createComponent(TransactionsPage);
    fixture.detectChanges();

    // Contas e categorias vazias; a transação não tem categoria (Phase 6.5).
    httpMock.match((req) => req.url.startsWith('/api/financial/accounts')).forEach((r) => r.flush([]));
    httpMock.match((req) => req.url.startsWith('/api/financial/categories')).forEach((r) => r.flush([]));
    httpMock.match((req) => req.url.startsWith('/api/financial/transactions')).forEach((r) =>
      r.flush({
        items: [
          {
            id: 't1',
            accountId: 'a1',
            categoryId: null,
            occurredAt: '2026-09-20T00:00:00Z',
            amount: { amount: 12, currency: 'EUR' },
            description: 'Reembolso',
            tags: [],
            direction: 'Inflow',
            kind: 'Regular',
            transferId: null,
            createdAt: '2026-09-20T00:00:00Z',
            updatedAt: '2026-09-20T00:00:00Z',
          },
        ],
        nextCursor: null,
      }),
    );

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Sem categoria');
    expect(text).toContain('+12');
  });

  it('shows reconciliation adjustments as "Acerto" with delete but no edit action', async () => {
    const fixture = TestBed.createComponent(TransactionsPage);
    fixture.detectChanges();

    httpMock.match((req) => req.url.startsWith('/api/financial/accounts')).forEach((r) => r.flush([]));
    httpMock.match((req) => req.url.startsWith('/api/financial/categories')).forEach((r) => r.flush([]));
    httpMock.match((req) => req.url.startsWith('/api/financial/transactions')).forEach((r) =>
      r.flush({
        items: [
          {
            id: 't-adj',
            accountId: 'a1',
            categoryId: null,
            occurredAt: '2026-09-20T23:59:59Z',
            amount: { amount: 15, currency: 'EUR' },
            description: 'Acerto de saldo',
            tags: [],
            direction: 'Outflow',
            kind: 'Adjustment',
            transferId: null,
            counterpartAccountId: null,
            createdAt: '2026-09-20T00:00:00Z',
            updatedAt: '2026-09-20T00:00:00Z',
          },
        ],
        nextCursor: null,
      }),
    );

    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent ?? '').toContain('Acerto');
    const row = el.querySelector('tbody tr') as HTMLElement;
    const actionsCell = row.querySelector('td:last-child') as HTMLElement;
    expect(actionsCell.querySelectorAll('p-button').length).toBe(1);
    expect(actionsCell.querySelector('.pi-pencil')).toBeNull();

    const kinds = (fixture.componentInstance as unknown as { kindOptions: { value: string | null }[] })
      .kindOptions.map((o) => o.value);
    expect(kinds).toContain('Adjustment');
    expect(kinds).toContain('Transfer');
  });

  it('offers "Isto foi em prestações" only for expenses on credit card accounts', async () => {
    const fixture = TestBed.createComponent(TransactionsPage);
    fixture.detectChanges();

    httpMock.match((req) => req.url.startsWith('/api/financial/accounts')).forEach((r) =>
      r.flush([
        { id: 'card', name: 'Cartão', type: 'CreditCard', currency: 'EUR' },
        { id: 'chk', name: 'Conta', type: 'Checking', currency: 'EUR' },
      ]),
    );
    httpMock.match((req) => req.url.startsWith('/api/financial/categories')).forEach((r) => r.flush([]));
    const tx = (id: string, accountId: string) => ({
      id,
      accountId,
      categoryId: null,
      occurredAt: '2026-09-20T12:00:00Z',
      amount: { amount: 600, currency: 'EUR' },
      description: 'Compra',
      tags: [],
      direction: 'Outflow',
      kind: 'Regular',
      transferId: null,
      counterpartAccountId: null,
      createdAt: '2026-09-20T00:00:00Z',
      updatedAt: '2026-09-20T00:00:00Z',
    });
    httpMock.match((req) => req.url.startsWith('/api/financial/transactions')).forEach((r) =>
      r.flush({ items: [tx('t-card', 'card'), tx('t-chk', 'chk')], nextCursor: null }),
    );

    await fixture.whenStable();
    fixture.detectChanges();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');
    expect(rows[0].querySelector('[aria-label="Isto foi em prestações"]')).toBeTruthy();
    expect(rows[1].querySelector('[aria-label="Isto foi em prestações"]')).toBeNull();
  });
});
