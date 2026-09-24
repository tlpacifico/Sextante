import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { DatePickerModule } from 'primeng/datepicker';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import { InstallmentPlanDto, InstallmentPlanRequest, TransactionDto } from '../../../core/api/financial.types';

/**
 * Phase 6.5 grupo 6 — criar/editar um plano de prestações. Modos:
 * `plan` preenchido = editar; `purchase` preenchido = criar a partir de uma
 * compra (valor fixo = valor da compra); nenhum = criar à mão (incluindo
 * planos que começaram antes de usar o Sextante).
 */
@Component({
  selector: 'app-installment-plan-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, DatePickerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      [header]="plan ? 'Editar plano de prestações' : 'Compra em prestações'"
      [visible]="visible"
      [modal]="true"
      [draggable]="false"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      [style]="{ width: '30rem' }"
      (onHide)="close.emit()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-4 mt-2">
        <div class="flex flex-col gap-1">
          <label for="ip-description" class="text-sm font-medium">Descrição</label>
          <input id="ip-description" pInputText formControlName="description" maxlength="200" autocomplete="off" />
        </div>

        <div class="flex flex-col gap-1">
          <label for="ip-total" class="text-sm font-medium">Valor total</label>
          <p-inputNumber
            inputId="ip-total"
            formControlName="totalAmount"
            mode="currency"
            [currency]="currency"
            locale="pt-PT"
            [min]="0"
            styleClass="w-full" />
          @if (purchase) {
            <small class="text-[var(--p-text-muted-color)]">Valor da compra — a compra conta pelo total na data em que foi feita.</small>
          }
        </div>

        @if (!purchase && !plan?.purchaseTransactionId) {
          <div class="flex flex-col gap-1">
            <label for="ip-purchase-date" class="text-sm font-medium">Data da compra</label>
            <p-datepicker
              inputId="ip-purchase-date"
              formControlName="purchaseDate"
              dateFormat="dd/mm/yy"
              [showIcon]="true"
              [maxDate]="today"
              appendTo="body"
              styleClass="w-full" />
            <small class="text-[var(--p-text-muted-color)]">Diz a partir de que extrato as prestações por faturar se descontam no pagamento.</small>
          </div>
        }

        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1 min-w-0">
            <label for="ip-count" class="text-sm font-medium">N.º de prestações</label>
            <p-inputNumber inputId="ip-count" formControlName="installmentCount" [min]="2" [max]="120" [useGrouping]="false"
              styleClass="w-full" inputStyleClass="w-full min-w-0" />
          </div>
          <div class="flex flex-col gap-1 min-w-0">
            <label for="ip-paid" class="text-sm font-medium">Já pagas</label>
            <p-inputNumber inputId="ip-paid" formControlName="installmentsAlreadyPaid" [min]="0" [useGrouping]="false"
              styleClass="w-full" inputStyleClass="w-full min-w-0" />
          </div>
        </div>
        <small class="text-[var(--p-text-muted-color)] -mt-2">"Já pagas" é para planos que começaram antes de usar o Sextante.</small>

        <div class="flex flex-col gap-1">
          <label for="ip-first" class="text-sm font-medium">Data da 1.ª prestação</label>
          <p-datepicker
            inputId="ip-first"
            formControlName="firstInstallmentDate"
            dateFormat="dd/mm/yy"
            [showIcon]="true"
            appendTo="body"
            styleClass="w-full" />
          <small class="text-[var(--p-text-muted-color)]">Data da prestação n.º 1, mesmo que já tenha passado.</small>
        </div>

        <div class="flex flex-col gap-1">
          <label for="ip-rate" class="text-sm font-medium">TAN (%) — opcional</label>
          <p-inputNumber
            inputId="ip-rate"
            formControlName="annualRate"
            mode="decimal"
            [minFractionDigits]="0"
            [maxFractionDigits]="4"
            [min]="0"
            [max]="100"
            locale="pt-PT" />
        </div>

        @if (preview(); as p) {
          <p class="text-sm" data-testid="installment-preview">
            {{ p.count }} × {{ p.amount }} {{ currency }} <span class="text-[var(--p-text-muted-color)]">(a última acerta os cêntimos)</span>
          </p>
        }

        <div class="flex justify-end gap-2 mt-2">
          <p-button label="Cancelar" severity="secondary" [outlined]="true" type="button" (click)="close.emit()" />
          <p-button
            type="submit"
            label="Guardar"
            icon="pi pi-check"
            [loading]="submitting()"
            [disabled]="submitting() || form.invalid" />
        </div>
      </form>
    </p-dialog>
  `,
})
export class InstallmentPlanDialogComponent implements OnChanges {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);

  @Input() visible = false;
  @Input() accountId: string | null = null;
  @Input() currency = 'EUR';
  @Input() plan: InstallmentPlanDto | null = null;
  @Input() purchase: TransactionDto | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  protected readonly submitting = signal(false);
  protected readonly today = new Date();

  protected readonly form = this.fb.group({
    description: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(200)]),
    totalAmount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    installmentCount: this.fb.control<number | null>(null, [Validators.required, Validators.min(2), Validators.max(120)]),
    installmentsAlreadyPaid: this.fb.nonNullable.control(0, [Validators.required, Validators.min(0)]),
    purchaseDate: this.fb.nonNullable.control(new Date(), Validators.required),
    firstInstallmentDate: this.fb.nonNullable.control(new Date(), Validators.required),
    annualRate: this.fb.control<number | null>(null, [Validators.min(0), Validators.max(100)]),
  });

  ngOnChanges(): void {
    if (!this.visible) {
      return;
    }

    const totalControl = this.form.controls.totalAmount;
    if (this.plan) {
      this.form.reset({
        description: this.plan.description,
        totalAmount: this.plan.totalAmount.amount,
        installmentCount: this.plan.installmentCount,
        installmentsAlreadyPaid: this.plan.installmentsAlreadyPaid,
        purchaseDate: this.parseLocalDate(this.plan.purchaseDate),
        firstInstallmentDate: this.parseLocalDate(this.plan.firstInstallmentDate),
        annualRate: this.plan.annualRate,
      });
      totalControl.enable();
    } else if (this.purchase) {
      // 1.ª prestação por defeito: mesmo dia, mês seguinte ao da compra.
      const purchaseDate = new Date(this.purchase.occurredAt);
      const first = new Date(purchaseDate.getFullYear(), purchaseDate.getMonth() + 1, purchaseDate.getDate());
      this.form.reset({
        description: this.purchase.description ?? '',
        totalAmount: this.purchase.amount.amount,
        installmentCount: null,
        installmentsAlreadyPaid: 0,
        purchaseDate: new Date(purchaseDate.getFullYear(), purchaseDate.getMonth(), purchaseDate.getDate()),
        firstInstallmentDate: first,
        annualRate: null,
      });
      totalControl.disable();
    } else {
      this.form.reset({
        description: '',
        totalAmount: null,
        installmentCount: null,
        installmentsAlreadyPaid: 0,
        purchaseDate: new Date(),
        firstInstallmentDate: new Date(),
        annualRate: null,
      });
      totalControl.enable();
    }
  }

  /** Valor de cada prestação (arredondado para baixo), como no backend. */
  protected preview(): { count: number; amount: string } | null {
    const { totalAmount, installmentCount } = this.form.getRawValue();
    if (!totalAmount || !installmentCount || installmentCount < 2) {
      return null;
    }
    const amount = Math.floor((totalAmount / installmentCount) * 100) / 100;
    return {
      count: installmentCount,
      amount: amount.toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
    };
  }

  async submit(): Promise<void> {
    const value = this.form.getRawValue();
    if (this.form.invalid || value.totalAmount === null || value.installmentCount === null) {
      return;
    }

    const request: InstallmentPlanRequest = {
      purchaseTransactionId: this.plan ? this.plan.purchaseTransactionId : (this.purchase?.id ?? null),
      description: value.description.trim(),
      totalAmount: value.totalAmount,
      installmentCount: value.installmentCount,
      installmentsAlreadyPaid: value.installmentsAlreadyPaid,
      purchaseDate: this.toDateString(value.purchaseDate),
      firstInstallmentDate: this.toDateString(value.firstInstallmentDate),
      annualRate: value.annualRate,
    };

    this.submitting.set(true);
    try {
      if (this.plan) {
        await this.api.updateInstallmentPlan(this.plan.id, request);
      } else {
        await this.api.createInstallmentPlan({ accountId: this.accountId ?? '', ...request });
      }
      this.messages.add({ severity: 'success', summary: 'Plano de prestações guardado', life: 3000 });
      this.close.emit();
      this.saved.emit();
    } catch (err) {
      const detail = err instanceof HttpErrorResponse && err.status === 400 && typeof err.error?.detail === 'string'
        ? err.error.detail
        : 'Não foi possível guardar o plano de prestações.';
      this.messages.add({ severity: 'error', summary: 'Erro', detail, life: 4000 });
    } finally {
      this.submitting.set(false);
    }
  }

  // Data local → YYYY-MM-DD; nunca toISOString() (desloca o dia em UTC+N).
  private toDateString(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private parseLocalDate(value: string): Date {
    const [y, m, d] = value.split('-').map(Number);
    return new Date(y, m - 1, d);
  }
}
