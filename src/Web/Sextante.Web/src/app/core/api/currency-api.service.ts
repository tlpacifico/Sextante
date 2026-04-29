import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  CreateCurrencyRequest,
  CurrencyDto,
  UpdateCurrencyRequest,
} from './identity.types';

@Injectable({ providedIn: 'root' })
export class CurrencyApiService {
  private readonly http = inject(HttpClient);

  /** Lookup público autenticado — apenas activas. */
  listActive(): Promise<CurrencyDto[]> {
    return firstValueFrom(this.http.get<CurrencyDto[]>('/api/currencies'));
  }

  /** Admin — lista completa (activas + inactivas). */
  listAll(): Promise<CurrencyDto[]> {
    return firstValueFrom(this.http.get<CurrencyDto[]>('/api/admin/currencies'));
  }

  create(req: CreateCurrencyRequest): Promise<CurrencyDto> {
    return firstValueFrom(
      this.http.post<CurrencyDto>('/api/admin/currencies', req),
    );
  }

  update(code: string, req: UpdateCurrencyRequest): Promise<CurrencyDto> {
    return firstValueFrom(
      this.http.put<CurrencyDto>(`/api/admin/currencies/${code}`, req),
    );
  }
}
