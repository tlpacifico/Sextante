import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageService } from 'primeng/api';
import { lastValueFrom } from 'rxjs';
import { SKIP_AUTH } from '../auth.service';
import {
  EMAIL_NOT_DELIVERED_MESSAGE_PT_PT,
  FORGOT_PASSWORD_GENERIC_MESSAGE_PT_PT,
} from '../i18n/messages.pt-PT';

@Component({
  selector: 'app-forgot-password-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, InputTextModule, ButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 class="text-lg font-medium mb-2">Recuperar palavra-passe</h2>
    <p class="text-sm text-[var(--p-text-muted-color)] mb-4">
      Indica o teu email. Se a conta existir, recebes instruções para
      definir uma nova palavra-passe.
    </p>
    <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-4">
      <label class="flex flex-col gap-1.5">
        <span class="text-sm font-medium">Email</span>
        <input
          type="email"
          pInputText
          autocomplete="email"
          formControlName="email"
          [attr.aria-invalid]="emailInvalid()"
          required
        />
        @if (emailInvalid()) {
          <small class="text-[var(--p-red-500)]">{{ emailErrorMessage() }}</small>
        }
      </label>

      <p-button
        type="submit"
        label="Enviar instruções"
        icon="pi pi-envelope"
        [loading]="submitting()"
        [disabled]="submitting()"
        styleClass="w-full"
      ></p-button>
    </form>

    <p class="mt-4 text-sm text-center">
      <a routerLink="/login" class="text-[var(--p-primary-500)] hover:underline">
        Voltar a iniciar sessão
      </a>
    </p>
  `,
})
export class ForgotPasswordPage {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly messages = inject(MessageService);

  protected readonly submitting = signal(false);
  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  emailInvalid = () => {
    const c: AbstractControl = this.form.controls.email;
    return c.invalid && (c.dirty || c.touched);
  };

  emailErrorMessage(): string {
    const c = this.form.controls.email;
    if (c.hasError('required')) return 'Email obrigatório.';
    if (c.hasError('email')) return 'Endereço de email inválido.';
    return '';
  }

  async submit(): Promise<void> {
    if (this.submitting()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.submitting.set(true);
    try {
      const ctx = new HttpContext().set(SKIP_AUTH, true);
      await lastValueFrom(
        this.http.post(
          '/api/auth/forgotPassword',
          { email: this.form.getRawValue().email },
          { context: ctx },
        ),
      );
      this.messages.add({
        severity: 'info',
        summary: 'Pedido enviado',
        detail: FORGOT_PASSWORD_GENERIC_MESSAGE_PT_PT,
        life: 6000,
      });
      this.form.reset();
    } catch {
      // Phase 1a: NotImplementedEmailSender lança em emails confirmados.
      // Não enumeramos — o utilizador vê sempre uma mensagem positiva,
      // exceto quando deu erro 5xx que sinaliza claramente o stub.
      this.messages.add({
        severity: 'warn',
        summary: 'Funcionalidade indisponível',
        detail: EMAIL_NOT_DELIVERED_MESSAGE_PT_PT,
        life: 6000,
      });
    } finally {
      this.submitting.set(false);
    }
  }
}
