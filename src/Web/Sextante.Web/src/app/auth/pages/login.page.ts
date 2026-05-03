import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { MessageService } from 'primeng/api';
import { AuthService } from '../auth.service';
import { translateError } from '../error-translator';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    InputTextModule,
    PasswordModule,
    ButtonModule,
    CheckboxModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 class="text-lg font-medium mb-4">Iniciar sessão</h2>
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

      <label class="flex flex-col gap-1.5">
        <span class="text-sm font-medium">Palavra-passe</span>
        <p-password
          formControlName="password"
          [feedback]="false"
          [toggleMask]="true"
          inputStyleClass="w-full"
          styleClass="w-full"
          autocomplete="current-password"
        ></p-password>
        @if (passwordInvalid()) {
          <small class="text-[var(--p-red-500)]">Palavra-passe obrigatória.</small>
        }
      </label>

      <div class="flex items-center gap-2">
        <p-checkbox formControlName="extendedSession" inputId="extended" [binary]="true" />
        <label for="extended" class="text-sm text-neutral-600 dark:text-neutral-400 cursor-pointer">
          Manter-me ligado
        </label>
      </div>

      <p-button
        type="submit"
        label="Entrar"
        icon="pi pi-sign-in"
        [loading]="submitting()"
        [disabled]="submitting()"
        styleClass="w-full"
      ></p-button>
    </form>

    <div class="mt-4 flex flex-col items-center gap-1.5 text-sm">
      <a routerLink="/forgot-password" class="text-[var(--p-primary-500)] hover:underline">
        Esqueci-me da palavra-passe
      </a>
      <p class="text-[var(--p-text-muted-color)]">
        Não tens conta?
        <a routerLink="/signup" class="text-[var(--p-primary-500)] hover:underline">
          Criar conta
        </a>
      </p>
    </div>
  `,
})
export class LoginPage implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(MessageService);

  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    extendedSession: [false],
  });

  emailInvalid = () => this.controlInvalid('email');
  passwordInvalid = () => this.controlInvalid('password');

  emailErrorMessage(): string {
    const c = this.form.controls.email;
    if (c.hasError('required')) return 'Email obrigatório.';
    if (c.hasError('email')) return 'Endereço de email inválido.';
    return '';
  }

  ngOnInit(): void {
    // Phase 5.5 — pre-fill email da última sessão.
    const lastEmail = this.auth.getLastLoginEmail();
    if (lastEmail) {
      this.form.controls.email.setValue(lastEmail);
    }

    const params = this.route.snapshot.queryParamMap;
    if (params.get('signup') === 'success') {
      this.messages.add({
        severity: 'success',
        summary: 'Conta criada',
        detail: 'A tua conta foi criada. Faz login para entrar.',
        life: 5000,
      });
    }
  }

  async submit(): Promise<void> {
    if (this.submitting()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.submitting.set(true);
    try {
      const { email, password, extendedSession } = this.form.getRawValue();
      await this.auth.login({ email, password, extendedSession });
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      const target = returnUrl && returnUrl.startsWith('/') ? returnUrl : '/app/dashboard';
      await this.router.navigateByUrl(target);
    } catch (err) {
      this.messages.add({
        severity: 'error',
        summary: 'Não foi possível iniciar sessão',
        detail: translateError(err),
        life: 5000,
      });
    } finally {
      this.submitting.set(false);
    }
  }

  private controlInvalid(name: 'email' | 'password'): boolean {
    const control: AbstractControl = this.form.controls[name];
    return control.invalid && (control.dirty || control.touched);
  }
}
