import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { signal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { FinancialStore } from '../state/financial.store';
import { RecurringRuleDialogComponent } from './recurring-rule.dialog';
import { AccountDto, CategoryDto } from '../../../core/api/financial.types';
import { CurrencyDto, TenantSettingsDto } from '../../../core/api/identity.types';

const MOCK_CURRENCIES: CurrencyDto[] = [
  { code: 'EUR', name: 'Euro', symbol: '€', minorUnits: 2, isActive: true },
  { code: 'USD', name: 'US Dollar', symbol: '$', minorUnits: 2, isActive: true },
];

const MOCK_ACCOUNTS: AccountDto[] = [
  {
    id: 'acc-1',
    name: 'Conta Corrente',
    type: 'Checking',
    currency: 'EUR',
    openingBalance: { amount: 1000, currency: 'EUR' },
    openingBalanceDate: '2026-01-01',
    currentBalance: { amount: 1000, currency: 'EUR' },
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

const MOCK_CATEGORIES: CategoryDto[] = [
  {
    id: 'cat-1',
    name: 'Alimentação',
    kind: 'Expense',
    iconName: 'restaurant',
    colorHex: '#FF0000',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

const MOCK_TENANT_SETTINGS: TenantSettingsDto = {
  id: 'ts-1',
  name: 'Test',
  primaryCurrency: 'EUR',
};

function createMockStore() {
  return {
    currencies: signal<CurrencyDto[]>(MOCK_CURRENCIES),
    accounts: signal<AccountDto[]>(MOCK_ACCOUNTS),
    categories: signal<CategoryDto[]>(MOCK_CATEGORIES),
    tenantSettings: signal<TenantSettingsDto | null>(MOCK_TENANT_SETTINGS),
  };
}

const RULE_FROM_API = {
  id: 'rule-1',
  description: 'Netflix',
  amount: { amount: 15.99, currency: 'EUR' },
  accountId: 'acc-1',
  categoryId: 'cat-1',
  frequency: 'Monthly',
  interval: 1,
  startDate: '2026-01-01',
  endDate: '2026-12-31',
  nextOccurrence: '2026-05-15',
  isActive: true,
  tags: ['streaming'],
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

/** Access the protected form from a test helper. */
function formOf(
  fixture: ReturnType<typeof TestBed.createComponent<RecurringRuleDialogComponent>>,
): FormGroup {
  return (fixture.componentInstance as unknown as Record<string, unknown>)['form'] as FormGroup;
}

/** Click the submit button in the dialog. */
function clickSubmit(
  fixture: ReturnType<typeof TestBed.createComponent<RecurringRuleDialogComponent>>,
): void {
  const btn = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
    'button[type="submit"]',
  )!;
  btn.click();
}

describe('RecurringRuleDialogComponent', () => {
  let httpMock: HttpTestingController;
  let mockStore: ReturnType<typeof createMockStore>;

  beforeEach(() => {
    mockStore = createMockStore();

    TestBed.configureTestingModule({
      imports: [RecurringRuleDialogComponent],
      providers: [
        { provide: FinancialStore, useValue: mockStore },
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('opens in create mode with empty form and default currency', async () => {
    const fixture = TestBed.createComponent(RecurringRuleDialogComponent);
    fixture.componentInstance.editingId = null;
    fixture.detectChanges();

    const f = formOf(fixture);
    expect(f.getRawValue().description).toBe('');
    expect(f.getRawValue().amount).toBe(0);
    expect(f.getRawValue().currency).toBe('EUR');
    expect(f.getRawValue().accountId).toBe('');
    expect(f.getRawValue().categoryId).toBeNull();
    expect(f.getRawValue().frequency).toBe('Monthly');
    expect(f.getRawValue().interval).toBe(1);
  });

  it('opens in edit mode and loads existing rule', async () => {
    const fixture = TestBed.createComponent(RecurringRuleDialogComponent);
    fixture.componentInstance.editingId = 'rule-1';
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/recurring-rules/rule-1');
    expect(req.request.method).toBe('GET');
    req.flush(RULE_FROM_API);

    await fixture.whenStable();
    fixture.detectChanges();

    const f = formOf(fixture);
    expect(f.getRawValue().description).toBe('Netflix');
    expect(f.getRawValue().amount).toBe(15.99);
    expect(f.getRawValue().currency).toBe('EUR');
    expect(f.getRawValue().accountId).toBe('acc-1');
    expect(f.getRawValue().frequency).toBe('Monthly');
    expect(f.getRawValue().interval).toBe(1);
    expect(f.getRawValue().isActive).toBeTrue();
  });

  it('submit creates rule via POST /api/financial/recurring-rules', async () => {
    const fixture = TestBed.createComponent(RecurringRuleDialogComponent);
    fixture.componentInstance.editingId = null;
    fixture.detectChanges();

    const f = formOf(fixture);
    f.patchValue({
      description: 'Supermercado',
      amount: 100,
      currency: 'EUR',
      accountId: 'acc-1',
      frequency: 'Monthly',
      interval: 1,
      startDate: new Date(2026, 4, 1),
    });
    fixture.detectChanges();

    clickSubmit(fixture);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/recurring-rules');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      description: 'Supermercado',
      amount: 100,
      currency: 'EUR',
      accountId: 'acc-1',
      categoryId: null,
      frequency: 'Monthly',
      interval: 1,
      startDate: '2026-05-01',
      endDate: null,
      tags: null,
    });
    req.flush({ ...RULE_FROM_API, id: 'new-rule' });

    await fixture.whenStable();
  });

  it('submit updates rule via PUT /api/financial/recurring-rules/:id', async () => {
    const fixture = TestBed.createComponent(RecurringRuleDialogComponent);
    fixture.componentInstance.editingId = 'rule-1';
    fixture.detectChanges();

    // flush the edit load
    httpMock.expectOne('/api/financial/recurring-rules/rule-1').flush(RULE_FROM_API);
    await fixture.whenStable();
    fixture.detectChanges();

    const f = formOf(fixture);
    f.patchValue({
      description: 'Netflix Premium',
      amount: 19.99,
      currency: 'EUR',
      accountId: 'acc-1',
      categoryId: 'cat-1',
      frequency: 'Monthly',
      interval: 1,
      startDate: new Date(2026, 0, 1),
      endDate: new Date(2026, 11, 31),
      tags: [],
      isActive: true,
    });
    fixture.detectChanges();

    clickSubmit(fixture);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/recurring-rules/rule-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      description: 'Netflix Premium',
      amount: 19.99,
      currency: 'EUR',
      accountId: 'acc-1',
      categoryId: 'cat-1',
      frequency: 'Monthly',
      interval: 1,
      startDate: '2026-01-01',
      endDate: '2026-12-31',
      tags: null,
      isActive: true,
    });
    req.flush(RULE_FROM_API);

    await fixture.whenStable();
  });

  it('submits with tags in request body', async () => {
    const fixture = TestBed.createComponent(RecurringRuleDialogComponent);
    fixture.componentInstance.editingId = null;
    fixture.detectChanges();

    const f = formOf(fixture);
    f.patchValue({
      description: 'Mercado',
      amount: 45.5,
      currency: 'EUR',
      accountId: 'acc-1',
      frequency: 'Weekly',
      interval: 1,
      startDate: new Date(2026, 4, 1),
      tags: ['compras', 'semanal'],
    });
    fixture.detectChanges();

    clickSubmit(fixture);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/recurring-rules');
    expect(req.request.body.tags).toEqual(['compras', 'semanal']);
    req.flush({ ...RULE_FROM_API, id: 'new-rule' });

    await fixture.whenStable();
  });

  it('shows startHint when startDate < today', async () => {
    const fixture = TestBed.createComponent(RecurringRuleDialogComponent);
    fixture.componentInstance.editingId = null;
    fixture.detectChanges();

    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    yesterday.setHours(0, 0, 0, 0);

    const f = formOf(fixture);
    f.patchValue({ startDate: yesterday });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Esta regra começou em');
  });

  it('does not show startHint when startDate >= today', async () => {
    const fixture = TestBed.createComponent(RecurringRuleDialogComponent);
    fixture.componentInstance.editingId = null;
    fixture.detectChanges();

    const future = new Date();
    future.setFullYear(future.getFullYear() + 1);
    future.setHours(0, 0, 0, 0);

    const f = formOf(fixture);
    f.patchValue({ startDate: future });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Esta regra começou em');
  });
});
