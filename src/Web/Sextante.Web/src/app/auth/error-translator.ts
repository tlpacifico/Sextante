import { HttpErrorResponse } from '@angular/common/http';
import {
  EMAIL_NOT_DELIVERED_MESSAGE_PT_PT,
  GENERIC_ERROR_MESSAGE_PT_PT,
  IDENTITY_ERROR_MESSAGES_PT_PT,
  NETWORK_ERROR_MESSAGE_PT_PT,
} from './i18n/messages.pt-PT';

interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

interface IdentityErrorBag {
  type?: string;
  title?: string;
  errors?: Record<string, string[] | string>;
  detail?: string;
}

/**
 * Traduz um erro vindo do backend (HttpErrorResponse) para uma mensagem
 * PT-PT. Tenta na ordem:
 * 1. Mensagens específicas para condições conhecidas
 *    (NotImplementedEmailSender → "Phase 6", offline → "sem rede").
 * 2. Códigos do ASP.NET Core Identity em <c>error.errors</c>.
 * 3. Validation problem details (<c>errors</c> dictionary do MVC).
 * 4. Fallback genérico.
 */
export function translateError(err: unknown): string {
  if (!(err instanceof HttpErrorResponse)) {
    return GENERIC_ERROR_MESSAGE_PT_PT;
  }

  if (err.status === 0) {
    return NETWORK_ERROR_MESSAGE_PT_PT;
  }

  const problem = err.error as ProblemDetails | IdentityErrorBag | string | null;

  // NotImplementedEmailSender — Phase 1a stub. Lança no caminho de email
  // confirmado e o handler do Identity propaga 500.
  if (typeof problem === 'string' && problem.toLowerCase().includes('email')) {
    return EMAIL_NOT_DELIVERED_MESSAGE_PT_PT;
  }
  if (
    err.status === 500
    && typeof problem === 'object'
    && problem
    && 'detail' in problem
    && typeof problem.detail === 'string'
    && /email|smtp|notimplementedemail/i.test(problem.detail)
  ) {
    return EMAIL_NOT_DELIVERED_MESSAGE_PT_PT;
  }

  if (typeof problem === 'object' && problem !== null && 'errors' in problem) {
    const errors = problem.errors;
    if (errors) {
      for (const [key, value] of Object.entries(errors)) {
        const code = pickCode(key, value);
        const message = code ? IDENTITY_ERROR_MESSAGES_PT_PT[code] : undefined;
        if (message) return message;
      }
      // Fallback: mostra a primeira mensagem como veio (já em PT-PT do
      // SignupEndpoint validation filter).
      const first = Object.values(errors).flat().filter(Boolean)[0];
      if (typeof first === 'string') {
        return first;
      }
    }
  }

  if (err.status === 401) {
    return IDENTITY_ERROR_MESSAGES_PT_PT['InvalidCredentials'];
  }

  return GENERIC_ERROR_MESSAGE_PT_PT;
}

function pickCode(key: string, value: string[] | string | undefined): string | undefined {
  if (Array.isArray(value)) {
    return value[0] ?? key;
  }
  if (typeof value === 'string') {
    return value || key;
  }
  return key;
}
