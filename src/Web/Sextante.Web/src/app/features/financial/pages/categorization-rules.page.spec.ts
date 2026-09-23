import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { CategorizationRulesPage } from './categorization-rules.page';

const CATEGORY = {
  id: 'cat-1', name: 'Supermercado', kind: 'Expense', iconName: 'pi-shopping-cart', colorHex: '#0EA5E9',
  createdAt: '2026-04-01T00:00:00Z', updatedAt: '2026-04-01T00:00:00Z',
};

const CARD = {
  id: 'a2', name: 'Cartão', type: 'CreditCard', currency: 'EUR',
  openingBalance: { amount: 0, currency: 'EUR' }, openingBalanceDate: '2026-08-01',
  currentBalance: { amount: 0, currency: 'EUR' }, createdAt: '', updatedAt: '', creditCard: null,
};

const CATEGORY_RULE = {
  id: 'r1', name: 'Continente', pattern: 'CONTINENTE',
  matchType: 'Contains', categoryId: 'cat-1',
  categoryName: 'Supermercado', categoryIcon: 'pi-shopping-cart',
  categoryColor: '#0EA5E9', priority: 1, isActive: true,
  createdAt: '2026-04-01T00:00:00Z', updatedAt: '2026-04-01T00:00:00Z',
  action: 'SetCategory', targetAccountId: null, targetAccountName: null,
};

const TRANSFER_RULE = {
  ...CATEGORY_RULE,
  id: 'r2', name: 'Pagamento cartão', pattern: 'PAGAMENTO CARTAO', categoryId: null,
  categoryName: '—', priority: 2,
  action: 'MarkAsTransfer', targetAccountId: 'a2', targetAccountName: 'Cartão',
};

describe('CategorizationRulesPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CategorizationRulesPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function create(rules: unknown[]) {
    const fixture = TestBed.createComponent(CategorizationRulesPage);
    fixture.detectChanges();
    httpMock.expectOne('/api/financial/categorization-rules').flush(rules);
    httpMock.expectOne('/api/financial/categories').flush([CATEGORY]);
    httpMock.expectOne('/api/financial/accounts').flush([CARD]);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('loads rules, categories and accounts on init', async () => {
    const fixture = await create([CATEGORY_RULE]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Continente');
  });

  it('renders the "Nova regra" action', async () => {
    const fixture = await create([]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Nova regra');
  });

  it('shows the target account of a transfer rule', async () => {
    const fixture = await create([TRANSFER_RULE]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Transferência → Cartão');
  });

  it('creates a transfer rule with the target account and no category', async () => {
    const fixture = await create([]);
    const page = fixture.componentInstance as any;
    page.openCreate();
    page.form.patchValue({ name: 'Pagamento cartão', pattern: 'PAGAMENTO CARTAO', action: 'MarkAsTransfer' });
    page.form.controls.targetAccountId.setValue('a2');
    fixture.detectChanges();

    expect(page.form.controls.categoryId.enabled).toBeFalse();
    expect(page.form.valid).toBeTrue();

    const done = page.submit();
    const req = httpMock.expectOne('/api/financial/categorization-rules');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'Pagamento cartão',
      pattern: 'PAGAMENTO CARTAO',
      matchType: 'Contains',
      categoryId: null,
      priority: 1,
      action: 'MarkAsTransfer',
      targetAccountId: 'a2',
    });
    req.flush(TRANSFER_RULE);
    await new Promise((resolve) => setTimeout(resolve));
    httpMock.expectOne('/api/financial/categorization-rules').flush([TRANSFER_RULE]);
    await done;
  });
});
