export type TenantRole = 'Owner' | 'Member' | 'ReadOnly';

export interface AuthState {
  /** Bearer token opaco emitido pelo MapIdentityApi (Phase 1a). */
  accessToken: string;
  /** Epoch ms em que o access token expira (Date.now() + expiresIn*1000). */
  accessTokenExpiresAt: number;
  userId: string;
  email: string;
  tenantId: string;
  /** Resolvido via GET /api/auth/me (Phase 1a Tenants tabela). */
  tenantName: string | null;
  tenantRole: TenantRole;
}

export interface LoginRequest {
  email: string;
  password: string;
  /** Phase 5.5 — "Manter-me ligado": emite refresh token com lifetime estendido. */
  extendedSession?: boolean;
}

export interface SignupRequest {
  email: string;
  password: string;
  tenantName: string;
}

export interface SignupResponse {
  userId: string;
  tenantId: string;
}

/**
 * Resposta do MapIdentityApi para /login e /refresh — formato OAuth-ish:
 * { tokenType, accessToken, expiresIn, refreshToken }. Phase 1a usa
 * encrypted ticket (não JWT), pelo que o accessToken é opaque para o
 * cliente.
 */
export interface TokenResponse {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

export interface MeResponse {
  userId: string;
  email: string;
  tenantId: string;
  tenantName: string | null;
  tenantRole: TenantRole;
}
