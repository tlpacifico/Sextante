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
});
