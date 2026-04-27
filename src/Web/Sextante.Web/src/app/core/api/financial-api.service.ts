import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  AccountDto,
  CategoryDto,
  CreateAccountRequest,
  CreateCategoryRequest,
  CreateTransactionRequest,
  TransactionByCategoryResponse,
  TransactionDto,
  TransactionFilter,
  TransactionSummaryResponse,
  TransactionsPageResponse,
  UpdateAccountRequest,
  UpdateCategoryRequest,
  UpdateTransactionRequest,
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
    if (filter.pageSize) {
      params = params.set('pageSize', filter.pageSize.toString());
    }
    return params;
  }
}
