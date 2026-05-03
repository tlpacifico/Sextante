import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { firstValueFrom, lastValueFrom } from 'rxjs';
import {
  AuthState,
  LoginRequest,
  MeResponse,
  SignupRequest,
  SignupResponse,
  TenantRole,
  TokenResponse,
} from './auth.types';

export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);
export const WITH_REFRESH_COOKIE = new HttpContextToken<boolean>(() => false);

const ALLOWED_ROLES: ReadonlySet<TenantRole> = new Set<TenantRole>([
  'Owner',
  'Member',
  'ReadOnly',
]);

const STORAGE_KEYS = {
  access: 'sextante.access',
  refresh: 'sextante.refresh',
  lastLoginEmail: 'sextante.last-login-email',
  extendedSession: 'sextante.extended-session',
} as const;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly state = signal<AuthState | null>(null);
  private inflightRefresh: Promise<string> | null = null;

  readonly snapshot = this.state.asReadonly();
  readonly isAuthenticated = computed(() => {
    const value = this.state();
    return value !== null && value.accessTokenExpiresAt > Date.now();
  });
  readonly tenantName = computed(() => this.state()?.tenantName ?? null);
  readonly tenantRole = computed<TenantRole | null>(() => this.state()?.tenantRole ?? null);
  readonly userEmail = computed(() => this.state()?.email ?? null);

  /** Phase 5.5 — email da última sessão para pre-fill no login. */
  getLastLoginEmail(): string | null {
    try {
      return localStorage.getItem(STORAGE_KEYS.lastLoginEmail);
    } catch {
      return null;
    }
  }

  getAccessToken(): string | null {
    return this.state()?.accessToken ?? null;
  }

  async signup(request: SignupRequest): Promise<SignupResponse> {
    const ctx = new HttpContext().set(SKIP_AUTH, true);
    return await lastValueFrom(
      this.http.post<SignupResponse>('/api/auth/signup', request, { context: ctx }),
    );
  }

  async login(request: LoginRequest): Promise<void> {
    const ctx = new HttpContext().set(SKIP_AUTH, true).set(WITH_REFRESH_COOKIE, true);
    const body = { email: request.email, password: request.password, extendedSession: request.extendedSession ?? false };
    const tokens = await lastValueFrom(
      this.http.post<TokenResponse>('/api/auth/login', body, {
        context: ctx,
        withCredentials: true,
      }),
    );

    const profile = await this.fetchProfile(tokens.accessToken);
    this.state.set(this.buildState(tokens, profile));

    this.persistTokens(tokens, request.extendedSession ?? false);
    this.persistLastLoginEmail(request.email);
  }

  async logout(): Promise<void> {
    const accessToken = this.getAccessToken();
    if (accessToken) {
      try {
        await lastValueFrom(
          this.http.post('/api/auth/logout', {}, { withCredentials: true }),
        );
      } catch {
        // Logout server-side é "best effort".
      }
    }
    this.clearStorage();
    this.state.set(null);
  }

  refresh(): Promise<string> {
    if (this.inflightRefresh) {
      return this.inflightRefresh;
    }
    this.inflightRefresh = this.doRefresh().finally(() => {
      this.inflightRefresh = null;
    });
    return this.inflightRefresh;
  }

  /**
   * Phase 5.5 — re-hidrata a sessão a partir de localStorage.
   * Tenta primeiro o access token; se expirado, tenta o refresh token
   * via cookie httpOnly. Se ambos falharem, limpa storage.
   */
  async rehydrateFromStorage(): Promise<boolean> {
    try {
      const storedAccess = localStorage.getItem(STORAGE_KEYS.access);
      if (storedAccess) {
        const tokens = JSON.parse(storedAccess) as { accessToken: string; expiresIn: number; refreshToken: string };
        const expiresAt = Date.now() + tokens.expiresIn * 1000;
        if (Date.now() < expiresAt) {
          // Access token ainda válido — carrega profile.
          try {
            const profile = await this.fetchProfile(tokens.accessToken);
            this.state.set(this.buildState(
              { accessToken: tokens.accessToken, expiresIn: tokens.expiresIn, refreshToken: tokens.refreshToken, tokenType: 'Bearer' },
              profile,
            ));
            return true;
          } catch {
            // Access token inválido — tenta refresh.
          }
        }
      }

      // Tenta refresh via cookie httpOnly.
      await this.refresh();
      return true;
    } catch {
      this.clearStorage();
      this.state.set(null);
      return false;
    }
  }

  async loadProfile(): Promise<boolean> {
    try {
      await this.refresh();
      return true;
    } catch {
      return false;
    }
  }

  private async doRefresh(): Promise<string> {
    const ctx = new HttpContext().set(SKIP_AUTH, true).set(WITH_REFRESH_COOKIE, true);
    let tokens: TokenResponse;
    try {
      tokens = await lastValueFrom(
        this.http.post<TokenResponse>(
          '/api/auth/refresh',
          {},
          { context: ctx, withCredentials: true },
        ),
      );
    } catch (err) {
      this.clearStorage();
      this.state.set(null);
      throw err;
    }

    const current = this.state();
    if (current === null) {
      const profile = await this.fetchProfile(tokens.accessToken);
      this.state.set(this.buildState(tokens, profile));
    } else {
      this.state.set({
        ...current,
        accessToken: tokens.accessToken,
        accessTokenExpiresAt: Date.now() + tokens.expiresIn * 1000,
      });
    }

    this.persistTokens(tokens, localStorage.getItem(STORAGE_KEYS.extendedSession) === 'true');
    return tokens.accessToken;
  }

  private async fetchProfile(accessToken: string): Promise<MeResponse> {
    return await firstValueFrom(
      this.http.get<MeResponse>('/api/auth/me', {
        headers: { Authorization: `Bearer ${accessToken}` },
        context: new HttpContext().set(SKIP_AUTH, true),
      }),
    );
  }

  private buildState(tokens: TokenResponse, profile: MeResponse): AuthState {
    const role: TenantRole = ALLOWED_ROLES.has(profile.tenantRole as TenantRole)
      ? (profile.tenantRole as TenantRole)
      : 'Member';

    return {
      accessToken: tokens.accessToken,
      accessTokenExpiresAt: Date.now() + tokens.expiresIn * 1000,
      userId: profile.userId,
      email: profile.email,
      tenantId: profile.tenantId,
      tenantName: profile.tenantName,
      tenantRole: role,
    };
  }

  private persistTokens(tokens: TokenResponse, extended: boolean): void {
    try {
      localStorage.setItem(STORAGE_KEYS.access, JSON.stringify({
        accessToken: tokens.accessToken,
        expiresIn: tokens.expiresIn,
        refreshToken: tokens.refreshToken,
      }));
      localStorage.setItem(STORAGE_KEYS.refresh, tokens.refreshToken);
      localStorage.setItem(STORAGE_KEYS.extendedSession, extended ? 'true' : 'false');
    } catch {
      // localStorage pode não estar disponível.
    }
  }

  private persistLastLoginEmail(email: string): void {
    try {
      localStorage.setItem(STORAGE_KEYS.lastLoginEmail, email);
    } catch {
      // localStorage pode não estar disponível.
    }
  }

  private clearStorage(): void {
    try {
      localStorage.removeItem(STORAGE_KEYS.access);
      localStorage.removeItem(STORAGE_KEYS.refresh);
      localStorage.removeItem(STORAGE_KEYS.extendedSession);
      // Mantém lastLoginEmail para pre-fill no próximo login.
    } catch {
      // localStorage pode não estar disponível.
    }
  }
}
