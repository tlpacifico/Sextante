import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { DatePickerModule } from 'primeng/datepicker';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import { AccountDto } from '../../../core/api/financial.types';

/**
 * Phase 6.5 grupo 4 — "o saldo real a DD/MM era X". Mostra o saldo
 * calculado à data (GET /balance?at=) e a diferença antes de confirmar; ao
 * confirmar, o servidor recalcula e cria um único acerto pela diferença.
 */
@Component({
  selector: 'app-reconcile-account-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, DatePickerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      [header]="'Reconciliar ' + (account?.name ?? '')"
      [visible]="visible"
      [modal]="true"
      [draggable]="false"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      [style]="{ width: '28rem' }"
      (onHide)="close.emit()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-4 mt-2">
        <div class="flex flex-col gap-1">
          <label for="reconcile-date" class="text-sm font-medium">Data</label>
          <p-datepicker
            inputId="reconcile-date"
            formControlName="date"
            dateFormat="dd/mm/yy"
            [showIcon]="true"
            [maxDate]="today"
            [minDate]="minDate"
            appendTo="body"
            styleClass="w-full"
            (onSelect)="loadCalculated()" />
        </div>

        <p class="text-sm text-[var(--p-text-muted-color)]">
          Saldo calculado nessa data:
          @if (loadingBalance()) {
            <span>a calcular…</span>
          } @else if (calculated() !== null) {
            <span class="font-medium text-[var(--p-text-color)]">{{ formatAmount(calculated()!) }} {{ account?.currency }}</span>
          } @else {
            <span>—</span>
          }
        </p>

        <div class="flex flex-col gap-1">
          <label for="reconcile-actual" class="text-sm font-medium">Saldo real</label>
          <p-inputNumber
            inputId="reconcile-actual"
            formControlName="actualBalance"
            mode="decimal"
            [minFractionDigits]="2"
            [maxFractionDigits]="2"
            locale="pt-PT"
            [suffix]="' ' + (account?.currency ?? '')"
            styleClass="w-full" />
          @if (account?.type === 'CreditCard') {
            <small class="text-[var(--p-text-muted-color)]">Dívida é negativa (ex.: −350,00).</small>
          }
        </div>

        @if (difference(); as diff) {
          <p class="text-sm" data-testid="reconcile-preview">
            Diferença: <span class="font-medium">{{ diff > 0 ? '+' : '−' }}{{ formatAmount(abs(diff)) }} {{ account?.currency }}</span>
            — será criado um acerto de {{ diff > 0 ? 'entrada' : 'saída' }}.
          </p>
        } @else if (difference() === 0) {
          <p class="text-sm" data-testid="reconcile-preview">Sem diferença — nada a acertar.</p>
        }

        <div class="flex justify-end gap-2 mt-2">
          <p-button label="Cancelar" severity="secondary" [outlined]="true" type="button" (click)="close.emit()" />
          <p-button
            type="submit"
            label="Confirmar"
            icon="pi pi-check"
            [loading]="submitting()"
            [disabled]="submitting() || loadingBalance() || form.invalid" />
        </div>
      </form>
    </p-dialog>
  `,
})
export class ReconcileAccountDialogComponent implements OnChanges {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);

  @Input() visible = false;
  @Input() account: AccountDto | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  protected readonly today = new Date();
  protected minDate: Date | null = null;

  protected readonly submitting = signal(false);
  protected readonly loadingBalance = signal(false);
  protected readonly calculated = signal<number | null>(null);

  protected readonly form = this.fb.group({
    date: this.fb.nonNullable.control(new Date(), Validators.required),
    actualBalance: this.fb.control<number | null>(null, Validators.required),
  });

  ngOnChanges(): void {
    if (!this.visible || !this.account) {
      return;
    }

    this.minDate = this.parseLocalDate(this.account.openingBalanceDate);
    this.form.reset({ date: new Date(), actualBalance: null });
    void this.loadCalculated();
  }

  async loadCalculated(): Promise<void> {
    if (!this.account) {
      return;
    }

    this.loadingBalance.set(true);
    this.calculated.set(null);
    try {
      const response = await this.api.getAccountBalance(
        this.account.id,
        this.toDateString(this.form.controls.date.value),
      );
      this.calculated.set(response.balance.amount);
    } catch {
      this.messages.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível calcular o saldo.', life: 3000 });
    } finally {
      this.loadingBalance.set(false);
    }
  }

  /** Real − calculado, arredondado ao cêntimo; null enquanto falta um dos dois. */
  protected difference(): number | null {
    const calculated = this.calculated();
    const actual = this.form.controls.actualBalance.value;
    if (calculated === null || actual === null || actual === undefined) {
      return null;
    }
    return Math.round((actual - calculated) * 100) / 100;
  }

  protected abs(value: number): number {
    return Math.abs(value);
  }

  protected formatAmount(value: number): string {
    return value.toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  async submit(): Promise<void> {
    const actualBalance = this.form.controls.actualBalance.value;
    if (!this.account || this.form.invalid || actualBalance === null) {
      return;
    }

    this.submitting.set(true);
    try {
      const response = await this.api.reconcileAccount(this.account.id, {
        date: this.toDateString(this.form.controls.date.value),
        actualBalance,
      });
      this.messages.add({
        severity: 'success',
        summary: response.adjustment ? 'Acerto criado' : 'Conta já reconciliada',
        life: 3000,
      });
      this.close.emit();
      this.saved.emit();
    } catch (err) {
      const detail = err instanceof HttpErrorResponse && err.status === 400 && typeof err.error?.detail === 'string'
        ? err.error.detail
        : 'Não foi possível reconciliar a conta.';
      this.messages.add({ severity: 'error', summary: 'Erro', detail, life: 4000 });
    } finally {
      this.submitting.set(false);
    }
  }

  // Data local → YYYY-MM-DD; nunca toISOString() (desloca o dia em UTC+N,
  // bug crítico do grupo 2).
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
