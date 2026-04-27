import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { CardModule } from 'primeng/card';
import { ChartModule } from 'primeng/chart';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { ConfirmationService, MessageService } from 'primeng/api';
import { FinancialApiService } from '../../core/api/financial-api.service';
import {
  CategoryDto,
  CreateTransactionRequest,
  TransactionDto,
} from '../../core/api/financial.types';
import { MoneyPipe } from '../../core/format/money.pipe';
import { FinancialStore, ChartKind } from '../financial/state/financial.store';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    DatePickerModule,
    CardModule,
    ChartModule,
    DialogModule,
    SelectModule,
    InputNumberModule,
    InputTextModule,
    MultiSelectModule,
    SelectButtonModule,
    TableModule,
    TagModule,
    ToastModule,
    ConfirmDialogModule,
    MoneyPipe,
    DatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService, ConfirmationService],
  template: `
    <div class="max-w-6xl mx-auto flex flex-col gap-6">
      <header class="flex items-center justify-between">
        <h1 class="text-2xl font-semibold">Dashboard</h1>
        <p-button
          label="Nova transação"
          icon="pi pi-plus"
          (onClick)="openCreate()"
        ></p-button>
      </header>

      <p-card>
        <form [formGroup]="filterForm" class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div class="flex flex-col gap-1">
            <label for="period">Período</label>
            <p-datepicker
              inputId="period"
              selectionMode="range"
              [showIcon]="true"
              dateFormat="dd/mm/yy"
              formControlName="period"
              styleClass="w-full"
            ></p-datepicker>
          </div>
          <div class="flex flex-col gap-1">
            <label for="categories">Categorias</label>
            <p-multiSelect
              inputId="categories"
              [options]="store.categories()"
              optionLabel="name"
              optionValue="id"
              formControlName="categoryIds"
              placeholder="Todas"
              styleClass="w-full"
            ></p-multiSelect>
          </div>
          <div class="flex flex-col gap-1">
            <label for="accounts">Contas</label>
            <p-multiSelect
              inputId="accounts"
              [options]="store.accounts()"
              optionLabel="name"
              optionValue="id"
              formControlName="accountIds"
              placeholder="Todas"
              styleClass="w-full"
            ></p-multiSelect>
          </div>
        </form>
      </p-card>

      <section class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <p-card>
          <div class="flex flex-col gap-1">
            <span class="text-sm text-[var(--p-text-muted-color)]">Entrada</span>
            <span class="text-2xl font-semibold text-emerald-600 dark:text-emerald-400">
              {{ store.summary().income | money }}
            </span>
          </div>
        </p-card>
        <p-card>
          <div class="flex flex-col gap-1">
            <span class="text-sm text-[var(--p-text-muted-color)]">Saída</span>
            <span class="text-2xl font-semibold text-red-600 dark:text-red-400">
              {{ store.summary().expense | money }}
            </span>
          </div>
        </p-card>
        <p-card>
          <div class="flex flex-col gap-1">
            <span class="text-sm text-[var(--p-text-muted-color)]">Líquido</span>
            <span class="text-2xl font-semibold">
              {{ store.summary().net | money }}
            </span>
          </div>
        </p-card>
      </section>

      <p-card>
        <div class="flex items-center justify-between mb-4">
          <h2 class="text-lg font-semibold">Por categoria</h2>
          <p-selectButton
            [options]="chartOptions"
            optionLabel="label"
            optionValue="value"
            [ngModel]="store.chartKind()"
            (ngModelChange)="onChartKindChange($event)"
            [allowEmpty]="false"
          ></p-selectButton>
        </div>
        @if (chartData(); as data) {
          @if (data.labels.length) {
            <p-chart type="doughnut" [data]="data" [options]="chartConfig"></p-chart>
          } @else {
            <p class="text-center text-[var(--p-text-muted-color)] py-8">
              Sem dados para o filtro atual.
            </p>
          }
        }
      </p-card>

      <p-card>
        <h2 class="text-lg font-semibold mb-4">Transações</h2>
        <p-table
          [value]="store.transactions()"
          [tableStyle]="{ 'min-width': '50rem' }"
          styleClass="p-datatable-sm"
          dataKey="id"
        >
          <ng-template pTemplate="header">
            <tr>
              <th style="width: 9rem">Data</th>
              <th style="width: 12rem">Conta</th>
              <th style="width: 14rem">Categoria</th>
              <th>Descrição</th>
              <th style="width: 9rem; text-align: right">Valor</th>
              <th style="width: 5rem"></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-transaction>
            <tr>
              <td>{{ transaction.occurredAt | date: 'dd/MM/yyyy' }}</td>
              <td>{{ accountName(transaction.accountId) }}</td>
              <td>
                @if (categoryFor(transaction.categoryId); as cat) {
                  <span class="inline-flex items-center gap-2">
                    <span
                      class="inline-flex items-center justify-center w-6 h-6 rounded-full"
                      [style.background-color]="cat.colorHex + '33'"
                    >
                      <i class="pi {{ cat.iconName }}" [style.color]="cat.colorHex"></i>
                    </span>
                    <span>{{ cat.name }}</span>
                  </span>
                }
              </td>
              <td>{{ transaction.description ?? '—' }}</td>
              <td
                class="text-right font-medium"
                [class.text-emerald-600]="kindFor(transaction.categoryId) === 'Income'"
                [class.dark:text-emerald-400]="kindFor(transaction.categoryId) === 'Income'"
                [class.text-red-600]="kindFor(transaction.categoryId) === 'Expense'"
                [class.dark:text-red-400]="kindFor(transaction.categoryId) === 'Expense'"
              >
                {{ transaction.amount | money }}
              </td>
              <td>
                <p-button
                  icon="pi pi-trash"
                  severity="danger"
                  [text]="true"
                  (onClick)="confirmArchive(transaction)"
                  ariaLabel="Arquivar"
                ></p-button>
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="6" class="text-center text-[var(--p-text-muted-color)]">
                Sem transações para o filtro atual.
              </td>
            </tr>
          </ng-template>
        </p-table>
        @if (store.hasMoreTransactions()) {
          <div class="flex justify-center mt-4">
            <p-button
              label="Carregar mais"
              [text]="true"
              (onClick)="loadMore()"
            ></p-button>
          </div>
        }
      </p-card>

      <p-dialog
        [(visible)]="dialogOpen"
        [modal]="true"
        [closable]="true"
        header="Nova transação"
        [style]="{ width: '32rem' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
          <div class="flex flex-col gap-1">
            <label for="t-account">Conta</label>
            <p-select
              inputId="t-account"
              [options]="store.accounts()"
              optionLabel="name"
              optionValue="id"
              formControlName="accountId"
              styleClass="w-full"
            ></p-select>
          </div>
          <div class="flex flex-col gap-1">
            <label for="t-category">Categoria</label>
            <p-select
              inputId="t-category"
              [options]="store.categories()"
              optionLabel="name"
              optionValue="id"
              formControlName="categoryId"
              styleClass="w-full"
            >
              <ng-template let-option pTemplate="item">
                <span class="flex items-center gap-2">
                  <i class="pi {{ option.iconName }}" [style.color]="option.colorHex"></i>
                  <span>{{ option.name }}</span>
                </span>
              </ng-template>
            </p-select>
          </div>
          <div class="flex flex-col gap-1">
            <label for="t-occurred">Data</label>
            <p-datepicker
              inputId="t-occurred"
              dateFormat="dd/mm/yy"
              formControlName="occurredAt"
              [showIcon]="true"
              styleClass="w-full"
            ></p-datepicker>
          </div>
          <div class="flex flex-col gap-1">
            <label for="t-amount">Valor</label>
            <p-inputNumber
              inputId="t-amount"
              mode="currency"
              currency="EUR"
              locale="pt-PT"
              [min]="0.01"
              formControlName="amount"
            ></p-inputNumber>
          </div>
          <div class="flex flex-col gap-1">
            <label for="t-description">Descrição</label>
            <input
              id="t-description"
              pInputText
              formControlName="description"
              maxlength="500"
            />
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

      <p-confirmDialog></p-confirmDialog>
      <p-toast></p-toast>
    </div>
  `,
})
export class DashboardPage implements OnInit {
  protected readonly store = inject(FinancialStore);
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly chartOptions = [
    { value: 'Expense', label: 'Despesas' },
    { value: 'Income', label: 'Receitas' },
  ];

