import { Injectable, computed, inject, signal } from '@angular/core';
import { CurrencyApiService } from '../../../core/api/currency-api.service';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import { TenantApiService } from '../../../core/api/tenant-api.service';
import {
  AccountDto,
  CategoryDto,
  TransactionByCategoryResponse,
  TransactionDto,
  TransactionFilter,
  TransactionSummaryResponse,
  TransactionViewMode,
} from '../../../core/api/financial.types';
import {
  CurrencyDto,
  TenantSettingsDto,
  UpdateTenantSettingsRequest,
} from '../../../core/api/identity.types';

export type ChartKind = 'Expense' | 'Income';

const DEFAULT_PRIMARY_CURRENCY = 'EUR';

const EMPTY_FILTER: TransactionFilter = {
  dateFrom: null,
  dateTo: null,
  categoryIds: null,
  accountIds: null,
  pageSize: 50,
};

const EMPTY_SUMMARY: TransactionSummaryResponse = {
  income: { amount: 0, currency: DEFAULT_PRIMARY_CURRENCY },
  expense: { amount: 0, currency: DEFAULT_PRIMARY_CURRENCY },
  net: { amount: 0, currency: DEFAULT_PRIMARY_CURRENCY },
  viewMode: 'converted',
  perCurrency: null,
};

/**
 * Store reactive (Signals + services, sem NgRx — tech-stack §19.1) que
 * agrega o estado das contas, categorias, transações, currencies e
 * tenant settings para o dashboard + páginas Phase 2/3.
 */
@Injectable({ providedIn: 'root' })
export class FinancialStore {
  private readonly api = inject(FinancialApiService);
  private readonly currencyApi = inject(CurrencyApiService);
  private readonly tenantApi = inject(TenantApiService);

  private readonly _accounts = signal<AccountDto[]>([]);
  private readonly _categories = signal<CategoryDto[]>([]);
  private readonly _transactions = signal<TransactionDto[]>([]);
  private readonly _transactionsCursor = signal<string | null>(null);
  private readonly _transactionFilter = signal<TransactionFilter>(EMPTY_FILTER);
  private readonly _summary = signal<TransactionSummaryResponse>(EMPTY_SUMMARY);
  private readonly _byCategory = signal<TransactionByCategoryResponse[]>([]);
  private readonly _chartKind = signal<ChartKind>('Expense');
  private readonly _viewMode = signal<TransactionViewMode>('converted');
  private readonly _currencies = signal<CurrencyDto[]>([]);
  private readonly _tenantSettings = signal<TenantSettingsDto | null>(null);

  readonly accounts = this._accounts.asReadonly();
  readonly categories = this._categories.asReadonly();
  readonly transactions = this._transactions.asReadonly();
  readonly transactionsCursor = this._transactionsCursor.asReadonly();
  readonly transactionFilter = this._transactionFilter.asReadonly();
  readonly summary = this._summary.asReadonly();
  readonly byCategory = this._byCategory.asReadonly();
  readonly chartKind = this._chartKind.asReadonly();
  readonly viewMode = this._viewMode.asReadonly();
  readonly currencies = this._currencies.asReadonly();
  readonly tenantSettings = this._tenantSettings.asReadonly();

  readonly activeAccounts = computed(() => this._accounts());
  readonly expenseCategories = computed(() =>
    this._categories().filter((c) => c.kind === 'Expense'),
  );
  readonly incomeCategories = computed(() =>
    this._categories().filter((c) => c.kind === 'Income'),
  );
  readonly hasMoreTransactions = computed(() => this._transactionsCursor() !== null);
  readonly primaryCurrency = computed(
    () => this._tenantSettings()?.primaryCurrency ?? DEFAULT_PRIMARY_CURRENCY,
  );

  setFilter(filter: TransactionFilter): void {
    this._transactionFilter.set({ ...filter });
  }

  setChartKind(kind: ChartKind): void {
    this._chartKind.set(kind);
  }

  async setViewMode(mode: TransactionViewMode): Promise<void> {
    if (this._viewMode() === mode) return;
    this._viewMode.set(mode);
    await Promise.all([this.loadSummary(), this.loadByCategory()]);
  }

  async loadAccounts(): Promise<void> {
    this._accounts.set(await this.api.listAccounts());
  }

  async loadCategories(): Promise<void> {
    this._categories.set(await this.api.listCategories());
  }

  async loadTransactions(reset = true): Promise<void> {
    const filter = this._transactionFilter();
    const cursor = reset ? null : this._transactionsCursor();
    const page = await this.api.listTransactions(filter, cursor);

    if (reset) {
      this._transactions.set(page.items);
    } else {
      this._transactions.update((current) => [...current, ...page.items]);
    }
    this._transactionsCursor.set(page.nextCursor);
  }

  async loadSummary(): Promise<void> {
    const filter = { ...this._transactionFilter(), viewMode: this._viewMode() };
    this._summary.set(await this.api.getTransactionSummary(filter));
  }

  async loadByCategory(): Promise<void> {
    const filter = { ...this._transactionFilter(), viewMode: this._viewMode() };
    const kind = this._chartKind();
    this._byCategory.set(await this.api.getTransactionsByCategory(filter, kind));
  }

  async loadCurrencies(force = false): Promise<void> {
    if (!force && this._currencies().length > 0) return;
    this._currencies.set(await this.currencyApi.listActive());
  }

  async loadTenantSettings(force = false): Promise<void> {
    if (!force && this._tenantSettings() !== null) return;
    this._tenantSettings.set(await this.tenantApi.getSettings());
  }

  async updateTenantSettings(req: UpdateTenantSettingsRequest): Promise<void> {
    const updated = await this.tenantApi.updateSettings(req);
    this._tenantSettings.set(updated);
    await this.refreshDashboard();
  }

  async refreshDashboard(): Promise<void> {
    await Promise.all([
      this.loadTransactions(true),
      this.loadSummary(),
      this.loadByCategory(),
    ]);
  }
}
