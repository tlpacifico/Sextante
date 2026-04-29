import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  TenantSettingsDto,
  UpdateTenantSettingsRequest,
} from './identity.types';

@Injectable({ providedIn: 'root' })
export class TenantApiService {
  private readonly http = inject(HttpClient);

  getSettings(): Promise<TenantSettingsDto> {
    return firstValueFrom(this.http.get<TenantSettingsDto>('/api/tenants/me'));
  }

  updateSettings(req: UpdateTenantSettingsRequest): Promise<TenantSettingsDto> {
    return firstValueFrom(
      this.http.put<TenantSettingsDto>('/api/tenants/me', req),
    );
  }
}
