import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { PasswordModule } from 'primeng/password';
import { MessageService } from 'primeng/api';
import { lastValueFrom } from 'rxjs';
import { SKIP_AUTH } from '../auth.service';
import { translateError } from '../error-translator';

@Component({
  selector: 'app-reset-password-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PasswordModule, ButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 class="text-lg font-medium mb-2">Definir nova palavra-passe</h2>

    @if (linkInvalid()) {
      <p class="text-sm text-[var(--p-red-500)] mb-4">
        O link de recuperação está incompleto ou expirou. Pede um novo email.
      </p>
      <p class="text-sm text-center">
        <a routerLink="/forgot-password" class="text-[var(--p-primary-500)] hover:underline">
          Pedir novo link
        </a>
      </p>
    } @else {
      <p class="text-sm text-[var(--p-text-muted-color)] mb-4">
        Define uma palavra-passe nova para <strong>{{ email() }}</strong>.
      </p>
      <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-4">
        <label class="flex flex-col gap-1.5">
          <span class="text-sm font-medium">Nova palavra-passe</span>
          <p-password
            formControlName="newPassword"
            [feedback]="true"
            [toggleMask]="true"
            inputStyleClass="w-full"
            styleClass="w-full"
            autocomplete="new-password"
            promptLabel="Mínimo 12 caracteres"
          ></p-password>
          @if (controlInvalid('newPassword')) {
            <small class="text-[var(--p-red-500)]">{{ passwordErrorMessage() }}</small>
          }
        </label>

        <label class="flex flex-col gap-1.5">
          <span class="text-sm font-medium">Confirmar palavra-passe</span>
          <p-password
            formControlName="confirmPassword"
            [feedback]="false"
            [toggleMask]="true"
            inputStyleClass="w-full"
            styleClass="w-full"
            autocomplete="new-password"
          ></p-password>
          @if (mismatchVisible()) {
            <small class="text-[var(--p-red-500)]">As palavras-passe não coincidem.</small>
          }
        </label>

        <p-button
          type="submit"
          label="Atualizar palavra-passe"
          icon="pi pi-check"
          [loading]="submitting()"
          [disabled]="submitting()"
          styleClass="w-full"
        ></p-button>
      </form>
    }
  `,
})
export class ResetPasswordPage implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly messages = inject(MessageService);

  protected readonly submitting = signal(false);
  protected readonly email = signal<string>('');
  private readonly code = signal<string>('');

  protected readonly form = this.fb.nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(12)]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: [passwordsMatch] },
  );

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    this.email.set(params.get('email') ?? '');
    this.code.set(params.get('code') ?? '');
  }

  protected linkInvalid(): boolean {
    return !this.email() || !this.code();
  }

  protected controlInvalid(name: 'newPassword' | 'confirmPassword'): boolean {
    const control: AbstractControl = this.form.controls[name];
    return control.invalid && (control.dirty || control.touched);
  }

  protected mismatchVisible(): boolean {
    const confirm = this.form.controls.confirmPassword;
    return (
      this.form.hasError('passwordsMismatch')
      && (confirm.dirty || confirm.touched)
    );
  }

  protected passwordErrorMessage(): string {
    const c = this.form.controls.newPassword;
    if (c.hasError('required')) return 'Palavra-passe obrigatória.';
    if (c.hasError('minlength')) return 'Mínimo 12 caracteres.';
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
          '/api/auth/resetPassword',
          {
            email: this.email(),
            resetCode: this.code(),
            newPassword: this.form.getRawValue().newPassword,
          },
          { context: ctx },
        ),
      );
      this.messages.add({
        severity: 'success',
        summary: 'Palavra-passe atualizada',
        detail: 'Já podes iniciar sessão com a nova palavra-passe.',
        life: 5000,
      });
      await this.router.navigate(['/login']);
    } catch (err) {
      this.messages.add({
        severity: 'error',
        summary: 'Não foi possível atualizar',
        detail: translateError(err),
        life: 6000,
      });
    } finally {
      this.submitting.set(false);
    }
  }
}

function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  if (!(group instanceof FormGroup)) return null;
  const a = group.get('newPassword')?.value;
  const b = group.get('confirmPassword')?.value;
  return a && b && a !== b ? { passwordsMismatch: true } : null;
}
