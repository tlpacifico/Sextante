import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ConfirmationService, MessageService } from 'primeng/api';
import { AccountsPage } from './accounts.page';

describe('AccountsPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        MessageService,
        ConfirmationService,
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('shows "Dívida" and highlights negative balance for a credit card account', async () => {
    const fixture = TestBed.createComponent(AccountsPage);
    fixture.detectChanges();

    httpMock.match((req) => req.url.startsWith('/api/financial/accounts')).forEach((r) =>
      r.flush([
        {
          id: 'a1',
          name: 'Cartão Ouro',
          type: 'CreditCard',
          currency: 'EUR',
          openingBalance: { amount: 0, currency: 'EUR' },
          openingBalanceDate: '2026-01-01',
          currentBalance: { amount: -500, currency: 'EUR' },
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
        },
      ]),
    );
    httpMock.match((req) => req.url.startsWith('/api/currencies')).forEach((r) => r.flush([]));
    httpMock.match((req) => req.url.startsWith('/api/tenants/me')).forEach((r) =>
      r.flush({ id: 't1', name: 'Tenant', primaryCurrency: 'EUR' }),
    );

    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('Dívida');
    expect(el.querySelector('.text-red-600')).toBeTruthy();
  });

  it('explains why an account with active transactions cannot be archived', async () => {
    const fixture = TestBed.createComponent(AccountsPage);
    fixture.detectChanges();
    // Pedidos de arranque (contas, moedas, definições do tenant) — irrelevantes aqui.
    httpMock.match(() => true).forEach((req) => req.flush([]));

    const toast = fixture.debugElement.injector.get(MessageService);
    const add = spyOn(toast, 'add');

    const archiving = (fixture.componentInstance as unknown as {
      archive(id: string): Promise<void>;
    }).archive('acc-1');

    httpMock
      .expectOne('/api/financial/accounts/acc-1')
      .flush(
        { title: 'Erros de validação', errors: { account: ['x'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await archiving;

    expect(add).toHaveBeenCalledWith(
      jasmine.objectContaining({
        severity: 'error',
        detail: 'Não é possível arquivar uma conta com transações ativas.',
      }),
    );
  });
});
