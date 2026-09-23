import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, OnInit, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { DatePickerModule } from 'primeng/datepicker';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../../core/api/financial-api.service';
import { TransactionDto } from '../../../../core/api/financial.types';

@Component({
  selector: 'app-transaction-edit-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DialogModule,
    ButtonModule,
    InputTextModule,
    InputNumberModule,
    SelectModule,
    DatePickerModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      header="Editar transação"
      [visible]="visible"
      [modal]="true"
      [draggable]="false"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      [style]="{ width: '28rem' }"
      (onHide)="close.emit()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-4 mt-2">
        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Data</label>
          <p-datepicker formControlName="occurredAt" dateFormat="dd/mm/yy" styleClass="w-full" />
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Conta</label>
          <p-select
            formControlName="accountId"
            [options]="accounts()"
            optionLabel="name"
            optionValue="id"
            placeholder="Selecionar"
            styleClass="w-full" />
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Categoria</label>
          <p-select
            formControlName="categoryId"
            [options]="categories()"
            optionLabel="name"
            optionValue="id"
            placeholder="Selecionar"
            styleClass="w-full" />
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Valor</label>
          <p-inputnumber formControlName="amount" mode="currency" currency="EUR" styleClass="w-full" />
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Descrição</label>
          <input pInputText formControlName="description" class="w-full" />
        </div>

        <div class="flex justify-end gap-2 mt-2">
          <p-button label="Cancelar" severity="secondary" [outlined]="true" (click)="close.emit()" />
          <p-button type="submit" label="Guardar" icon="pi pi-check" [loading]="submitting()" [disabled]="submitting()" />
        </div>
      </form>
    </p-dialog>
  `,
})
export class TransactionEditDialogComponent implements OnChanges, OnInit {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);

  @Input() visible = false;
  @Input() transaction: TransactionDto | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  protected readonly submitting = signal(false);
  protected readonly accounts = signal<{ id: string; name: string }[]>([]);
  protected readonly categories = signal<{ id: string; name: string }[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    occurredAt: [new Date(), Validators.required],
    accountId: ['', Validators.required],
    categoryId: ['', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    description: [''],
  });

  ngOnInit(): void {
    this.loadReferenceData();
  }

  ngOnChanges(): void {
    if (this.visible && this.transaction) {
      this.patchForm(this.transaction);
    }
  }

  private async loadReferenceData(): Promise<void> {
    try {
      const [accs, cats] = await Promise.all([
        this.api.listAccounts(),
        this.api.listCategories(),
      ]);
      this.accounts.set(accs.map(a => ({ id: a.id, name: a.name })));
      this.categories.set(cats.map(c => ({ id: c.id, name: c.name })));
    } catch { /* ignore */ }
  }

  private patchForm(tx: TransactionDto): void {
    this.form.patchValue({
      occurredAt: new Date(tx.occurredAt),
      accountId: tx.accountId,
      categoryId: tx.categoryId ?? '',
      amount: tx.amount.amount,
      description: tx.description ?? '',
    });
  }

  async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    if (!this.transaction) return;

    this.submitting.set(true);
    try {
      const fv = this.form.getRawValue();
      await this.api.updateTransaction(this.transaction.id, {
        accountId: fv.accountId,
        categoryId: fv.categoryId,
        occurredAt: fv.occurredAt.toISOString(),
        amount: fv.amount,
        description: fv.description,
        tags: [],
      });
      this.messages.add({ severity: 'success', summary: 'Actualizada', detail: 'Transação actualizada com sucesso.', life: 3000 });
      this.close.emit();
      this.saved.emit();
    } catch {
      this.messages.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao actualizar transação.', life: 3000 });
    } finally {
      this.submitting.set(false);
    }
  }
}
