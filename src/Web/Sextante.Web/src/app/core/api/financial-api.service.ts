import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  AccountDto,
  BudgetAlertDto,
  BudgetDto,
  BudgetProgressDto,
  CategorizationRuleDto,
  CategoryDto,
  ConfirmImportResponse,
  CreateAccountRequest,
  CreateBudgetRequest,
  CreateCategorizationRuleRequest,
  CreateCategoryRequest,
  CreateImportProfileRequest,
  CreateRecurringRuleRequest,
  CreateTransactionRequest,
  ImportBatchDto,
  ImportProfileDto,
  ReapplyRulesRequest,
  ReapplyRulesResponse,
  RecurringRuleDto,
  ReorderRulesRequest,
  TransactionByCategoryResponse,
  TransactionDto,
  TransactionFilter,
  TransactionSummaryResponse,
  TransactionsPageResponse,
  UpdateAccountRequest,
  UpdateBudgetRequest,
  UpdateCategorizationRuleRequest,
  UpdateCategoryRequest,
  UpdateImportProfileRequest,
  UpdatePreviewRequest,
  UpdateRecurringRuleRequest,
  UpdateTransactionRequest,
  UploadCsvResponse,
} from './financial.types';

@Injectable({ providedIn: 'root' })
export class FinancialApiService {
  private readonly http = inject(HttpClient);

  // Accounts ----------------------------------------------------------------
  listAccounts(): Promise<AccountDto[]> {
    return firstValueFrom(this.http.get<AccountDto[]>('/api/financial/accounts'));
  }

  createAccount(req: CreateAccountRequest): Promise<AccountDto> {
    return firstValueFrom(this.http.post<AccountDto>('/api/financial/accounts', req));
  }

  updateAccount(id: string, req: UpdateAccountRequest): Promise<AccountDto> {
    return firstValueFrom(
      this.http.put<AccountDto>(`/api/financial/accounts/${id}`, req),
    );
  }

