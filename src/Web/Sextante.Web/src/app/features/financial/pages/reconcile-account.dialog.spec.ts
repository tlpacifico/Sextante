import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MessageService } from 'primeng/api';
import { AccountDto } from '../../../core/api/financial.types';
import { ReconcileAccountDialogComponent } from './reconcile-account.dialog';

function localDateString(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function account(overrides: Partial<AccountDto> = {}): AccountDto {
  return {
    id: 'a1',
    name: 'Conta à ordem',
    type: 'Checking',
    currency: 'EUR',
    openingBalance: { amount: 100, currency: 'EUR' },
    openingBalanceDate: '2026-01-01',
    currentBalance: { amount: 80, currency: 'EUR' },
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    creditCard: null,
    ...overrides,
  };
}

describe('ReconcileAccountDialogComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ReconcileAccountDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        MessageService,
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('previews the difference against the calculated balance and submits the reconciliation', async () => {
    const fixture = TestBed.createComponent(ReconcileAccountDialogComponent);
    const component = fixture.componentInstance;
    component.visible = true;
    component.account = account();
    component.ngOnChanges();
    fixture.detectChanges();

    const today = localDateString(new Date());
    const balanceReq = httpMock.expectOne(
      (req) => req.url === '/api/financial/accounts/a1/balance' && req.params.get('at') === today,
    );
    balanceReq.flush({ accountId: 'a1', at: today, balance: { amount: 80, currency: 'EUR' } });
    await fixture.whenStable();

    const internals = component as unknown as {
      form: { controls: { actualBalance: { setValue(v: number): void } } };
      difference(): number | null;
      submit(): Promise<void>;
    };
    internals.form.controls.actualBalance.setValue(95);
    fixture.detectChanges();

    expect(internals.difference()).toBe(15);
    const text = (fixture.nativeElement.ownerDocument.body as HTMLElement).textContent ?? '';
    expect(text).toContain('+15');
    expect(text).toContain('entrada');

    let saved = false;
    component.saved.subscribe(() => (saved = true));
    const submitting = internals.submit();

    const post = httpMock.expectOne('/api/financial/accounts/a1/reconcile');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ date: today, actualBalance: 95 });
    post.flush({
      accountId: 'a1',
      date: today,
      calculatedBalance: { amount: 80, currency: 'EUR' },
      actualBalance: { amount: 95, currency: 'EUR' },
      difference: { amount: 15, currency: 'EUR' },
      adjustment: null,
    });
    await submitting;

    expect(saved).toBeTrue();
  });

  it('explains that credit card debt is negative', async () => {
    const fixture = TestBed.createComponent(ReconcileAccountDialogComponent);
    const component = fixture.componentInstance;
    component.visible = true;
    component.account = account({ type: 'CreditCard', name: 'Cartão' });
    component.ngOnChanges();
    fixture.detectChanges();

    httpMock.match((req) => req.url.startsWith('/api/financial/accounts/a1/balance')).forEach((r) =>
      r.flush({ accountId: 'a1', at: null, balance: { amount: -500, currency: 'EUR' } }),
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement.ownerDocument.body as HTMLElement).textContent ?? '';
    expect(text).toContain('Dívida é negativa');
  });
});
