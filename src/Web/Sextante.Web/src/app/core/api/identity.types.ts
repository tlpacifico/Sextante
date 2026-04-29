/**
 * DTOs do módulo Identity (Currency / ExchangeRate / Tenant settings).
 * Espelham os records C# em
 * `Sextante.Modules.Identity.Api.Endpoints.*`.
 */

export type ExchangeRateSource = 'ECB' | 'manual' | (string & {});

export interface CurrencyDto {
  code: string;
  name: string;
  symbol: string;
  minorUnits: number;
  isActive: boolean;
}

export interface CreateCurrencyRequest {
  code: string;
  name: string;
  symbol: string;
  minorUnits: number;
  isActive: boolean;
}

export interface UpdateCurrencyRequest {
  name: string;
  symbol: string;
  minorUnits: number;
  isActive: boolean;
}

export interface ExchangeRateDto {
  id: string;
  rateDate: string; // ISO 8601 date (YYYY-MM-DD)
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  source: ExchangeRateSource;
  updatedAt: string;
}

export interface ExchangeRateSnapshotStateDto {
  lastRunAt: string | null;
  lastSuccessAt: string | null;
  lastError: string | null;
}

export interface ManualExchangeRateRequest {
  rateDate: string; // YYYY-MM-DD
  toCurrency: string;
  rate: number;
}

export interface ListExchangeRatesQuery {
  from?: string | null;
  to?: string | null;
  currencies?: string[] | null;
}

export interface TenantSettingsDto {
  id: string;
  name: string;
  primaryCurrency: string;
}

export interface UpdateTenantSettingsRequest {
  primaryCurrency: string;
}
