/**
 * DTOs do módulo Financial. Espelham os records C# em
 * `Sextante.Modules.Financial.Application.Features.*`.
 */

export interface Money {
  amount: number;
  currency: string;
}

export type AccountType = 'Checking' | 'Savings' | 'Cash' | 'CreditCard';
export type CategoryKind = 'Expense' | 'Income';

export interface AccountDto {
  id: string;
  name: string;
  type: AccountType;
  currency: string;
  openingBalance: Money;
  createdAt: string;
  updatedAt: string;
}

export interface CreateAccountRequest {
  name: string;
  type: AccountType;
  currency?: string | null;
  openingBalanceAmount: number;
}

export interface UpdateAccountRequest {
  name: string;
  type: AccountType;
}

export interface CategoryDto {
  id: string;
  name: string;
  kind: CategoryKind;
  iconName: string;
  colorHex: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCategoryRequest {
  name: string;
  kind: CategoryKind;
  iconName: string;
  colorHex: string;
}

export interface UpdateCategoryRequest {
  name: string;
  iconName: string;
  colorHex: string;
}

export interface TransactionDto {
  id: string;
  accountId: string;
  categoryId: string;
  occurredAt: string;
  amount: Money;
  description: string | null;
  tags: string[];
  exchangeRateToPrimary?: number | null;
  exchangeRateAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateTransactionRequest {
  accountId: string;
  categoryId: string;
  occurredAt: string;
  amount: number;
  currency?: string | null;
  description?: string | null;
  tags?: string[] | null;
}

export interface UpdateTransactionRequest {
  accountId: string;
  categoryId: string;
  occurredAt: string;
  amount: number;
  description?: string | null;
  tags?: string[] | null;
}

export type TransactionViewMode = 'converted' | 'original';

export interface TransactionFilter {
  dateFrom?: string | null;
  dateTo?: string | null;
  categoryIds?: string[] | null;
  accountIds?: string[] | null;
  pageSize?: number | null;
  viewMode?: TransactionViewMode | null;
}

export interface TransactionsPageResponse {
  items: TransactionDto[];
  nextCursor: string | null;
}

export interface CurrencyTotals {
  currency: string;
  income: Money;
  expense: Money;
  net: Money;
}

export interface TransactionSummaryResponse {
  income: Money;
  expense: Money;
  net: Money;
  viewMode: TransactionViewMode;
  perCurrency: CurrencyTotals[] | null;
}

export interface TransactionByCategoryResponse {
  categoryId: string;
  categoryName: string;
  iconName: string;
  colorHex: string;
  total: Money;
}

export const ACCOUNT_TYPES: AccountType[] = ['Checking', 'Savings', 'Cash', 'CreditCard'];
export const CATEGORY_KINDS: CategoryKind[] = ['Expense', 'Income'];

export const ACCOUNT_TYPE_LABELS: Record<AccountType, string> = {
  Checking: 'Conta corrente',
  Savings: 'Poupança',
  Cash: 'Dinheiro',
  CreditCard: 'Cartão de crédito',
};

export const CATEGORY_KIND_LABELS: Record<CategoryKind, string> = {
  Expense: 'Despesa',
  Income: 'Receita',
};
