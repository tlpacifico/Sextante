import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { DatePickerModule } from 'primeng/datepicker';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../../core/api/financial-api.service';

/**
 * Dados suficientes para preencher o formulário ao editar uma transferência
 * a partir de uma linha da tabela (que só tem uma perna carregada). O valor
 * da perna oposta é o melhor palpite (igual ao da perna clicada — correto
 * no caso mais comum de mesma moeda); em transferências entre moedas
 * diferentes o utilizador confirma/corrige antes de guardar.
 */
export interface TransferEditSeed {
  transferId: string;
  fromAccountId: string;
  toAccountId: string;
  occurredAt: string;
  amountOut: number;
  amountIn: number;
  description: string | null;
}

/**
 * "Nova transferência" e edição de uma transferência existente — mesmo
 * componente, modo definido por `editing` (null = criar).
 */
@Component({
  selector: 'app-transfer-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DialogModule,
    ButtonModule,
    SelectModule,
    InputNumberModule,
    InputTextModule,
    DatePickerModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      [header]="editing ? 'Editar transferência' : 'Nova transferência'"
      [visible]="visible"
      [modal]="true"
      [draggable]="false"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      [style]="{ width: '28rem' }"
      (onHide)="close.emit()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-4 mt-2">
        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Conta de origem</label>
          <p-select
            formControlName="fromAccountId"
            [options]="accounts"
            optionLabel="name"
            optionValue="id"
            placeholder="Selecionar"
            styleClass="w-full" />
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Conta de destino</label>
          <p-select
            formControlName="toAccountId"
            [options]="accounts"
            optionLabel="name"
            optionValue="id"
            placeholder="Selecionar"
            styleClass="w-full" />
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Data</label>
          <p-datepicker formControlName="occurredAt" dateFormat="dd/mm/yy" styleClass="w-full" />
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Valor</label>
          <p-inputnumber formControlName="amountOut" mode="currency" [currency]="fromCurrency() || 'EUR'" styleClass="w-full" />
        </div>

        @if (crossCurrency()) {
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Valor recebido</label>
            <p-inputnumber formControlName="amountIn" mode="currency" [currency]="toCurrency() || 'EUR'" styleClass="w-full" />
          </div>
        }

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Descrição</label>
          <input pInputText formControlName="description" class="w-full" />
        </div>

        <div class="flex justify-end gap-2 mt-2">
          <p-button label="Cancelar" severity="secondary" [outlined]="true" type="button" (click)="close.emit()" />
          <p-button type="submit" label="Guardar" icon="pi pi-check" [loading]="submitting()" [disabled]="submitting()" />
        </div>
      </form>
    </p-dialog>
  `,
})
export class TransferDialogComponent implements OnChanges {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);

  @Input() visible = false;
  @Input() accounts: { id: string; name: string; currency: string }[] = [];
  @Input() editing: TransferEditSeed | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    fromAccountId: ['', Validators.required],
    toAccountId: ['', Validators.required],
    occurredAt: [new Date(), Validators.required],
    amountOut: [0, [Validators.required, Validators.min(0.01)]],
    amountIn: [null as number | null],
    description: [''],
  });

  ngOnChanges(): void {
    if (this.visible) {
      if (this.editing) {
        this.form.reset({
          fromAccountId: this.editing.fromAccountId,
          toAccountId: this.editing.toAccountId,
          occurredAt: new Date(this.editing.occurredAt),
          amountOut: this.editing.amountOut,
          amountIn: this.editing.amountIn,
          description: this.editing.description ?? '',
        });
      } else {
        this.form.reset({
          fromAccountId: '',
          toAccountId: '',
          occurredAt: new Date(),
          amountOut: 0,
          amountIn: null,
          description: '',
        });
      }
    }
  }

  protected fromCurrency(): string | undefined {
    return this.accounts.find(a => a.id === this.form.controls.fromAccountId.value)?.currency;
  }

  protected toCurrency(): string | undefined {
    return this.accounts.find(a => a.id === this.form.controls.toAccountId.value)?.currency;
  }

  protected crossCurrency(): boolean {
    const from = this.fromCurrency();
    const to = this.toCurrency();
    return !!from && !!to && from !== to;
  }

  async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.submitting.set(true);
    try {
      const fv = this.form.getRawValue();
      const req = {
        fromAccountId: fv.fromAccountId,
        toAccountId: fv.toAccountId,
        occurredAt: fv.occurredAt.toISOString(),
        amountOut: fv.amountOut,
        amountIn: this.crossCurrency() ? fv.amountIn : null,
        description: fv.description || null,
      };

      if (this.editing) {
        await this.api.updateTransfer(this.editing.transferId, req);
        this.messages.add({ severity: 'success', summary: 'Atualizada', detail: 'Transferência atualizada.', life: 3000 });
      } else {
        await this.api.createTransfer(req);
        this.messages.add({ severity: 'success', summary: 'Criada', detail: 'Transferência criada.', life: 3000 });
      }
      this.close.emit();
      this.saved.emit();
    } catch {
      this.messages.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao guardar transferência.', life: 3000 });
    } finally {
      this.submitting.set(false);
    }
  }
}
