import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DatePickerModule } from 'primeng/datepicker';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { ConfirmationService, MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import { BudgetDto } from '../../../core/api/financial.types';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { FinancialStore } from '../state/financial.store';
import { BudgetDialogComponent } from './budget.dialog';

/**
 * Página principal de orçamentos: month picker + tabela com cores
 * por threshold (verde < 80, amarelo 80–99, vermelho ≥ 100).
 */
@Component({
  selector: 'app-budgets-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    ConfirmDialogModule,
    DatePickerModule,
    TableModule,
    TagModule,
    ToastModule,
    MoneyPipe,
    BudgetDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService, ConfirmationService],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between flex-wrap gap-2 mb-4">
        <h1 class="text-xl md:text-2xl font-semibold">Orçamentos</h1>
        <div class="flex items-center gap-2">
          <p-datepicker
            view="month"
            dateFormat="mm/yy"
            [(ngModel)]="periodPicker"
            (onSelect)="onPeriodChange()"
            styleClass="w-40"
            data-testid="budgets-period-picker"
          ></p-datepicker>
          <p-button
            label="Novo orçamento"
            icon="pi pi-plus"
            (onClick)="openCreate()"
            data-testid="budgets-new-button"
          ></p-button>
        </div>
      </div>

      <div class="overflow-x-auto">
        <p-table
          [value]="budgets()"
          [tableStyle]="{ 'min-width': '50rem' }"
          styleClass="p-datatable-sm"
          dataKey="id"
        >
          <ng-template pTemplate="header">
            <tr>
              <th>Categoria</th>
              <th style="width: 8rem">Limite</th>
              <th style="width: 8rem">Gasto</th>
              <th style="width: 8rem">Restante</th>
              <th style="width: 7rem">% usado</th>
              <th style="width: 8rem">Projeção</th>
              <th style="width: 6rem">Threshold</th>
              <th style="width: 7rem"></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-budget>
            <tr [class]="rowClassFor(budget)">
              <td>{{ categoryNameFor(budget.categoryId) }}</td>
              <td>{{ budget.limitAmount | money: budget.limitCurrency }}</td>
              <td>{{ budget.progress.spentAmount | money: budget.limitCurrency }}</td>
              <td>{{ budget.progress.remainingAmount | money: budget.limitCurrency }}</td>
              <td>
                <p-tag
                  [value]="budget.progress.percentUsed.toFixed(0) + '%'"
                  [severity]="severityFor(budget)"
                  [rounded]="true"
                ></p-tag>
              </td>
              <td>
                @if (budget.progress.projectedAmount != null) {
                  {{ budget.progress.projectedAmount | money: budget.limitCurrency }}
                } @else {
                  <span class="text-[var(--p-text-muted-color)]">—</span>
                }
                @if (budget.progress.hasIncompleteRates) {
                  <p-tag
                    value="Parcial"
                    severity="warn"
                    [rounded]="true"
                    pTooltip="Algumas transações foram ignoradas por falta de câmbio."
                  ></p-tag>
                }
              </td>
              <td>{{ budget.alertThresholdPercent }}%</td>
              <td>
                <p-button
                  icon="pi pi-pencil"
                  severity="secondary"
                  [text]="true"
                  (onClick)="openEdit(budget)"
                  ariaLabel="Editar"
                ></p-button>
                <p-button
                  icon="pi pi-trash"
                  severity="danger"
                  [text]="true"
                  (onClick)="confirmArchive(budget)"
                  ariaLabel="Apagar"
                ></p-button>
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="8" class="text-center text-[var(--p-text-muted-color)]">
                Sem orçamentos para este mês. Cria o primeiro para começar a controlar gastos por categoria.
              </td>
            </tr>
          </ng-template>
        </p-table>
      </div>

      @if (dialogOpen()) {
        <app-budget-dialog
          [editingId]="editingId()"
          (saved)="onSaved()"
          (closed)="closeDialog()"
        ></app-budget-dialog>
      }

      <p-confirmDialog></p-confirmDialog>
      <p-toast></p-toast>
    </div>
  `,
})
export class BudgetsPage implements OnInit {
  private readonly api = inject(FinancialApiService);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly store = inject(FinancialStore);

  protected readonly budgets = signal<BudgetDto[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected periodPicker: Date = new Date();

  protected readonly currentYear = computed(() => this.periodPicker.getFullYear());
  protected readonly currentMonth = computed(() => this.periodPicker.getMonth() + 1);

  async ngOnInit(): Promise<void> {
    if (this.store.categories().length === 0) {
      await this.store.loadCategories();
    }
    await this.loadBudgets();
  }

  private async loadBudgets(): Promise<void> {
    try {
      const list = await this.api.listBudgets(
        this.periodPicker.getFullYear(),
        this.periodPicker.getMonth() + 1,
      );
      this.budgets.set(list);
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar orçamentos.',
      });
    }
  }

  protected categoryNameFor(categoryId: string): string {
    return this.store.categories().find((c) => c.id === categoryId)?.name ?? 'Categoria';
  }

  protected severityFor(budget: BudgetDto): 'success' | 'warn' | 'danger' {
    const p = budget.progress.percentUsed;
    if (p >= 100) return 'danger';
    if (p >= budget.alertThresholdPercent) return 'warn';
    return 'success';
  }

  protected rowClassFor(budget: BudgetDto): string {
    const sev = this.severityFor(budget);
    if (sev === 'danger') return 'bg-red-50 dark:bg-red-950/30';
    if (sev === 'warn') return 'bg-yellow-50 dark:bg-yellow-950/30';
    return '';
  }

  protected onPeriodChange(): void {
    void this.loadBudgets();
  }

  protected openCreate(): void {
    this.editingId.set(null);
    this.dialogOpen.set(true);
  }

  protected openEdit(budget: BudgetDto): void {
    this.editingId.set(budget.id);
    this.dialogOpen.set(true);
  }

  protected closeDialog(): void {
    this.dialogOpen.set(false);
    this.editingId.set(null);
  }

  protected onSaved(): void {
    this.closeDialog();
    void this.loadBudgets();
  }

  protected confirmArchive(budget: BudgetDto): void {
    const name = this.categoryNameFor(budget.categoryId);
    this.confirm.confirm({
      message: `Apagar o orçamento "${name}"?`,
      header: 'Apagar orçamento',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Apagar',
      rejectLabel: 'Cancelar',
      accept: () => void this.archive(budget.id),
    });
  }

  private async archive(id: string): Promise<void> {
    try {
      await this.api.archiveBudget(id);
      this.toast.add({ severity: 'success', summary: 'Orçamento apagado' });
      await this.loadBudgets();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível apagar o orçamento.',
      });
    }
  }
}