  protected readonly chartConfig = {
    cutout: '60%',
    plugins: {
      legend: {
        position: 'right',
      },
    },
  };

  protected readonly chartData = computed(() => {
    const rows = this.store.byCategory();
    return {
      labels: rows.map((r) => r.categoryName),
      datasets: [
        {
          data: rows.map((r) => r.total.amount),
          backgroundColor: rows.map((r) => r.colorHex),
          hoverBackgroundColor: rows.map((r) => r.colorHex),
        },
      ],
    };
  });

  protected readonly dialogOpenSignal = signal(false);
  protected readonly submitting = signal(false);

  protected get dialogOpen(): boolean {
    return this.dialogOpenSignal();
  }
  protected set dialogOpen(value: boolean) {
    this.dialogOpenSignal.set(value);
  }

  protected readonly filterForm = this.fb.nonNullable.group({
    period: this.fb.control<Date[] | null>(null),
    categoryIds: this.fb.control<string[]>([]),
    accountIds: this.fb.control<string[]>([]),
  });

  protected readonly form = this.fb.nonNullable.group({
    accountId: ['', Validators.required],
    categoryId: ['', Validators.required],
    occurredAt: [new Date() as Date | null, Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    description: this.fb.control<string | null>(null),
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([
      this.store.loadAccounts(),
      this.store.loadCategories(),
    ]);

    this.filterForm.valueChanges.subscribe((value) => {
      const period = value.period;
      const dateFrom = period?.[0] ? period[0].toISOString() : null;
      const dateTo = period?.[1] ? period[1].toISOString() : null;
      this.store.setFilter({
        dateFrom,
        dateTo,
        categoryIds: value.categoryIds && value.categoryIds.length ? value.categoryIds : null,
        accountIds: value.accountIds && value.accountIds.length ? value.accountIds : null,
        pageSize: 50,
      });
      void this.store.refreshDashboard();
    });

    await this.store.refreshDashboard();
  }

  protected accountName(id: string): string {
    return this.store.accounts().find((a) => a.id === id)?.name ?? '—';
  }

  protected categoryFor(id: string): CategoryDto | null {
    return this.store.categories().find((c) => c.id === id) ?? null;
  }

  protected kindFor(id: string): 'Income' | 'Expense' | null {
    return this.categoryFor(id)?.kind ?? null;
  }

  protected onChartKindChange(value: ChartKind): void {
    this.store.setChartKind(value);
    void this.store.loadByCategory();
  }

  protected openCreate(): void {
    this.form.reset({
      accountId: '',
      categoryId: '',
      occurredAt: new Date(),
      amount: 0,
      description: null,
    });
    this.dialogOpenSignal.set(true);
  }

  protected close(): void {
    this.dialogOpenSignal.set(false);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      return;
    }
    this.submitting.set(true);
    try {
      const value = this.form.getRawValue();
      const occurredAt = (value.occurredAt as Date).toISOString();
      const request: CreateTransactionRequest = {
        accountId: value.accountId,
        categoryId: value.categoryId,
        occurredAt,
        amount: value.amount,
        description: value.description,
      };
      await this.api.createTransaction(request);
      this.toast.add({ severity: 'success', summary: 'Transação criada' });
      this.close();
      await this.store.refreshDashboard();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível criar a transação.',
      });
    } finally {
      this.submitting.set(false);
    }
  }

  protected confirmArchive(transaction: TransactionDto): void {
    this.confirm.confirm({
      message: 'Arquivar esta transação?',
      header: 'Arquivar transação',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Arquivar',
      rejectLabel: 'Cancelar',
      accept: () => void this.archive(transaction.id),
    });
  }

  protected async loadMore(): Promise<void> {
    try {
      await this.store.loadTransactions(false);
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar mais transações.',
      });
    }
  }

  private async archive(id: string): Promise<void> {
    try {
      await this.api.archiveTransaction(id);
      this.toast.add({ severity: 'success', summary: 'Transação arquivada' });
      await this.store.refreshDashboard();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível arquivar a transação.',
      });
    }
  }
}
