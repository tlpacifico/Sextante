import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../../core/api/financial-api.service';
import { TransactionDto } from '../../../../core/api/financial.types';

/**
 * Grupo 3 — converte uma transação Regular existente numa perna de
 * transferência, ligando a uma contraparte já importada ou criando-a
 * (R3: só automático quando as contas têm a mesma moeda).
 */
@Component({
  selector: 'app-mark-as-transfer-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      header="Marcar como transferência"
      [visible]="visible"
      [modal]="true"
      [draggable]="false"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      [style]="{ width: '28rem' }"
      (onHide)="close.emit()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-4 mt-2">
        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Conta contraparte</label>
          <p-select
            formControlName="counterpartAccountId"
            [options]="counterpartOptions()"
            optionLabel="name"
            optionValue="id"
            placeholder="Selecionar"
            styleClass="w-full"
            (onChange)="onCounterpartAccountChange()" />
        </div>

        @if (form.controls.counterpartAccountId.value) {
          @if (loadingCandidates()) {
            <p class="text-sm text-[var(--p-text-muted-color)]">A procurar transações correspondentes…</p>
          } @else if (candidates().length > 0) {
            <div class="flex flex-col gap-1">
              <label class="text-sm font-medium">Ligar a uma transação existente</label>
              <p-select
                formControlName="counterpartTransactionId"
                [options]="candidateOptions()"
                optionLabel="label"
                optionValue="id"
                placeholder="Nenhuma (criar nova)"
                [showClear]="true"
                styleClass="w-full" />
            </div>
          }

          @if (!form.controls.counterpartTransactionId.value) {
            @if (sameCurrency()) {
              <p class="text-sm text-[var(--p-text-muted-color)]">
                Sem contraparte escolhida, cria-se uma nova transação na conta selecionada.
              </p>
            } @else {
              <p class="text-sm text-danger">
                As contas têm moedas diferentes — escolha uma transação existente para ligar; não é possível criar a contraparte automaticamente.
              </p>
            }
          }
        }

        <div class="flex justify-end gap-2 mt-2">
          <p-button label="Cancelar" severity="secondary" [outlined]="true" type="button" (click)="close.emit()" />
          <p-button
            type="submit"
            label="Confirmar"
            icon="pi pi-check"
            [loading]="submitting()"
            [disabled]="submitting() || form.invalid || !canSubmit()" />
        </div>
      </form>
    </p-dialog>
  `,
})
export class MarkAsTransferDialogComponent implements OnChanges {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);

  @Input() visible = false;
  @Input() transaction: TransactionDto | null = null;
  @Input() accounts: { id: string; name: string; currency: string }[] = [];
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  protected readonly submitting = signal(false);
  protected readonly loadingCandidates = signal(false);
  protected readonly candidates = signal<TransactionDto[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    counterpartAccountId: ['', Validators.required],
    counterpartTransactionId: [null as string | null],
  });

  ngOnChanges(): void {
    if (this.visible) {
      this.form.reset({ counterpartAccountId: '', counterpartTransactionId: null });
      this.candidates.set([]);
    }
  }

  protected counterpartOptions(): { id: string; name: string }[] {
    return this.accounts.filter(a => a.id !== this.transaction?.accountId);
  }

  protected candidateOptions(): { id: string; label: string }[] {
    return this.candidates().map(c => ({
      id: c.id,
      label: `${new Date(c.occurredAt).toLocaleDateString('pt-PT')} — ${c.description ?? 'Sem descrição'} — ${c.amount.amount} ${c.amount.currency}`,
    }));
  }

  protected sameCurrency(): boolean {
    const counterpartAccountId = this.form.controls.counterpartAccountId.value;
    const counterpartAccount = this.accounts.find(a => a.id === counterpartAccountId);
    return !!counterpartAccount && counterpartAccount.currency === this.transaction?.amount.currency;
  }

  protected canSubmit(): boolean {
    return !!this.form.controls.counterpartTransactionId.value || this.sameCurrency();
  }

  protected async onCounterpartAccountChange(): Promise<void> {
    this.form.controls.counterpartTransactionId.setValue(null);
    const counterpartAccountId = this.form.controls.counterpartAccountId.value;
    if (!counterpartAccountId || !this.transaction) {
      this.candidates.set([]);
      return;
    }

    this.loadingCandidates.set(true);
    try {
      // A API só filtra `kind` por Income/Expense/Transfer/Adjustment (não
      // "Regular" diretamente) — o filtro decisivo por Regular + sentido
      // oposto faz-se aqui, sobre o resultado já restringido por conta+valor.
      const results = await this.api.listTransactionsSimple({
        accountIds: [counterpartAccountId],
        amountMin: this.transaction.amount.amount,
        amountMax: this.transaction.amount.amount,
      });
      this.candidates.set(results.filter(t => t.kind === 'Regular' && t.direction !== this.transaction!.direction));
    } catch {
      this.candidates.set([]);
    } finally {
      this.loadingCandidates.set(false);
    }
  }

  async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || !this.transaction || !this.canSubmit()) return;

    this.submitting.set(true);
    try {
      const fv = this.form.getRawValue();
      await this.api.convertToTransfer(this.transaction.id, {
        counterpartAccountId: fv.counterpartAccountId,
        counterpartTransactionId: fv.counterpartTransactionId,
      });
      this.messages.add({ severity: 'success', summary: 'Convertida', detail: 'Transação marcada como transferência.', life: 3000 });
      this.close.emit();
      this.saved.emit();
    } catch {
      this.messages.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao marcar como transferência.', life: 3000 });
    } finally {
      this.submitting.set(false);
    }
  }
}
