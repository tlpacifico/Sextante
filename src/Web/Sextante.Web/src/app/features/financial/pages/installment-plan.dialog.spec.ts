import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MessageService } from 'primeng/api';
import { TransactionDto } from '../../../core/api/financial.types';
import { InstallmentPlanDialogComponent } from './installment-plan.dialog';

const purchase: TransactionDto = {
  id: 't1',
  accountId: 'card',
  categoryId: 'cat',
  occurredAt: '2026-09-10T12:00:00Z',
  amount: { amount: 600, currency: 'EUR' },
  description: 'Portátil',
  tags: [],
  direction: 'Outflow',
  kind: 'Regular',
  transferId: null,
  counterpartAccountId: null,
  createdAt: '2026-09-10T12:00:00Z',
  updatedAt: '2026-09-10T12:00:00Z',
} as TransactionDto;

describe('InstallmentPlanDialogComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [InstallmentPlanDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), MessageService],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('prefills from the purchase, previews the installment and posts the plan', async () => {
    const fixture = TestBed.createComponent(InstallmentPlanDialogComponent);
    const component = fixture.componentInstance;
    component.visible = true;
    component.accountId = 'card';
    component.purchase = purchase;
    component.ngOnChanges();
    fixture.detectChanges();

    const internals = component as unknown as {
      form: {
        getRawValue(): { description: string; totalAmount: number; firstInstallmentDate: Date };
        controls: { installmentCount: { setValue(v: number): void } };
      };
      submit(): Promise<void>;
    };
    const value = internals.form.getRawValue();
    expect(value.description).toBe('Portátil');
    expect(value.totalAmount).toBe(600);
    expect(value.firstInstallmentDate.getFullYear()).toBe(2026);
    expect(value.firstInstallmentDate.getMonth()).toBe(9);
    expect(value.firstInstallmentDate.getDate()).toBe(10);

    internals.form.controls.installmentCount.setValue(6);
    fixture.detectChanges();
    const text = (fixture.nativeElement.ownerDocument.body as HTMLElement).textContent ?? '';
    expect(text).toContain('6 ×');
    expect(text).toContain('100,00');

    let saved = false;
    component.saved.subscribe(() => (saved = true));
    const submitting = internals.submit();

    const req = httpMock.expectOne('/api/financial/installment-plans');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      accountId: 'card',
      purchaseTransactionId: 't1',
      description: 'Portátil',
      totalAmount: 600,
      installmentCount: 6,
      installmentsAlreadyPaid: 0,
      firstInstallmentDate: '2026-10-10',
      annualRate: null,
    });
    req.flush({});
    await submitting;
    expect(saved).toBeTrue();
  });

  it('updates an existing plan with PUT', async () => {
    const fixture = TestBed.createComponent(InstallmentPlanDialogComponent);
    const component = fixture.componentInstance;
    component.visible = true;
    component.accountId = 'card';
    component.plan = {
      id: 'p1',
      accountId: 'card',
      purchaseTransactionId: null,
      description: 'Frigorífico',
      totalAmount: { amount: 1200, currency: 'EUR' },
      installmentCount: 12,
      installmentsAlreadyPaid: 3,
      firstInstallmentDate: '2026-04-15',
      annualRate: 4.5,
      installmentAmount: { amount: 100, currency: 'EUR' },
      installmentsPaidOrDue: 6,
      remainingAmount: { amount: 600, currency: 'EUR' },
      nextInstallmentDate: '2026-10-15',
      isActive: true,
      schedule: [],
      createdAt: '2026-04-15T00:00:00Z',
      updatedAt: '2026-04-15T00:00:00Z',
    };
    component.ngOnChanges();
    fixture.detectChanges();

    const submitting = (component as unknown as { submit(): Promise<void> }).submit();
    const req = httpMock.expectOne('/api/financial/installment-plans/p1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.firstInstallmentDate).toBe('2026-04-15');
    expect(req.request.body.installmentsAlreadyPaid).toBe(3);
    expect(req.request.body.annualRate).toBe(4.5);
    req.flush({});
    await submitting;
  });
});
