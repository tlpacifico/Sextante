import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { SelectModule } from 'primeng/select';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { FinancialStore } from '../financial/state/financial.store';

@Component({
  selector: 'app-settings-general-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    CardModule,
    SelectModule,
    ToastModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <div class="max-w-2xl mx-auto flex flex-col gap-4">
      <h1 class="text-xl md:text-2xl font-semibold">Definições gerais</h1>

      <p-card>
        <form [formGroup]="form" class="flex flex-col gap-4" (ngSubmit)="submit()">
          <div class="flex flex-col gap-1">
            <label for="primary-currency">Moeda principal</label>
            <p-select
              inputId="primary-currency"
              [options]="store.currencies()"
              optionLabel="code"
              optionValue="code"
              formControlName="primaryCurrency"
              styleClass="w-full"
            ></p-select>
            <small class="text-[var(--p-text-muted-color)]">
              Transações antigas mantêm o câmbio gravado no momento da
              criação. A mudança aplica-se a novas transações e ao
              dashboard.
            </small>
          </div>
          <div class="flex justify-end">
            <p-button
              label="Guardar"
              icon="pi pi-check"
              type="submit"
              [disabled]="form.invalid || submitting()"
            ></p-button>
          </div>
        </form>
      </p-card>

      <p-toast></p-toast>
    </div>
  `,
})
export class SettingsGeneralPage implements OnInit {
  protected readonly store = inject(FinancialStore);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    primaryCurrency: ['', Validators.required],
  });

  constructor() {
    // Sync current tenant primary into the form when settings load.
    effect(() => {
      const settings = this.store.tenantSettings();
      if (settings && !this.form.controls.primaryCurrency.value) {
        this.form.controls.primaryCurrency.setValue(settings.primaryCurrency);
      }
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      await Promise.all([
        this.store.loadCurrencies(),
        this.store.loadTenantSettings(true),
      ]);
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar as definições.',
      });
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.submitting.set(true);
    try {
      const value = this.form.getRawValue();
      await this.store.updateTenantSettings({
        primaryCurrency: value.primaryCurrency,
      });
      this.toast.add({
        severity: 'success',
        summary: `Moeda principal alterada para ${value.primaryCurrency}.`,
        detail: 'Transações antigas mantêm o câmbio gravado no momento.',
      });
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível alterar a moeda principal.',
      });
    } finally {
      this.submitting.set(false);
    }
  }
}
