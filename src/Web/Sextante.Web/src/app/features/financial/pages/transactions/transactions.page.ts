import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { debounceTime } from 'rxjs';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectButtonModule } from 'primeng/selectbutton';
import { DialogModule } from 'primeng/dialog';
import { SkeletonModule } from 'primeng/skeleton';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../../core/api/financial-api.service';
import { TransactionDto } from '../../../../core/api/financial.types';
import { PageHeaderComponent } from '../../../../shared/ui/page-header/page-header.component';
import { EmptyStateComponent } from '../../../../shared/ui/empty-state/empty-state.component';
import { TransactionEditDialogComponent } from './transaction-edit.dialog';
import { RecategorizeDialogComponent } from './recategorize.dialog';

@Component({
  selector: 'app-transactions-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DecimalPipe,
    DatePipe,
    TableModule,
    ButtonModule,
    InputTextModule,
    MultiSelectModule,
    DatePickerModule,
    InputNumberModule,
    SelectButtonModule,
    DialogModule,
    SkeletonModule,
    ToastModule,
    PageHeaderComponent,
    EmptyStateComponent,
    TransactionEditDialogComponent,
    RecategorizeDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <sxt-page-header title="Transações" description="Gere todas as suas transações. Filtre, edite e recategorize." />

    <!-- Filters -->
    <form [formGroup]="filterForm" class="mb-4 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-neutral-500">Intervalo</span>
        <p-datepicker
          formControlName="dateRange"
          selectionMode="range"
          [showIcon]="true"
          dateFormat="dd/mm/yy"
          styleClass="w-full text-sm" />
      </div>

      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-neutral-500">Conta</span>
        <p-multiSelect
          formControlName="accountIds"
          [options]="accounts()"
          optionLabel="name"
          optionValue="id"
          [showClear]="true"
          placeholder="Todas"
          styleClass="w-full" />
      </div>

      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-neutral-500">Categoria</span>
        <p-multiSelect
          formControlName="categoryIds"
          [options]="categories()"
          optionLabel="name"
          optionValue="id"
          [showClear]="true"
          placeholder="Todas"
          styleClass="w-full" />
      </div>

      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-neutral-500">Tipo</span>
        <p-selectbutton
          formControlName="kind"
          [options]="kindOptions"
          optionLabel="label"
          optionValue="value"
          styleClass="text-sm" />
      </div>

      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-neutral-500">Descrição</span>
        <input pInputText formControlName="description" placeholder="Pesquisar..." class="text-sm w-full" />
      </div>

      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-neutral-500">Valor mín.</span>
        <p-inputnumber formControlName="amountMin" mode="currency" currency="EUR" styleClass="w-full text-sm" />
      </div>

      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-neutral-500">Valor máx.</span>
        <p-inputnumber formControlName="amountMax" mode="currency" currency="EUR" styleClass="w-full text-sm" />
      </div>
    </form>

    <!-- Actions -->
    <div class="mb-4 flex flex-wrap items-center justify-between gap-2">
      <span class="text-sm text-neutral-500">
        {{ transactions().length }} transação(ões)
      </span>
      <p-button
        label="Exportar CSV"
        icon="pi pi-download"
        severity="secondary"
        [outlined]="true"
        [loading]="exporting()"
        [disabled]="transactions().length === 0"
        (click)="exportCsv()" />
    </div>

    <!-- Bulk actions -->
    @if (selectedIds().length > 0) {
      <div class="mb-4 flex items-center gap-3 p-3 bg-primary-50 dark:bg-primary-900/20 rounded-lg">
        <span class="text-sm font-medium">{{ selectedIds().length }} selecionada(s)</span>
        <p-button
          label="Recategorizar"
          icon="pi pi-tags"
          severity="secondary"
          size="small"
          (click)="openRecategorize()" />
      </div>
    }

    <!-- Table -->
    @if (loading()) {
      <div class="space-y-2">
        @for (i of [1,2,3,4,5]; track i) {
          <p-skeleton height="3rem" />
        }
      </div>
    } @else if (transactions().length === 0 && !loading()) {
      <sxt-empty-state
        icon="pi pi-receipt"
        title="Sem transações"
        description="As suas transações aparecerão aqui. Importe um extrato CSV para começar." />
    } @else {
      <div class="overflow-x-auto">
        <p-table
          [value]="transactions()"
          [paginator]="true"
          [rows]="25"
          [rowsPerPageOptions]="[10, 25, 50, 100]"
          [scrollable]="true"
          sortField="occurredAt"
          [sortOrder]="-1"
          [tableStyle]="{ 'min-width': '50rem' }"
          styleClass="text-sm"
          selectionMode="multiple"
          [(selection)]="selected"
          (selectionChange)="onSelectionChange()">
          <ng-template pTemplate="header">
            <tr>
              <th style="width: 3rem"></th>
              <th pSortableColumn="occurredAt">Data <p-sortIcon field="occurredAt" /></th>
              <th>Conta</th>
              <th>Categoria</th>
              <th>Descrição</th>
              <th pSortableColumn="amount">Valor <p-sortIcon field="amount" /></th>
              <th style="width: 5rem">Ações</th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-tx>
            <tr>
              <td>
                <p-tableCheckbox [value]="tx" />
              </td>
              <td>{{ tx.occurredAt | date:'dd/MM/yyyy' }}</td>
              <td class="text-sm">{{ getAccountName(tx.accountId) }}</td>
              <td>
                <span class="inline-flex items-center gap-1 text-sm">
                  <i [class]="getCategoryIcon(tx.categoryId)" class="text-xs"></i>
                  {{ getCategoryName(tx.categoryId) }}
                </span>
              </td>
              <td class="text-sm max-w-[200px] truncate">{{ tx.description || '—' }}</td>
              <td>
                <span [class]="isIncome(tx) ? 'text-success font-medium' : 'text-danger font-medium'">
                  {{ isIncome(tx) ? '+' : '−' }}{{ tx.amount.amount | number:'1.2-2' }} {{ tx.amount.currency }}
                </span>
              </td>
              <td>
                <p-button
                  icon="pi pi-pencil"
                  severity="secondary"
                  [text]="true"
                  size="small"
                  (click)="editTransaction(tx)" />
                <p-button
                  icon="pi pi-trash"
                  severity="danger"
                  [text]="true"
                  size="small"
                  (click)="archiveTransaction(tx)" />
              </td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    }

    <app-transaction-edit-dialog
      [visible]="editDialogVisible()"
      [transaction]="selectedTransaction()"
      (close)="editDialogVisible.set(false)"
      (saved)="onTransactionSaved()" />

    <app-recategorize-dialog
      [visible]="recategorizeDialogVisible()"
      [transactionIds]="selectedIds()"
      [categories]="categories()"
      (close)="recategorizeDialogVisible.set(false)"
      (saved)="onRecategorized()" />
  `,
})
export class TransactionsPage implements OnInit {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);

  protected readonly loading = signal(true);
  protected readonly transactions = signal<TransactionDto[]>([]);
  protected readonly accounts = signal<{ id: string; name: string }[]>([]);
  protected readonly categories = signal<{ id: string; name: string; iconName: string; colorHex: string; kind: string }[]>([]);
  protected readonly selected = signal<TransactionDto[]>([]);
  protected readonly selectedTransaction = signal<TransactionDto | null>(null);
  protected readonly editDialogVisible = signal(false);
  protected readonly recategorizeDialogVisible = signal(false);
  protected readonly exporting = signal(false);

  protected readonly kindOptions = [
    { label: 'Todos', value: null },
    { label: 'Despesas', value: 'Expense' },
    { label: 'Receitas', value: 'Income' },
  ];

  protected readonly filterForm = this.fb.nonNullable.group({
    dateRange: [null as [Date, Date] | null],
    accountIds: [[] as string[]],
    categoryIds: [[] as string[]],
    kind: [null as string | null],
    description: [''],
    amountMin: [null as number | null],
    amountMax: [null as number | null],
  });

  selectedIds() {
    return this.selected().map(t => t.id);
  }

  ngOnInit(): void {
    this.loadReferenceData();
    this.filterForm.valueChanges.pipe(debounceTime(300)).subscribe(() => this.loadTransactions());
    this.loadTransactions();
  }

  private async loadReferenceData(): Promise<void> {
    try {
      const [accs, cats] = await Promise.all([
        this.api.listAccounts(),
        this.api.listCategories(),
      ]);
      this.accounts.set(accs.map(a => ({ id: a.id, name: a.name })));
      this.categories.set(cats.map(c => ({
        id: c.id,
        name: c.name,
        iconName: c.iconName,
        colorHex: c.colorHex,
        kind: c.kind,
      })));
    } catch {
      // Silencioso — se falhar, os selects ficam vazios.
    }
  }

  /**
   * Phase 6 — todos os filtros são aplicados no servidor; filtrar em
   * memória só funcionava dentro da página devolvida. Partilhado entre a
   * listagem e o export para que o CSV corresponda ao que está no ecrã.
   */
  private currentFilter() {
    const f = this.filterForm.getRawValue();
    const [d1, d2] = f.dateRange ?? [null, null];
    return {
      dateFrom: d1 ? new Date(d1).toISOString() : undefined,
      dateTo: d2 ? new Date(d2).toISOString() : undefined,
      accountIds: f.accountIds?.length ? f.accountIds : undefined,
      categoryIds: f.categoryIds?.length ? f.categoryIds : undefined,
      kind: f.kind ? (f.kind as 'Income' | 'Expense') : undefined,
      descriptionContains: f.description || undefined,
      amountMin: f.amountMin ?? undefined,
      amountMax: f.amountMax ?? undefined,
    };
  }

  protected async exportCsv(): Promise<void> {
    this.exporting.set(true);
    try {
      const blob = await this.api.exportTransactions(this.currentFilter());
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `transacoes-${new Date().toISOString().slice(0, 10)}.csv`;
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Falha ao exportar transações.',
        life: 3000,
      });
    } finally {
      this.exporting.set(false);
    }
  }

  private async loadTransactions(): Promise<void> {
    this.loading.set(true);
    try {
      const items = await this.api.listTransactionsSimple(this.currentFilter());
      this.transactions.set(items);
    } catch {
      this.messages.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao carregar transações.', life: 3000 });
    } finally {
      this.loading.set(false);
    }
  }

  getAccountName(id: string): string {
    return this.accounts().find(a => a.id === id)?.name ?? '—';
  }

  getCategoryName(id: string): string {
    return this.categories().find(c => c.id === id)?.name ?? '—';
  }

  getCategoryIcon(id: string): string {
    const cat = this.categories().find(c => c.id === id);
    return cat?.iconName ? `pi pi-${cat.iconName}` : 'pi pi-tag';
  }

  isIncome(tx: TransactionDto): boolean {
    const cat = this.categories().find(c => c.id === tx.categoryId);
    return cat?.kind === 'Income';
  }

  protected editTransaction(tx: TransactionDto): void {
    this.selectedTransaction.set(tx);
    this.editDialogVisible.set(true);
  }

  protected async archiveTransaction(tx: TransactionDto): Promise<void> {
    try {
      await this.api.archiveTransaction(tx.id);
      this.messages.add({ severity: 'success', summary: 'Eliminada', detail: 'Transação arquivada.', life: 3000 });
      this.loadTransactions();
    } catch {
      this.messages.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao arquivar transação.', life: 3000 });
    }
  }

  protected onTransactionSaved(): void {
    this.loadTransactions();
  }

  protected onSelectionChange(): void {
    // selection is handled by signal
  }

  protected openRecategorize(): void {
    this.recategorizeDialogVisible.set(true);
  }

  protected onRecategorized(): void {
    this.selected.set([]);
    this.loadTransactions();
  }
}
