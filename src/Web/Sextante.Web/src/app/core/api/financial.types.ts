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
  openingBalanceDate: string;
  currentBalance: Money;
  createdAt: string;
  updatedAt: string;
  /** Só em cartões configurados (Phase 6.5 grupo 5). */
  creditCard: CreditCardSettingsDto | null;
}

// Phase 6.5 grupo 5 — definições e vista do cartão de crédito.
export interface CreditCardSettingsDto {
  creditLimit: Money;
  statementClosingDay: number;
  paymentDueDay: number;
  paymentAccountId: string | null;
}

export interface CreditCardSettingsInput {
  /** Na moeda da conta. */
  creditLimit: number;
  statementClosingDay: number;
  paymentDueDay: number;
  paymentAccountId: string | null;
}

export interface CreditCardCycleDto {
  start: string;
  end: string;
  paymentDueDate: string;
  spent: Money;
  paymentsReceived: Money;
}

/** Sem `settings`, só `currentBalance` e `currentDebt` vêm preenchidos. */
export interface CreditCardViewDto {
  accountId: string;
  currentBalance: Money;
  currentDebt: Money;
  settings: CreditCardSettingsDto | null;
  available: Money | null;
  currentCycle: CreditCardCycleDto | null;
  previousCycle: CreditCardCycleDto | null;
  previousClosingDebt: Money | null;
  nextPaymentDueDate: string | null;
  nextPaymentAmount: Money | null;
  paymentAccountId: string | null;
}

export interface CreateAccountRequest {
  name: string;
  type: AccountType;
  currency?: string | null;
  openingBalanceAmount: number;
  openingBalanceDate?: string | null;
  creditCard?: CreditCardSettingsInput | null;
}

export interface AccountBalanceResponse {
  accountId: string;
  balance: Money;
  at: string;
}

// Phase 6.5 grupo 4 — acerto de saldo (reconciliação).
export interface ReconcileAccountRequest {
  /** YYYY-MM-DD, data local (nunca via toISOString). */
  date: string;
  actualBalance: number;
}

export interface ReconcileAccountResponse {
  accountId: string;
  date: string;
  calculatedBalance: Money;
  actualBalance: Money;
  /** Real − calculado, com sinal. */
  difference: Money;
  /** null quando não há diferença. */
  adjustment: TransactionDto | null;
}

