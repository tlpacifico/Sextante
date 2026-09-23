import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { CreditCardViewDto } from '../../../core/api/financial.types';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { CreditCardPage } from './credit-card.page';

const money = (amount: number) => ({ amount, currency: 'EUR' });

function view(overrides: Partial<CreditCardViewDto> = {}): CreditCardViewDto {
  return {
    accountId: 'a1',
    currentBalance: money(-870),
    currentDebt: money(870),
    settings: { creditLimit: money(2000), statementClosingDay: 20, paymentDueDay: 10, paymentAccountId: 'chk' },
    available: money(1130),
    currentCycle: {
      start: '2026-09-21',
      end: '2026-10-20',
      paymentDueDate: '2026-11-10',
      spent: money(70),
      paymentsReceived: money(400),
    },
    previousCycle: {
      start: '2026-08-21',
      end: '2026-09-20',
      paymentDueDate: '2026-10-10',
      spent: money(200),
      paymentsReceived: money(0),
    },
    previousClosingDebt: money(1200),
    nextPaymentDueDate: '2026-10-10',
    nextPaymentAmount: money(800),
    paymentAccountId: 'chk',
    unbilledInstallmentsAtPreviousClose: money(0),
    ...overrides,
  };
}

describe('CreditCardPage', () => {
  let httpMock: HttpTestingController;
  const pipe = new MoneyPipe();

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CreditCardPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        MessageService,
        ConfirmationService,
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'a1' }) } } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  async function render(dto: CreditCardViewDto) {
    const fixture = TestBed.createComponent(CreditCardPage);
    fixture.detectChanges();

    httpMock.expectOne('/api/financial/accounts/a1/credit-card').flush(dto);
    httpMock.match((req) => req.url === '/api/financial/accounts').forEach((r) =>
      r.flush([
        { id: 'a1', name: 'Cartão Ouro', type: 'CreditCard', currency: 'EUR' },
        { id: 'chk', name: 'Conta à ordem', type: 'Checking', currency: 'EUR' },
      ]),
    );
    httpMock.match((req) => !req.url.startsWith('/api/financial/transactions')).forEach((r) => r.flush([]));
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('shows debt, available, next payment and loads the current cycle movements', async () => {
    const fixture = await render(view());

    const movements = httpMock.expectOne((req) => req.url === '/api/financial/transactions');
    expect(movements.request.params.getAll('accountIds')).toEqual(['a1']);
    expect(movements.request.params.get('dateFrom')).toBe('2026-09-21T00:00:00Z');
    expect(movements.request.params.get('dateTo')).toBe('2026-10-20T23:59:59Z');
    movements.flush({ items: [], nextCursor: null });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(pipe.transform(money(870)));
    expect(text).toContain(pipe.transform(money(1130)));
    expect(text).toContain(pipe.transform(money(800)));
    expect(text).toContain('10/10/2026');
    expect(text).toContain('Conta à ordem');
    expect(text).toContain('Cartão Ouro');
  });

  it('asks to configure the card when it has no settings', async () => {
    const fixture = await render(
      view({
        settings: null,
        available: null,
        currentCycle: null,
        previousCycle: null,
        previousClosingDebt: null,
        nextPaymentDueDate: null,
        nextPaymentAmount: null,
        paymentAccountId: null,
      }),
    );

    httpMock.expectNone((req) => req.url === '/api/financial/transactions');
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Configure o limite');
    expect(text).toContain(pipe.transform(money(870)));
  });

  it('lists active installment plans and explains the unbilled discount', async () => {
    const fixture = await render(view({ unbilledInstallmentsAtPreviousClose: money(500) }));

    httpMock.match((req) => req.url === '/api/financial/transactions').forEach((r) => r.flush({ items: [], nextCursor: null }));
    const plans = httpMock.expectOne((req) => req.url === '/api/financial/installment-plans');
    expect(plans.request.params.get('accountId')).toBe('a1');
    plans.flush([
      {
        id: 'p1',
        accountId: 'a1',
        purchaseTransactionId: null,
        purchaseDate: '2026-06-20',
        description: 'Portátil',
        totalAmount: money(600),
        installmentCount: 6,
        installmentsAlreadyPaid: 0,
        firstInstallmentDate: '2026-07-10',
        annualRate: null,
        installmentAmount: money(100),
        installmentsPaidOrDue: 3,
        remainingAmount: money(300),
        nextInstallmentDate: '2026-10-10',
        isActive: true,
        schedule: [],
        createdAt: '2026-07-10T00:00:00Z',
        updatedAt: '2026-07-10T00:00:00Z',
      },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Portátil');
    expect(text).toContain('prestação 3 de 6');
    expect(text).toContain(pipe.transform(money(300)));
    expect(text).toContain('prestações por faturar');
    expect(text).toContain(pipe.transform(money(500)));
  });
});
