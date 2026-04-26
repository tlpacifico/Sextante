/**
 * Mapeamento de error codes do ASP.NET Core Identity (e do
 * <c>SignupEndpoint</c> custom) para mensagens PT-PT a apresentar
 * via PrimeNG Toast / inline `p-message`. Fallback genérico para
 * códigos desconhecidos.
 *
 * Mantém as mensagens **anti-enumeração**: nunca revelar se um email
 * já existe (ver Phase 1a §SignupEndpoint).
 */
export const IDENTITY_ERROR_MESSAGES_PT_PT: Record<string, string> = {
  // Login / refresh
  InvalidCredentials: 'Credenciais inválidas. Confirma o email e a palavra-passe.',
  RequiresTwoFactor: 'Esta conta tem 2FA — funcionalidade ainda não disponível.',
  LockedOut: 'Conta temporariamente bloqueada. Tenta de novo em alguns minutos.',
  NotAllowed: 'A conta ainda não está ativa. Confirma o email para continuar.',

  // Password requirements (Identity defaults)
  PasswordTooShort: 'A palavra-passe é demasiado curta (mínimo 12 caracteres).',
  PasswordRequiresDigit: 'A palavra-passe tem de incluir pelo menos um dígito.',
  PasswordRequiresLower: 'A palavra-passe tem de incluir pelo menos uma letra minúscula.',
  PasswordRequiresUpper: 'A palavra-passe tem de incluir pelo menos uma letra maiúscula.',
  PasswordRequiresNonAlphanumeric:
    'A palavra-passe tem de incluir pelo menos um caractere não alfanumérico.',
  PasswordRequiresUniqueChars: 'A palavra-passe tem de ter caracteres mais variados.',

  // Email
  InvalidEmail: 'Endereço de email inválido.',
  DuplicateUserName: 'Não foi possível criar a conta com os dados fornecidos.',
  DuplicateEmail: 'Não foi possível criar a conta com os dados fornecidos.',

  // Reset / confirm
  InvalidToken: 'O link expirou ou é inválido. Pede um novo email.',

  // Custom (signup endpoint genérico, do plano)
  SignupRejected: 'Não foi possível criar a conta com os dados fornecidos.',
};

export const GENERIC_ERROR_MESSAGE_PT_PT =
  'Algo correu mal. Tenta de novo em alguns instantes.';

export const NETWORK_ERROR_MESSAGE_PT_PT =
  'Sem ligação ao servidor. Verifica a tua internet e tenta novamente.';

export const EMAIL_NOT_DELIVERED_MESSAGE_PT_PT =
  'Envio de emails ainda não disponível — Phase 6.';

export const FORGOT_PASSWORD_GENERIC_MESSAGE_PT_PT =
  'Se o email existir, vais receber instruções dentro de momentos.';
