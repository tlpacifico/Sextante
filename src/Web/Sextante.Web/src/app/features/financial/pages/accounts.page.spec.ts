import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
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
        provideRouter([]),
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

  it('sends the opening balance date as the local calendar day, not the UTC-shifted one', async () => {
    const fixture = TestBed.createComponent(AccountsPage);
    fixture.detectChanges();
    httpMock.match(() => true).forEach((req) => req.flush([]));

    const component = fixture.componentInstance as unknown as {
      form: { patchValue(value: Record<string, unknown>): void };
      submit(): Promise<void>;
    };

    // Meia-noite local a 1 de agosto de 2026 — em qualquer fuso horário a
    // leste de UTC (ex.: Lisboa no verão, UTC+1), `.toISOString()` desloca
    // esta data para 31/07 em UTC. O pedido enviado ao backend tem de manter
    // "2026-08-01", a data efetivamente escolhida no calendário local.
    component.form.patchValue({
      name: 'Conta de teste',
      openingBalanceDate: new Date(2026, 7, 1),
    });
    const submitting = component.submit();

    const req = httpMock.expectOne('/api/financial/accounts');
    expect(req.request.body.openingBalanceDate).toBe('2026-08-01');
    req.flush({
      id: 'a1',
      name: '',
      type: 'Checking',
      currency: 'EUR',
      openingBalance: { amount: 0, currency: 'EUR' },
      openingBalanceDate: '2026-08-01',
      currentBalance: { amount: 0, currency: 'EUR' },
      createdAt: '2026-08-01T00:00:00Z',
      updatedAt: '2026-08-01T00:00:00Z',
    });
    // `submit()` recarrega a lista de contas após criar — a chamada só chega
    // ao HttpTestingController depois de um "tick" de microtasks.
    await fixture.whenStable();
    httpMock.expectOne('/api/financial/accounts').flush([]);
    await submitting;
  });

  function cardAccount(overrides: Record<string, unknown> = {}) {
    return {
      id: 'c1',
      name: 'Cartão Ouro',
      type: 'CreditCard',
      currency: 'EUR',
      openingBalance: { amount: 0, currency: 'EUR' },
      openingBalanceDate: '2026-01-01',
      currentBalance: { amount: -500, currency: 'EUR' },
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      creditCard: null,
      ...overrides,
    };
  }

  it('shows credit card settings for the CreditCard type and sends them on create', async () => {
    const fixture = TestBed.createComponent(AccountsPage);
    fixture.detectChanges();
    httpMock.match(() => true).forEach((req) => req.flush([]));

    const component = fixture.componentInstance as unknown as {
      form: { patchValue(value: Record<string, unknown>): void };
      openCreate(): void;
      submit(): Promise<void>;
    };
    component.openCreate();
    component.form.patchValue({ name: 'Cartão', type: 'CreditCard', openingBalance: -100 });
    component.form.patchValue({ creditLimit: 2000, statementClosingDay: 20, paymentDueDay: 10 });
    fixture.detectChanges();
    await fixture.whenStable();

    const body = (fixture.nativeElement.ownerDocument.body as HTMLElement).textContent ?? '';
    expect(body).toContain('Definições do cartão');

    const submitting = component.submit();
    const req = httpMock.expectOne('/api/financial/accounts');
    expect(req.request.body.creditCard).toEqual({
      creditLimit: 2000,
      statementClosingDay: 20,
      paymentDueDay: 10,
      paymentAccountId: null,
    });
    req.flush(cardAccount());
    await fixture.whenStable();
    httpMock.match('/api/financial/accounts').forEach((r) => r.flush([]));
    await submitting;
  });

  it('sends no credit card settings for other account types', async () => {
    const fixture = TestBed.createComponent(AccountsPage);
    fixture.detectChanges();
    httpMock.match(() => true).forEach((req) => req.flush([]));

    const component = fixture.componentInstance as unknown as {
      form: { patchValue(value: Record<string, unknown>): void };
      openCreate(): void;
      submit(): Promise<void>;
    };
    component.openCreate();
    component.form.patchValue({ name: 'Conta', type: 'Checking' });

    const submitting = component.submit();
    const req = httpMock.expectOne('/api/financial/accounts');
    expect(req.request.body.creditCard).toBeNull();
    req.flush(cardAccount({ type: 'Checking' }));
    await fixture.whenStable();
    httpMock.match('/api/financial/accounts').forEach((r) => r.flush([]));
    await submitting;
  });

  it('links credit card rows to the card page', async () => {
    const fixture = TestBed.createComponent(AccountsPage);
    fixture.detectChanges();

    httpMock.match((req) => req.url.startsWith('/api/financial/accounts')).forEach((r) => r.flush([cardAccount()]));
    httpMock.match(() => true).forEach((r) => r.flush([]));
    await fixture.whenStable();
    fixture.detectChanges();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/app/accounts/c1/credit-card"]');
    expect(link).toBeTruthy();
  });
});
