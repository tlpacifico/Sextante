import { Injectable, computed, inject, signal } from '@angular/core';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import {
  AccountDto,
  CategoryDto,
  TransactionByCategoryResponse,
  TransactionDto,
  TransactionFilter,
  TransactionSummaryResponse,
} from '../../../core/api/financial.types';

export type ChartKind = 'Expense' | 'Income';

const EMPTY_FILTER: TransactionFilter = {
  dateFrom: null,
  dateTo: null,
  categoryIds: null,
  accountIds: null,
  pageSize: 50,
};

const EMPTY_SUMMARY: TransactionSummaryResponse = {
  income: { amount: 0, currency: 'EUR' },
  expense: { amount: 0, currency: 'EUR' },
  net: { amount: 0, currency: 'EUR' },
};

/**
 * Store reactive (Signals + services, sem NgRx — tech-stack §19.1) que
 * agrega o estado das contas, categorias e transações para o dashboard
 * + páginas CRUD da Phase 2.
 */
@Injectable({ providedIn: 'root' })
export class FinancialStore {
  private readonly api = inject(FinancialApiService);

  private readonly _accounts = signal<AccountDto[]>([]);
  private readonly _categories = signal<CategoryDto[]>([]);
  private readonly _transactions = signal<TransactionDto[]>([]);
  private readonly _transactionsCursor = signal<string | null>(null);
  private readonly _transactionFilter = signal<TransactionFilter>(EMPTY_FILTER);
  private readonly _summary = signal<TransactionSummaryResponse>(EMPTY_SUMMARY);
  private readonly _byCategory = signal<TransactionByCategoryResponse[]>([]);
  private readonly _chartKind = signal<ChartKind>('Expense');

  readonly accounts = this._accounts.asReadonly();
  readonly categories = this._categories.asReadonly();
  readonly transactions = this._transactions.asReadonly();
  readonly transactionsCursor = this._transactionsCursor.asReadonly();
  readonly transactionFilter = this._transactionFilter.asReadonly();
  readonly summary = this._summary.asReadonly();
  readonly byCategory = this._byCategory.asReadonly();
  readonly chartKind = this._chartKind.asReadonly();

  readonly activeAccounts = computed(() => this._accounts());
  readonly expenseCategories = computed(() =>
    this._categories().filter((c) => c.kind === 'Expense'),
  );
  readonly incomeCategories = computed(() =>
    this._categories().filter((c) => c.kind === 'Income'),
  );
  readonly hasMoreTransactions = computed(() => this._transactionsCursor() !== null);

  setFilter(filter: TransactionFilter): void {
    this._transactionFilter.set({ ...filter });
  }

  setChartKind(kind: ChartKind): void {
    this._chartKind.set(kind);
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
    const filter = this._transactionFilter();
    this._summary.set(await this.api.getTransactionSummary(filter));
  }

  async loadByCategory(): Promise<void> {
    const filter = this._transactionFilter();
    const kind = this._chartKind();
    this._byCategory.set(await this.api.getTransactionsByCategory(filter, kind));
  }

  async refreshDashboard(): Promise<void> {
    await Promise.all([
      this.loadTransactions(true),
      this.loadSummary(),
      this.loadByCategory(),
    ]);
  }
}
