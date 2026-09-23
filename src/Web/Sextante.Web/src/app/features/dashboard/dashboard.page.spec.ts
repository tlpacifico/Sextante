import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { DashboardPage } from './dashboard.page';

describe('DashboardPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        MessageService,
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('shows "Sem categoria" instead of a blank cell for uncategorized transactions', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    fixture.detectChanges();

    httpMock.match((req) => req.url.startsWith('/api/financial/accounts')).forEach((r) => r.flush([]));
    httpMock.match((req) => req.url.startsWith('/api/financial/categories')).forEach((r) => r.flush([]));
    httpMock.match((req) => req.url.startsWith('/api/currencies')).forEach((r) => r.flush([]));
    httpMock.match((req) => req.url.startsWith('/api/tenants/me')).forEach((r) =>
      r.flush({ id: 'tenant-1', name: 'Tenant', primaryCurrency: 'EUR' }),
    );
    httpMock.match((req) => req.url.startsWith('/api/financial/budgets')).forEach((r) => r.flush([]));

    // Estas 5 chamadas (accounts/categories/currencies/tenant/budgets) resolvem
    // via Promise.all no ngOnInit; só depois disso é que o componente dispara
    // refreshDashboard() (transactions/summary/by-category). Um tick de
    // microtasks é necessário entre os dois lotes de pedidos HTTP.
    await fixture.whenStable();

    httpMock
      .match((req) => req.url.startsWith('/api/financial/transactions/summary'))
      .forEach((r) =>
        r.flush({
          income: { amount: 0, currency: 'EUR' },
          expense: { amount: 12, currency: 'EUR' },
          net: { amount: -12, currency: 'EUR' },
          viewMode: 'converted',
          perCurrency: null,
        }),
      );
    httpMock
      .match((req) => req.url.startsWith('/api/financial/transactions/by-category'))
      .forEach((r) => r.flush([]));
    httpMock
      .match(
        (req) =>
          req.url.startsWith('/api/financial/transactions') &&
          !req.url.includes('/summary') &&
          !req.url.includes('/by-category'),
      )
      .forEach((r) =>
        r.flush({
          items: [
            {
              id: 't1',
              accountId: 'a1',
              categoryId: null,
              occurredAt: '2026-09-20T00:00:00Z',
              amount: { amount: 12, currency: 'EUR' },
              description: 'Sem categoria associada',
              tags: [],
              direction: 'Outflow',
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

    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';
    expect(text).toContain('Sem categoria');
    expect(element.querySelector('.pi-tag')).toBeTruthy();
  });
});
