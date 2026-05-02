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
import { TextareaModule } from 'primeng/textarea';
import { SelectModule } from 'primeng/select';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import { BudgetDto } from '../../../core/api/financial.types';
import { FinancialStore } from '../state/financial.store';

interface ThresholdOption {
  value: number | 'custom';
  label: string;
}

const PRESET_THRESHOLDS: ThresholdOption[] = [
  { value: 50, label: '50%' },
  { value: 70, label: '70%' },
  { value: 80, label: '80% (default)' },
  { value: 90, label: '90%' },
  { value: 'custom', label: 'Personalizado' },
];

/**
 * Dialog para criar/editar Budget. O período (mês/ano) é imutável após
 * criar — campo desabilitado em modo edit (Phase 5b §requirements
 * §Decisions: granularidade 1 budget por categoria+mês).
 */
@Component({
  selector: 'app-budget-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DatePickerModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    TextareaModule,
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
      [header]="editingId ? 'Editar orçamento' : 'Novo orçamento'"
      [style]="{ width: '32rem' }"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      (onHide)="closed.emit()"
    >
      <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
        <div class="flex flex-col gap-1">
          <label for="bdg-category">Categoria</label>
          <p-select
            inputId="bdg-category"
            [options]="expenseCategories()"
            optionLabel="name"
            optionValue="id"
            formControlName="categoryId"
            styleClass="w-full"
            placeholder="Escolhe uma categoria de despesa"
          ></p-select>
        </div>

        <div class="flex flex-col gap-1">
          <label for="bdg-period">Mês / Ano</label>
          <p-datepicker
            inputId="bdg-period"
            view="month"
            dateFormat="mm/yy"
            formControlName="period"
            styleClass="w-full"
            [readonlyInput]="!!editingId"
            [disabled]="!!editingId"
          ></p-datepicker>
          @if (editingId) {
            <small class="text-[var(--p-text-muted-color)]">
              Período não é editável após criar — apaga e cria novo se precisares de mudar.
            </small>
          }
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label for="bdg-limit">Limite</label>
            <p-inputNumber
              inputId="bdg-limit"
              mode="currency"
              [currency]="form.controls.currency.value || 'EUR'"
              locale="pt-PT"
              [min]="0.01"
              formControlName="limit"
            ></p-inputNumber>
          </div>
          <div class="flex flex-col gap-1">
            <label for="bdg-currency">Moeda</label>
            <p-select
              inputId="bdg-currency"
              [options]="store.currencies()"
              optionLabel="code"
              optionValue="code"
              formControlName="currency"
              styleClass="w-full"
            ></p-select>
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label for="bdg-threshold-preset">Alerta a</label>
            <p-select
              inputId="bdg-threshold-preset"
              [options]="thresholdPresets"
              optionLabel="label"
              optionValue="value"
              formControlName="thresholdPreset"
              styleClass="w-full"
            ></p-select>
          </div>
          @if (showCustomThreshold()) {
            <div class="flex flex-col gap-1">
              <label for="bdg-threshold-custom">Threshold (%)</label>
              <p-inputNumber
                inputId="bdg-threshold-custom"
                [min]="1"
                [max]="99"
                formControlName="thresholdCustom"
                styleClass="w-full"
              ></p-inputNumber>
            </div>
          }
        </div>

        <div class="flex flex-col gap-1">
          <label for="bdg-notes">Notas (opcional)</label>
          <textarea
            id="bdg-notes"
            pTextarea
            formControlName="notes"
            maxlength="500"
            rows="3"
            class="w-full"
          ></textarea>
        </div>

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
export class BudgetDialogComponent implements OnInit {
  @Input() editingId: string | null = null;
  @Output() saved = new EventEmitter<void>();
  @Output() closed = new EventEmitter<void>();

  protected readonly store = inject(FinancialStore);
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  protected readonly visible = true;
  protected readonly submitting = signal(false);
  protected readonly thresholdPresets = PRESET_THRESHOLDS;

  protected readonly expenseCategories = computed(() =>
    this.store.categories().filter((c) => c.kind === 'Expense'),
  );

  protected readonly form = this.fb.nonNullable.group({
    categoryId: ['' as string, Validators.required],
    period: [new Date() as Date, Validators.required],
    limit: [0 as number, [Validators.required, Validators.min(0.01)]],
    currency: ['EUR' as string, Validators.required],
    thresholdPreset: [80 as number | 'custom', Validators.required],
    thresholdCustom: [80 as number, [Validators.min(1), Validators.max(99)]],
    notes: ['' as string],
  });

  protected readonly showCustomThreshold = signal(false);

  async ngOnInit(): Promise<void> {
    if (this.editingId) {
      try {
        const budget = await this.api.getBudget(this.editingId);
        this.patchForm(budget);
      } catch {
        this.toast.add({
          severity: 'error',
          summary: 'Erro',
          detail: 'Não foi possível carregar o orçamento.',
        });
      }
    } else {
      const defaultCurrency = this.store.tenantSettings()?.primaryCurrency ?? 'EUR';
      this.form.patchValue({ currency: defaultCurrency });
    }

    this.form.controls.thresholdPreset.valueChanges.subscribe((value) => {
      this.showCustomThreshold.set(value === 'custom');
    });
  }

  private patchForm(budget: BudgetDto): void {
    const isPreset = PRESET_THRESHOLDS.some((p) =>
      typeof p.value === 'number' && p.value === budget.alertThresholdPercent,
    );
    this.form.patchValue({
      categoryId: budget.categoryId,
      period: new Date(budget.year, budget.month - 1, 1),
      limit: budget.limitAmount,
      currency: budget.limitCurrency,
      thresholdPreset: isPreset ? budget.alertThresholdPercent : 'custom',
      thresholdCustom: budget.alertThresholdPercent,
      notes: budget.notes ?? '',
    });
    this.showCustomThreshold.set(!isPreset);
  }

  protected close(): void {
    this.closed.emit();
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;

    this.submitting.set(true);
    try {
      const v = this.form.getRawValue();
      const period = v.period;
      const threshold =
        v.thresholdPreset === 'custom' ? v.thresholdCustom : v.thresholdPreset;
      const notes = v.notes.trim() ? v.notes.trim() : null;

      if (this.editingId) {
        await this.api.updateBudget(this.editingId, {
          limitAmount: v.limit,
          limitCurrency: v.currency,
          alertThresholdPercent: threshold,
          notes,
        });
        this.toast.add({ severity: 'success', summary: 'Orçamento atualizado' });
      } else {
        await this.api.createBudget({
          categoryId: v.categoryId,
          year: period.getFullYear(),
          month: period.getMonth() + 1,
          limitAmount: v.limit,
          limitCurrency: v.currency,
          alertThresholdPercent: threshold,
          notes,
        });
        this.toast.add({ severity: 'success', summary: 'Orçamento criado' });
      }
      this.saved.emit();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível guardar o orçamento.',
      });
    } finally {
      this.submitting.set(false);
    }
  }
}
