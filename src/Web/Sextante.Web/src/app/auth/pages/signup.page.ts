import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { MessageService } from 'primeng/api';
import { MessageModule } from 'primeng/message';
import { AuthService } from '../auth.service';
import { translateError } from '../error-translator';

@Component({
  selector: 'app-signup-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    InputTextModule,
    PasswordModule,
    ButtonModule,
    MessageModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 class="text-lg font-medium mb-4">Criar conta</h2>
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
          <small class="text-[var(--p-red-500)]">
            {{ emailErrorMessage() }}
          </small>
        }
      </label>

      <label class="flex flex-col gap-1.5">
        <span class="text-sm font-medium">Palavra-passe</span>
        <p-password
          formControlName="password"
          [feedback]="true"
          [toggleMask]="true"
          inputStyleClass="w-full"
          styleClass="w-full"
          autocomplete="new-password"
          promptLabel="Mínimo 12 caracteres"
        ></p-password>
        @if (passwordInvalid()) {
          <small class="text-[var(--p-red-500)]">
            {{ passwordErrorMessage() }}
          </small>
        }
      </label>

      <label class="flex flex-col gap-1.5">
        <span class="text-sm font-medium">Nome do espaço financeiro (tenant)</span>
        <input
          type="text"
          pInputText
          formControlName="tenantName"
          [attr.aria-invalid]="tenantInvalid()"
          required
        />
        <small class="text-xs text-[var(--p-text-muted-color)]">
          Pode ser o teu nome ou agregado familiar (e.g. "Família Silva").
        </small>
        @if (tenantInvalid()) {
          <small class="text-[var(--p-red-500)]">
            {{ tenantErrorMessage() }}
          </small>
        }
      </label>

      <p-button
        type="submit"
        label="Criar conta"
        icon="pi pi-user-plus"
        [loading]="submitting()"
        [disabled]="submitting()"
        styleClass="w-full"
      ></p-button>
    </form>

    <p class="mt-4 text-sm text-center text-[var(--p-text-muted-color)]">
      Já tens conta?
      <a routerLink="/login" class="text-[var(--p-primary-500)] hover:underline">
        Iniciar sessão
      </a>
    </p>
  `,
})
export class SignupPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly messages = inject(MessageService);

  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(12)]],
    tenantName: [
      '',
      [Validators.required, Validators.minLength(2), Validators.maxLength(200)],
    ],
  });

  emailInvalid = () => this.controlInvalid('email');
  passwordInvalid = () => this.controlInvalid('password');
  tenantInvalid = () => this.controlInvalid('tenantName');

  emailErrorMessage(): string {
    const c = this.form.controls.email;
    if (c.hasError('required')) return 'Email obrigatório.';
    if (c.hasError('email')) return 'Endereço de email inválido.';
    return '';
  }

  passwordErrorMessage(): string {
    const c = this.form.controls.password;
    if (c.hasError('required')) return 'Palavra-passe obrigatória.';
    if (c.hasError('minlength')) return 'Mínimo 12 caracteres.';
    return '';
  }

  tenantErrorMessage(): string {
    const c = this.form.controls.tenantName;
    if (c.hasError('required')) return 'Nome obrigatório.';
    if (c.hasError('minlength')) return 'Mínimo 2 caracteres.';
    if (c.hasError('maxlength')) return 'Máximo 200 caracteres.';
    return '';
  }

  async submit(): Promise<void> {
    if (this.submitting()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.submitting.set(true);
    try {
      await this.auth.signup(this.form.getRawValue());
      await this.router.navigate(['/login'], {
        queryParams: { signup: 'success' },
      });
    } catch (err) {
      this.messages.add({
        severity: 'error',
        summary: 'Não foi possível criar a conta',
        detail: translateError(err),
        life: 5000,
      });
    } finally {
      this.submitting.set(false);
    }
  }

  private controlInvalid(name: 'email' | 'password' | 'tenantName'): boolean {
    const control: AbstractControl = this.form.controls[name];
    return control.invalid && (control.dirty || control.touched);
  }
}