  archiveAccount(id: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`/api/financial/accounts/${id}`),
    );
  }

  // Categories --------------------------------------------------------------
  listCategories(): Promise<CategoryDto[]> {
    return firstValueFrom(this.http.get<CategoryDto[]>('/api/financial/categories'));
  }

  createCategory(req: CreateCategoryRequest): Promise<CategoryDto> {
    return firstValueFrom(this.http.post<CategoryDto>('/api/financial/categories', req));
  }

  updateCategory(id: string, req: UpdateCategoryRequest): Promise<CategoryDto> {
    return firstValueFrom(
      this.http.put<CategoryDto>(`/api/financial/categories/${id}`, req),
    );
  }

  archiveCategory(id: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`/api/financial/categories/${id}`),
    );
  }

  // Transactions ------------------------------------------------------------
  listTransactions(
    filter: TransactionFilter,
    cursor?: string | null,
  ): Promise<TransactionsPageResponse> {
    let params = this.toParams(filter);
    if (cursor) {
      params = params.set('cursor', cursor);
    }
    return firstValueFrom(
      this.http.get<TransactionsPageResponse>('/api/financial/transactions', { params }),
    );
  }

  getTransactionSummary(filter: TransactionFilter): Promise<TransactionSummaryResponse> {
    const params = this.toParams(filter);
    return firstValueFrom(
      this.http.get<TransactionSummaryResponse>(
        '/api/financial/transactions/summary',
        { params },
      ),
    );
  }

  getTransactionsByCategory(
    filter: TransactionFilter,
    kind: 'Expense' | 'Income' = 'Expense',
  ): Promise<TransactionByCategoryResponse[]> {
    const params = this.toParams(filter).set('kind', kind);
    return firstValueFrom(
      this.http.get<TransactionByCategoryResponse[]>(
        '/api/financial/transactions/by-category',
        { params },
      ),
    );
  }

  createTransaction(req: CreateTransactionRequest): Promise<TransactionDto> {
    return firstValueFrom(this.http.post<TransactionDto>('/api/financial/transactions', req));
  }

  updateTransaction(id: string, req: UpdateTransactionRequest): Promise<TransactionDto> {
    return firstValueFrom(
      this.http.put<TransactionDto>(`/api/financial/transactions/${id}`, req),
    );
  }

  archiveTransaction(id: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`/api/financial/transactions/${id}`),
    );
  }

  /** Phase 5.5 — list all transactions without cursor pagination. */
  async listTransactionsSimple(filter: {
    dateFrom?: string;
    dateTo?: string;
    accountIds?: string[];
    categoryIds?: string[];
  }): Promise<TransactionDto[]> {
    let params = new HttpParams();
    if (filter.dateFrom) params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo) params = params.set('dateTo', filter.dateTo);
    if (filter.accountIds?.length) params = params.set('accountIds', filter.accountIds.join(','));
    if (filter.categoryIds?.length) params = params.set('categoryIds', filter.categoryIds.join(','));
    params = params.set('pageSize', '500'); // fetch all in one call for MVP

    const page = await firstValueFrom(
      this.http.get<TransactionsPageResponse>('/api/financial/transactions', { params }),
    );
    return page.items;
  }

  /** Phase 5.5 — bulk recategorize transactions. */
  recategorizeTransactions(ids: string[], categoryId: string): Promise<{ updatedCount: number }> {
    return firstValueFrom(
      this.http.patch<{ updatedCount: number }>(
        '/api/financial/transactions/recategorize',
        { ids, categoryId },
      ),
    );
  }

  // Categorization Rules (Phase 4) -------------------------------------------
  listCategorizationRules(): Promise<CategorizationRuleDto[]> {
    return firstValueFrom(this.http.get<CategorizationRuleDto[]>('/api/financial/categorization-rules'));
  }

  createCategorizationRule(req: CreateCategorizationRuleRequest): Promise<CategorizationRuleDto> {
    return firstValueFrom(this.http.post<CategorizationRuleDto>('/api/financial/categorization-rules', req));
  }

  updateCategorizationRule(id: string, req: UpdateCategorizationRuleRequest): Promise<CategorizationRuleDto> {
    return firstValueFrom(this.http.put<CategorizationRuleDto>(`/api/financial/categorization-rules/${id}`, req));
  }

  archiveCategorizationRule(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/financial/categorization-rules/${id}`));
  }

  reorderRules(req: ReorderRulesRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>('/api/financial/categorization-rules/reorder', req));
  }

  reapplyRules(params: ReapplyRulesRequest): Promise<ReapplyRulesResponse> {
    let httpParams = new HttpParams();
    if (params.categoryId) httpParams = httpParams.set('categoryId', params.categoryId);
    if (params.from) httpParams = httpParams.set('from', params.from);
    if (params.to) httpParams = httpParams.set('to', params.to);
    if (params.onlyUncategorized != null) httpParams = httpParams.set('onlyUncategorized', String(params.onlyUncategorized));
    return firstValueFrom(this.http.post<ReapplyRulesResponse>('/api/financial/categorization-rules/reapply', undefined, { params: httpParams }));
  }

  // Import Profiles (Phase 4) ------------------------------------------------
  listImportProfiles(): Promise<ImportProfileDto[]> {
    return firstValueFrom(this.http.get<ImportProfileDto[]>('/api/financial/import-profiles'));
  }

  createImportProfile(req: CreateImportProfileRequest): Promise<ImportProfileDto> {
    return firstValueFrom(this.http.post<ImportProfileDto>('/api/financial/import-profiles', req));
  }

  updateImportProfile(id: string, req: UpdateImportProfileRequest): Promise<ImportProfileDto> {
    return firstValueFrom(this.http.put<ImportProfileDto>(`/api/financial/import-profiles/${id}`, req));
  }

  archiveImportProfile(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/financial/import-profiles/${id}`));
  }

  // CSV Import (Phase 4) -----------------------------------------------------
  uploadCsv(file: File, importProfileId?: string | null): Promise<UploadCsvResponse> {
    const formData = new FormData();
    formData.append('file', file);
    let url = '/api/financial/imports/upload';
    if (importProfileId) {
      url += `?importProfileId=${importProfileId}`;
    }
    return firstValueFrom(this.http.post<UploadCsvResponse>(url, formData));
  }

  updatePreview(batchId: string, req: UpdatePreviewRequest): Promise<UploadCsvResponse> {
    return firstValueFrom(this.http.put<UploadCsvResponse>(`/api/financial/imports/${batchId}/preview`, req));
  }

  confirmImport(batchId: string, includeDuplicates: string[]): Promise<ConfirmImportResponse> {
    return firstValueFrom(this.http.post<ConfirmImportResponse>(`/api/financial/imports/${batchId}/confirm`, { includeDuplicates }));
  }

  listImportBatches(): Promise<ImportBatchDto[]> {
    return firstValueFrom(this.http.get<ImportBatchDto[]>('/api/financial/imports'));
  }

  // Recurring Rules (Phase 5a) ---------------------------------------------
  listRecurringRules(): Promise<RecurringRuleDto[]> {
    return firstValueFrom(this.http.get<RecurringRuleDto[]>('/api/financial/recurring-rules'));
  }

  getRecurringRule(id: string): Promise<RecurringRuleDto> {
    return firstValueFrom(this.http.get<RecurringRuleDto>(`/api/financial/recurring-rules/${id}`));
  }

  createRecurringRule(req: CreateRecurringRuleRequest): Promise<RecurringRuleDto> {
    return firstValueFrom(this.http.post<RecurringRuleDto>('/api/financial/recurring-rules', req));
  }

  updateRecurringRule(id: string, req: UpdateRecurringRuleRequest): Promise<RecurringRuleDto> {
    return firstValueFrom(this.http.put<RecurringRuleDto>(`/api/financial/recurring-rules/${id}`, req));
  }

  archiveRecurringRule(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/financial/recurring-rules/${id}`));
  }

  getUpcomingOccurrences(id: string, count: number = 10): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>(`/api/financial/recurring-rules/${id}/upcoming`, {
      params: { count: count.toString() },
    }));
  }

  // ── Phase 5b — Budgets ─────────────────────────────────────────────

  listBudgets(year?: number, month?: number): Promise<BudgetDto[]> {
    let params = new HttpParams();
    if (year !== undefined) params = params.set('year', year.toString());
    if (month !== undefined) params = params.set('month', month.toString());
    return firstValueFrom(this.http.get<BudgetDto[]>('/api/financial/budgets', { params }));
  }

  getBudget(id: string): Promise<BudgetDto> {
    return firstValueFrom(this.http.get<BudgetDto>(`/api/financial/budgets/${id}`));
  }

  getBudgetProgress(id: string): Promise<BudgetProgressDto> {
    return firstValueFrom(this.http.get<BudgetProgressDto>(`/api/financial/budgets/${id}/progress`));
  }

  createBudget(req: CreateBudgetRequest): Promise<BudgetDto> {
    return firstValueFrom(this.http.post<BudgetDto>('/api/financial/budgets', req));
  }

  updateBudget(id: string, req: UpdateBudgetRequest): Promise<BudgetDto> {
    return firstValueFrom(this.http.put<BudgetDto>(`/api/financial/budgets/${id}`, req));
  }

  archiveBudget(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/financial/budgets/${id}`));
  }

  listActiveBudgetAlerts(): Promise<BudgetAlertDto[]> {
    return firstValueFrom(this.http.get<BudgetAlertDto[]>('/api/financial/budgets/alerts/active'));
  }

  acknowledgeBudgetAlert(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(
      `/api/financial/budgets/alerts/${id}/acknowledge`, {}));
  }

  private toParams(filter: TransactionFilter): HttpParams {
    let params = new HttpParams();
    if (filter.dateFrom) params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo) params = params.set('dateTo', filter.dateTo);
    if (filter.categoryIds && filter.categoryIds.length > 0) {
      for (const id of filter.categoryIds) {
        params = params.append('categoryIds', id);
      }
    }
    if (filter.accountIds && filter.accountIds.length > 0) {
      for (const id of filter.accountIds) {
        params = params.append('accountIds', id);
      }
    }
    if (filter.recurringRuleId) {
      params = params.set('recurringRuleId', filter.recurringRuleId);
    }
    if (filter.pageSize) {
      params = params.set('pageSize', filter.pageSize.toString());
    }
    if (filter.viewMode) {
      params = params.set('viewMode', filter.viewMode);
    }
    return params;
  }
}
