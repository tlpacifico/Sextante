import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import {
  FREQUENCIES,
  FREQUENCY_LABELS,
  Frequency,
  RecurringRuleDto,
} from '../../../core/api/financial.types';
import { FinancialStore } from '../state/financial.store';

@Component({
  selector: 'app-recurring-rule-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DatePickerModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    ToastModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <p-dialog
      [(visible)]="visible"
      [modal]="true"
      [closable]="true"
      [header]="editingId ? 'Editar regra' : 'Nova regra recorrente'"
      [style]="{ width: '32rem' }"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      (onHide)="closed.emit()"
    >
      <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
        <div class="flex flex-col gap-1">
          <label for="rule-desc">Descrição</label>
          <input
            id="rule-desc"
            pInputText
            formControlName="description"
            maxlength="256"
            autocomplete="off"
          />
        </div>
        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label for="rule-amount">Valor</label>
            <p-inputNumber
              inputId="rule-amount"
              mode="currency"
              [currency]="form.controls.currency.value || 'EUR'"
              locale="pt-PT"
              [min]="0.01"
              formControlName="amount"
            ></p-inputNumber>
          </div>
          <div class="flex flex-col gap-1">
            <label for="rule-currency">Moeda</label>
            <p-select
              inputId="rule-currency"
              [options]="store.currencies()"
              optionLabel="code"
              optionValue="code"
              formControlName="currency"
              styleClass="w-full"
            ></p-select>
          </div>
        </div>
        <div class="flex flex-col gap-1">
          <label for="rule-account">Conta</label>
          <p-select
            inputId="rule-account"
            [options]="store.accounts()"
            optionLabel="name"
            optionValue="id"
            formControlName="accountId"
            styleClass="w-full"
          ></p-select>
        </div>
        <div class="flex flex-col gap-1">
          <label for="rule-category">Categoria (opcional)</label>
          <p-select
            inputId="rule-category"
            [options]="store.categories()"
            optionLabel="name"
            optionValue="id"
            formControlName="categoryId"
            styleClass="w-full"
            [showClear]="true"
          ></p-select>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label for="rule-freq">Frequência</label>
            <p-select
              inputId="rule-freq"
              [options]="freqOptions"
              optionLabel="label"
              optionValue="value"
              formControlName="frequency"
              styleClass="w-full"
            ></p-select>
          </div>
          <div class="flex flex-col gap-1">
            <label for="rule-interval">Intervalo</label>
            <p-inputNumber
              inputId="rule-interval"
              [min]="1"
              formControlName="interval"
              styleClass="w-full"
            ></p-inputNumber>
          </div>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label for="rule-start">Data de início</label>
            <p-datepicker
              inputId="rule-start"
              formControlName="startDate"
              dateFormat="dd/mm/yy"
              styleClass="w-full"
            ></p-datepicker>
          </div>
          <div class="flex flex-col gap-1">
            <label for="rule-end">Data de fim (opcional)</label>
            <p-datepicker
              inputId="rule-end"
              formControlName="endDate"
              dateFormat="dd/mm/yy"
              styleClass="w-full"
            ></p-datepicker>
          </div>
        </div>

        @if (startHint(); as hint) {
          <div class="px-3 py-2 rounded-md bg-[var(--p-highlight-background)] text-[var(--p-highlight-color)] text-sm">
            {{ hint }}
          </div>
        }

        @if (editingId) {
          <div class="flex items-center gap-2">
            <label for="rule-active">Ativa</label>
            <input
              id="rule-active"
              type="checkbox"
              formControlName="isActive"
            />
          </div>
        }

        <div class="flex justify-end gap-2 pt-2">
          <p-button
            label="Cancelar"
            severity="secondary"
            [text]="true"
            type="button"
            (onClick)="close()"
          ></p-button>
          <p-button
            label="Guardar"
            icon="pi pi-check"
            type="submit"
            [disabled]="form.invalid || submitting()"
          ></p-button>
        </div>
      </form>
    </p-dialog>
  `,
})
export class RecurringRuleDialogComponent implements OnInit {
  @Input() editingId: string | null = null;
  @Output() saved = new EventEmitter<void>();
  @Output() closed = new EventEmitter<void>();

  protected readonly store = inject(FinancialStore);
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  protected readonly visible = true;
  protected readonly submitting = signal(false);

  protected readonly freqOptions = FREQUENCIES.map((value) => ({
    value,
    label: FREQUENCY_LABELS[value],
  }));

  protected readonly form = this.fb.nonNullable.group({
    description: ['', [Validators.required, Validators.maxLength(256)]],
    amount: [0 as number, [Validators.required, Validators.min(0.01)]],
    currency: ['EUR' as string, Validators.required],
    accountId: ['' as string, Validators.required],
    categoryId: [null as string | null],
    frequency: ['Monthly' as Frequency, Validators.required],
    interval: [1, [Validators.required, Validators.min(1)]],
    startDate: [new Date() as Date, Validators.required],
    endDate: [null as Date | null],
    tags: [[] as string[]],
    isActive: [true as boolean],
  });

  private readonly _startDateTouched = signal(0);

  protected readonly startHint = computed(() => {
    void this._startDateTouched();
    const start = this.form.controls.startDate.value;
    if (!start) return null;

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const startDate = new Date(start);
    startDate.setHours(0, 0, 0, 0);

    if (startDate < today) {
      const formatted = start.toLocaleDateString('pt-PT');
      return `Esta regra começou em ${formatted}; a primeira materialização será na próxima ocorrência futura.`;
    }

    return null;
  });

  async ngOnInit(): Promise<void> {
    if (this.editingId) {
      try {
        const rule = await this.api.getRecurringRule(this.editingId);
        if (rule) {
          this.patchForm(rule);
        }
      } catch {
        this.toast.add({
          severity: 'error',
          summary: 'Erro',
          detail: 'Não foi possível carregar a regra.',
        });
      }
    } else {
      const defaultCurrency = this.store.tenantSettings()?.primaryCurrency ?? 'EUR';
      this.form.patchValue({ currency: defaultCurrency });
    }

    this.form.controls.startDate.valueChanges.subscribe(() => {
      this._startDateTouched.update((n) => n + 1);
    });
  }

  private patchForm(rule: RecurringRuleDto): void {
    this.form.patchValue({
      description: rule.description,
      amount: rule.amount.amount,
      currency: rule.amount.currency,
      accountId: rule.accountId,
      categoryId: rule.categoryId,
      frequency: rule.frequency,
      interval: rule.interval,
      startDate: new Date(rule.startDate),
      endDate: rule.endDate ? new Date(rule.endDate) : null,
      tags: rule.tags ?? [],
      isActive: rule.isActive,
    });
  }

  protected close(): void {
    this.closed.emit();
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;

    this.submitting.set(true);
    try {
      const v = this.form.getRawValue();
      const req = {
        description: v.description,
        amount: v.amount,
        currency: v.currency,
        accountId: v.accountId,
        categoryId: v.categoryId,
        frequency: v.frequency,
        interval: v.interval,
        startDate: this.toDateString(v.startDate),
        endDate: v.endDate ? this.toDateString(v.endDate) : null,
        tags: v.tags.length > 0 ? v.tags : null,
      };

      if (this.editingId) {
        await this.api.updateRecurringRule(this.editingId, {
          ...req,
          isActive: v.isActive,
        });
        this.toast.add({ severity: 'success', summary: 'Regra atualizada' });
      } else {
        await this.api.createRecurringRule(req);
        this.toast.add({ severity: 'success', summary: 'Regra criada' });
      }
      this.saved.emit();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível guardar a regra.',
      });
    } finally {
      this.submitting.set(false);
    }
  }

  private toDateString(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }
}
