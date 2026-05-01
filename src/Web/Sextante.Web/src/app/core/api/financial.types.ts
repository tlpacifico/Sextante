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
