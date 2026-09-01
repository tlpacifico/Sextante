import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { MessageService } from 'primeng/api';
import { AuthService } from '../auth/auth.service';

const DISMISSED_KEY = 'sextante.emailBannerDismissed';

/**
 * Phase 6 — banner **não bloqueante** de confirmação de email. O backend
 * mantém `SignIn.RequireConfirmedEmail = false` de propósito (num sistema
 * de um utilizador, exigir confirmação só cria risco de auto-lockout se o
 * relay falhar), pelo que este aviso é o único mecanismo — e é dispensável
 * por sessão.
 */
@Component({
  selector: 'app-email-confirmation-banner',
  standalone: true,
  imports: [ButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <div
        class="flex flex-wrap items-center justify-between gap-2 border-b border-amber-200 bg-amber-50 px-4 py-2 text-amber-900 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-100"
        data-testid="email-confirmation-banner"
      >
        <div class="flex min-w-0 items-center gap-2">
          <i class="pi pi-envelope"></i>
          <span class="truncate text-sm">
            Confirme o seu email para garantir a recuperação da palavra-passe.
          </span>
        </div>
        <div class="flex items-center gap-1">
          <p-button
            label="Reenviar"
            severity="secondary"
            size="small"
            [text]="true"
            [loading]="sending()"
            (onClick)="resend()"
            data-testid="email-confirmation-resend"
          ></p-button>
          <p-button
            label="Dispensar"
            severity="secondary"
            size="small"
            [text]="true"
            (onClick)="dismiss()"
            data-testid="email-confirmation-dismiss"
          ></p-button>
        </div>
      </div>
    }
  `,
})
export class EmailConfirmationBannerComponent {
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly sending = signal(false);
  protected readonly dismissed = signal(readDismissed());

  protected readonly visible = computed(
    () => this.auth.isAuthenticated() && !this.auth.emailConfirmed() && !this.dismissed(),
  );

  protected async resend(): Promise<void> {
    if (this.sending()) {
      return;
    }

    this.sending.set(true);
    try {
      await this.auth.resendConfirmationEmail();
      this.messages.add({
        severity: 'success',
        summary: 'Email enviado',
        detail: 'Verifique a caixa de entrada — e a pasta de spam.',
        life: 4000,
      });
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível reenviar o email de confirmação.',
        life: 4000,
      });
    } finally {
      this.sending.set(false);
    }
  }

  protected dismiss(): void {
    this.dismissed.set(true);
    try {
      // sessionStorage e não localStorage: dispensar vale para esta
      // sessão, não para sempre — o email continua por confirmar.
      sessionStorage.setItem(DISMISSED_KEY, 'true');
    } catch {
      // Modo privado / storage bloqueado: o banner volta no próximo load.
    }
  }
}

function readDismissed(): boolean {
  try {
    return sessionStorage.getItem(DISMISSED_KEY) === 'true';
  } catch {
    return false;
  }
}
