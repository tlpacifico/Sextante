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

/**
 * Marca um request como "sem auth header" (skip do `authInterceptor`).
 * Usado pelo signup, login, forgot-password — endpoints que não devem
 * carregar `Authorization`. Usa `HttpContext` em vez de URL allowlist
 * para evitar que o interceptor tenha de manter listas que deslocadas
 * com novos endpoints.
 */
export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);

/**
 * Marca um request como "withCredentials: true" para que o browser envie
 * o cookie httpOnly do refresh token. O interceptor aplica isto sempre
 * a `/api/auth/refresh` mas o token está disponível para requests
 * personalizados (e.g. logout, que precisa do bearer mas não do cookie).
 */
export const WITH_REFRESH_COOKIE = new HttpContextToken<boolean>(() => false);

const ALLOWED_ROLES: ReadonlySet<TenantRole> = new Set<TenantRole>([
  'Owner',
  'Member',
  'ReadOnly',
]);

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly state = signal<AuthState | null>(null);
  /** Promise in-flight do refresh — usado para evitar "refresh storm". */
  private inflightRefresh: Promise<string> | null = null;

  readonly snapshot = this.state.asReadonly();
  readonly isAuthenticated = computed(() => {
    const value = this.state();
    return value !== null && value.accessTokenExpiresAt > Date.now();
  });
  readonly tenantName = computed(() => this.state()?.tenantName ?? null);
  readonly tenantRole = computed<TenantRole | null>(() => this.state()?.tenantRole ?? null);
  readonly userEmail = computed(() => this.state()?.email ?? null);

  /** Devolve o access token actual, ou null se não houver sessão. */
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
    const tokens = await lastValueFrom(
      this.http.post<TokenResponse>('/api/auth/login', request, {
        context: ctx,
        withCredentials: true,
      }),
    );

    const profile = await this.fetchProfile(tokens.accessToken);
    this.state.set(this.buildState(tokens, profile));
  }

  async logout(): Promise<void> {
    const accessToken = this.getAccessToken();
    if (accessToken) {
      try {
        await lastValueFrom(
          this.http.post('/api/auth/logout', {}, { withCredentials: true }),
        );
      } catch {
        // Logout server-side é "best effort" — clear local state mesmo
        // se a chamada falhar (rede off-line, token expirado).
      }
    }
    this.state.set(null);
  }

  /**
   * Refresh storm guard: a primeira chamada cria a Promise; chamadores
   * concorrentes recebem a mesma Promise pendente. Em sucesso, devolve
   * o novo access token; em falha, limpa state e propaga.
   */
  refresh(): Promise<string> {
    if (this.inflightRefresh) {
      return this.inflightRefresh;
    }
    this.inflightRefresh = this.doRefresh().finally(() => {
      this.inflightRefresh = null;
    });
    return this.inflightRefresh;
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
      this.state.set(null);
      throw err;
    }

    const current = this.state();
    if (current === null) {
      // Refresh succeeded but we don't have profile yet — fetch it.
      const profile = await this.fetchProfile(tokens.accessToken);
      this.state.set(this.buildState(tokens, profile));
    } else {
      this.state.set({
        ...current,
        accessToken: tokens.accessToken,
        accessTokenExpiresAt: Date.now() + tokens.expiresIn * 1000,
      });
    }
    return tokens.accessToken;
  }

  /**
   * Re-hidrata a sessão a partir do cookie httpOnly (usado no bootstrap
   * da app e no refresh do browser). Tenta um `/refresh`; se o cookie
   * estiver inválido ou ausente, devolve `false` e fica deslogado.
   */
  async loadProfile(): Promise<boolean> {
    try {
      await this.refresh();
      return true;
    } catch {
      return false;
    }
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
}