export interface UpdateAccountRequest {
  name: string;
  type: AccountType;
  /** PUT substitui: null num cartão remove as definições. */
  creditCard?: CreditCardSettingsInput | null;
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

/** Phase 6.5 — sentido do movimento; o sinal já não vem da categoria. */
export type TransactionDirection = 'Inflow' | 'Outflow';
export type TransactionKind = 'Regular' | 'Transfer' | 'Adjustment';

export interface TransactionDto {
  id: string;
  accountId: string;
  /** null em transferências, acertos e transações sem categoria. */
  categoryId: string | null;
  occurredAt: string;
  amount: Money;
  description: string | null;
  tags: string[];
  exchangeRateToPrimary?: number | null;
  exchangeRateAt?: string | null;
  recurringRuleId?: string | null;
  createdAt: string;
  updatedAt: string;
  direction: TransactionDirection;
  kind: TransactionKind;
  transferId?: string | null;
  /** Phase 6.5 grupo 3 — conta da outra perna; null fora de transferências. */
  counterpartAccountId: string | null;
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
  recurringRuleId?: string | null;
  pageSize?: number | null;
  viewMode?: TransactionViewMode | null;
}

export interface TransactionsPageResponse {
  items: TransactionDto[];
  nextCursor: string | null;
}

// --- Transfers (Phase 6.5 grupo 3) ---
export interface CreateTransferRequest {
  fromAccountId: string;
  toAccountId: string;
  occurredAt: string;
  amountOut: number;
  amountIn?: number | null;
  description?: string | null;
}

export interface UpdateTransferRequest extends CreateTransferRequest {}

export interface ConvertToTransferRequest {
  counterpartAccountId: string;
  counterpartTransactionId?: string | null;
}

export interface TransferResponseDto {
  transferId: string;
  outLeg: TransactionDto;
  inLeg: TransactionDto;
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

// --- Categorization Rules (Phase 4) ---
export type MatchType = 'Contains' | 'Equals' | 'StartsWith';

export interface CategorizationRuleDto {
  id: string;
  name: string;
  pattern: string;
  matchType: MatchType;
  categoryId: string;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  priority: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCategorizationRuleRequest {
  name: string;
  pattern: string;
  matchType: string;
  categoryId: string;
  priority: number;
}

export interface UpdateCategorizationRuleRequest {
  name: string;
  pattern: string;
  matchType: string;
  categoryId: string;
  priority: number;
  isActive: boolean;
}

export interface ReorderRulesRequest {
  ruleIds: string[];
}

export interface ReapplyRulesRequest {
  categoryId?: string | null;
  from?: string | null;
  to?: string | null;
  onlyUncategorized?: boolean | null;
}

export interface ReapplyRulesResponse {
  totalProcessed: number;
  categorizedCount: number;
  unchangedCount: number;
}

export const MATCH_TYPES: MatchType[] = ['Contains', 'Equals', 'StartsWith'];
export const MATCH_TYPE_LABELS: Record<MatchType, string> = {
  Contains: 'Contém',
  Equals: 'Igual a',
  StartsWith: 'Começa com',
};

// --- Import Profiles (Phase 4) ---
export type TransactionField = 'Date' | 'Amount' | 'Currency' | 'Description' | 'Account' | 'Category' | 'CreditDebitIndicator';

export interface ColumnMappingDto {
  csvColumnName: string;
  transactionField: string;
  defaultValue?: string | null;
}

export interface ImportProfileDto {
  id: string;
  name: string;
  columnMappings: ColumnMappingDto[];
  delimiter: string;
  hasHeaderRow: boolean;
  dateFormat: string;
  decimalSeparator: string;
  skipRows: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateImportProfileRequest {
  name: string;
  columnMappings: ColumnMappingDto[];
  delimiter?: string | null;
  hasHeaderRow?: boolean | null;
  dateFormat?: string | null;
  decimalSeparator?: string | null;
  skipRows?: number | null;
}

export interface UpdateImportProfileRequest {
  name: string;
  columnMappings: ColumnMappingDto[];
  delimiter: string;
  hasHeaderRow: boolean;
  dateFormat: string;
  decimalSeparator: string;
  skipRows: number;
}

export const TRANSACTION_FIELDS: TransactionField[] = ['Date', 'Amount', 'Currency', 'Description', 'Account', 'Category', 'CreditDebitIndicator'];
export const TRANSACTION_FIELD_LABELS: Record<TransactionField, string> = {
  Date: 'Data',
  Amount: 'Valor',
  Currency: 'Moeda',
  Description: 'Descrição',
  Account: 'Conta',
  Category: 'Categoria',
  CreditDebitIndicator: 'Débito/Crédito',
};

// --- CSV Import (Phase 4) ---
export interface PreviewRow {
  rowIndex: number;
  values: string[];
  isDuplicate: boolean;
  duplicateTransactionId: string | null;
  suggestedCategoryName: string | null;
  suggestedCategoryId: string | null;
  isAutoCategorized: boolean;
  error: string | null;
}

export interface UploadCsvResponse {
  batchId: string;
  headers: string[];
  previewRows: PreviewRow[];
  totalRowCount: number;
  truncated: boolean;
  detectedDelimiter: string;
  detectedHasHeader: boolean;
  errors: string[];
}

export interface ColumnMappingInput {
  csvColumnName: string;
  transactionField: string | null;
}

export interface UpdatePreviewRequest {
  columnMappings: ColumnMappingInput[];
  delimiter?: string | null;
  hasHeaderRow?: boolean | null;
  dateFormat?: string | null;
  decimalSeparator?: string | null;
  skipRows?: number | null;
}

export interface ConfirmImportResponse {
  batchId: string;
  importedRows: number;
  autoCategorized: number;
  manualCount: number;
  errorRows: number;
  status: string;
}

export interface ImportBatchDto {
  id: string;
  importProfileId: string | null;
  fileName: string;
  status: string;
  totalRows: number;
  importedRows: number;
  duplicateRows: number;
  errorRows: number;
  createdAt: string;
}

// --- Recurring Rules (Phase 5a) ---
export type Frequency = 'Daily' | 'Weekly' | 'Monthly' | 'Yearly';

export interface RecurringRuleDto {
  id: string;
  description: string;
  amount: Money;
  accountId: string;
  categoryId: string | null;
  frequency: Frequency;
  interval: number;
  startDate: string;
  endDate: string | null;
  nextOccurrence: string | null;
  isActive: boolean;
  tags: string[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateRecurringRuleRequest {
  description: string;
  amount: number;
  currency: string;
  accountId: string;
  categoryId?: string | null;
  frequency: Frequency;
  interval: number;
  startDate: string;
  endDate?: string | null;
  tags?: string[] | null;
}

export interface UpdateRecurringRuleRequest {
  description: string;
  amount: number;
  currency: string;
  accountId: string;
  categoryId?: string | null;
  frequency: Frequency;
  interval: number;
  startDate: string;
  endDate?: string | null;
  isActive: boolean;
  tags?: string[] | null;
}

export const FREQUENCIES: Frequency[] = ['Daily', 'Weekly', 'Monthly', 'Yearly'];
export const FREQUENCY_LABELS: Record<Frequency, string> = {
  Daily: 'Diária',
  Weekly: 'Semanal',
  Monthly: 'Mensal',
  Yearly: 'Anual',
};

// ── Phase 5b — Budgets ───────────────────────────────────────────────

export interface BudgetProgressDto {
  limitAmount: number;
  limitCurrency: string;
  spentAmount: number;
  remainingAmount: number;
  percentUsed: number;
  projectedAmount?: number | null;
  hasIncompleteRates: boolean;
}

export interface BudgetDto {
  id: string;
  categoryId: string;
  year: number;
  month: number;
  limitAmount: number;
  limitCurrency: string;
  alertThresholdPercent: number;
  notes?: string | null;
  progress: BudgetProgressDto;
  createdAt: string;
  updatedAt: string;
}

export interface BudgetAlertDto {
  id: string;
  budgetId: string;
  categoryId: string;
  threshold: number;
  triggeredAt: string;
  spentAtTriggerAmount: number;
  spentAtTriggerCurrency: string;
  acknowledged: boolean;
  acknowledgedAt?: string | null;
}

export interface CreateBudgetRequest {
  categoryId: string;
  year: number;
  month: number;
  limitAmount: number;
  limitCurrency: string;
  alertThresholdPercent?: number | null;
  notes?: string | null;
}

export interface UpdateBudgetRequest {
  limitAmount: number;
  limitCurrency: string;
  alertThresholdPercent?: number | null;
  notes?: string | null;
}
