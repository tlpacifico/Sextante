import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ExchangeRateDto,
  ExchangeRateSnapshotStateDto,
  ListExchangeRatesQuery,
  ManualExchangeRateRequest,
} from './identity.types';

@Injectable({ providedIn: 'root' })
export class ExchangeRatesApiService {
  private readonly http = inject(HttpClient);

  list(query?: ListExchangeRatesQuery): Promise<ExchangeRateDto[]> {
    let params = new HttpParams();
    if (query?.from) params = params.set('from', query.from);
    if (query?.to) params = params.set('to', query.to);
    if (query?.currencies && query.currencies.length > 0) {
      for (const c of query.currencies) {
        params = params.append('currencies', c);
      }
    }
    return firstValueFrom(
      this.http.get<ExchangeRateDto[]>('/api/admin/exchange-rates', { params }),
    );
  }

  getState(): Promise<ExchangeRateSnapshotStateDto> {
    return firstValueFrom(
      this.http.get<ExchangeRateSnapshotStateDto>(
        '/api/admin/exchange-rates/state',
      ),
    );
  }

  insertManual(req: ManualExchangeRateRequest): Promise<ExchangeRateDto> {
    return firstValueFrom(
      this.http.post<ExchangeRateDto>('/api/admin/exchange-rates', req),
    );
  }

  runSnapshot(): Promise<void> {
    return firstValueFrom(
      this.http.post<void>('/api/admin/exchange-rates/snapshot/run', {}),
    );
  }
}
