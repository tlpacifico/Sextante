import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { ProgressBarModule } from 'primeng/progressbar';
import { TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import { CreditCardViewDto, TransactionDto } from '../../../core/api/financial.types';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { FinancialStore } from '../state/financial.store';

/**
 * Phase 6.5 grupo 5 — vista do cartão de crédito: dívida, limite,
 * disponível, próximo pagamento, ciclo corrente (com movimentos) e extrato
 * anterior. Tudo calculado no backend a partir das definições do cartão.
 */
@Component({
  selector: 'app-credit-card-page',
  standalone: true,
  imports: [DatePipe, DecimalPipe, RouterLink, ButtonModule, ProgressBarModule, TableModule, ToastModule, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center gap-2 mb-4">
        <a routerLink="/app/accounts" class="p-button p-button-text p-button-secondary p-button-icon-only" aria-label="Voltar às contas">
          <i class="pi pi-arrow-left"></i>
        </a>
        <h1 class="text-xl md:text-2xl font-semibold">{{ accountName() }}</h1>
      </div>

      @if (view(); as v) {
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
          <section class="p-4 rounded-lg border border-[var(--p-content-border-color)]">
            <h2 class="text-sm font-medium text-[var(--p-text-muted-color)] mb-2">Dívida atual</h2>
            <p class="text-2xl font-semibold">{{ v.currentDebt | money }}</p>
            @if (v.settings && v.available) {
              <p-progressBar [value]="usagePercent()" [showValue]="false" styleClass="h-2 my-3" />
              <dl class="grid grid-cols-2 gap-2 text-sm">
                <dt class="text-[var(--p-text-muted-color)]">Limite</dt>
                <dd class="text-right">{{ v.settings.creditLimit | money }}</dd>
                <dt class="text-[var(--p-text-muted-color)]">Disponível</dt>
                <dd class="text-right" [class.text-red-600]="v.available.amount < 0">{{ v.available | money }}</dd>
              </dl>
            }
          </section>

          @if (v.settings && v.nextPaymentAmount && v.nextPaymentDueDate) {
            <section class="p-4 rounded-lg border border-[var(--p-content-border-color)]">
              <h2 class="text-sm font-medium text-[var(--p-text-muted-color)] mb-2">Próximo pagamento</h2>
              @if (v.nextPaymentAmount.amount > 0) {
                <p class="text-2xl font-semibold">{{ v.nextPaymentAmount | money }}</p>
                <p class="text-sm mt-1">
                  até {{ v.nextPaymentDueDate | date: 'dd/MM/yyyy' }}
                  @if (paymentAccountName(); as payer) {
                    · a partir de {{ payer }}
                  }
                </p>
              } @else {
                <p class="text-lg font-medium">Extrato pago</p>
                <p class="text-sm mt-1">Extrato com pagamento a {{ v.nextPaymentDueDate | date: 'dd/MM/yyyy' }}.</p>
              }
            </section>
          }
        </div>

        @if (!v.settings) {
          <p class="p-4 rounded-lg border border-[var(--p-content-border-color)] text-sm">
            Configure o limite e os dias de fecho e pagamento na
            <a routerLink="/app/accounts" class="underline">página de contas</a>
            para ver o ciclo, o extrato e o próximo pagamento.
          </p>
        }

        @if (v.currentCycle; as cycle) {
          <section class="p-4 rounded-lg border border-[var(--p-content-border-color)] mb-4">
            <h2 class="font-medium mb-1">Ciclo corrente</h2>
            <p class="text-sm text-[var(--p-text-muted-color)] mb-3">
              {{ cycle.start | date: 'dd/MM/yyyy' }} – {{ cycle.end | date: 'dd/MM/yyyy' }}
            </p>
            <dl class="grid grid-cols-2 gap-2 text-sm mb-3 max-w-sm">
              <dt class="text-[var(--p-text-muted-color)]">Gastos</dt>
              <dd class="text-right">{{ cycle.spent | money }}</dd>
              <dt class="text-[var(--p-text-muted-color)]">Pagamentos recebidos</dt>
              <dd class="text-right">{{ cycle.paymentsReceived | money }}</dd>
            </dl>
            <div class="overflow-x-auto">
              <p-table [value]="movements()" styleClass="p-datatable-sm" [tableStyle]="{ 'min-width': '28rem' }">
                <ng-template pTemplate="header">
                  <tr>
                    <th>Data</th>
                    <th>Descrição</th>
                    <th class="text-right">Valor</th>
                  </tr>
                </ng-template>
                <ng-template pTemplate="body" let-tx>
                  <tr>
                    <td>{{ tx.occurredAt | date: 'dd/MM/yyyy' }}</td>
                    <td class="truncate max-w-[16rem]">{{ tx.description || kindLabel(tx) }}</td>
                    <td class="text-right" [class]="tx.direction === 'Inflow' ? 'text-success' : 'text-danger'">
                      {{ tx.direction === 'Inflow' ? '+' : '−' }}{{ tx.amount.amount | number: '1.2-2' }} {{ tx.amount.currency }}
                    </td>
                  </tr>
                </ng-template>
                <ng-template pTemplate="emptymessage">
                  <tr>
                    <td colspan="3" class="text-center text-[var(--p-text-muted-color)]">Sem movimentos neste ciclo.</td>
                  </tr>
                </ng-template>
              </p-table>
            </div>
          </section>
        }

        @if (v.previousCycle; as previous) {
          <section class="p-4 rounded-lg border border-[var(--p-content-border-color)]">
            <h2 class="font-medium mb-1">Extrato anterior</h2>
            <p class="text-sm text-[var(--p-text-muted-color)] mb-3">
              {{ previous.start | date: 'dd/MM/yyyy' }} – {{ previous.end | date: 'dd/MM/yyyy' }}
            </p>
            <dl class="grid grid-cols-2 gap-2 text-sm max-w-sm">
              <dt class="text-[var(--p-text-muted-color)]">Dívida no fecho</dt>
              <dd class="text-right">{{ v.previousClosingDebt | money }}</dd>
              <dt class="text-[var(--p-text-muted-color)]">Gastos</dt>
              <dd class="text-right">{{ previous.spent | money }}</dd>
              <dt class="text-[var(--p-text-muted-color)]">Pagamento até</dt>
              <dd class="text-right">{{ previous.paymentDueDate | date: 'dd/MM/yyyy' }}</dd>
            </dl>
          </section>
        }
      } @else if (loading()) {
        <p class="text-sm text-[var(--p-text-muted-color)]">A carregar…</p>
      }

      <p-toast></p-toast>
    </div>
  `,
})
export class CreditCardPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(FinancialApiService);
  private readonly toast = inject(MessageService);
  protected readonly store = inject(FinancialStore);

  private readonly accountId = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly loading = signal(true);
  protected readonly view = signal<CreditCardViewDto | null>(null);
  protected readonly movements = signal<TransactionDto[]>([]);

  protected readonly accountName = computed(
    () => this.store.accounts().find((a) => a.id === this.accountId)?.name ?? 'Cartão de crédito',
  );

  protected readonly paymentAccountName = computed(() => {
    const id = this.view()?.paymentAccountId;
    return id ? (this.store.accounts().find((a) => a.id === id)?.name ?? null) : null;
  });

  protected readonly usagePercent = computed(() => {
    const v = this.view();
    const limit = v?.settings?.creditLimit.amount ?? 0;
    if (!v || limit <= 0) {
      return 0;
    }
    return Math.min(100, Math.round((v.currentDebt.amount / limit) * 100));
  });

  async ngOnInit(): Promise<void> {
    try {
      const [view] = await Promise.all([this.api.getCreditCardView(this.accountId), this.store.loadAccounts()]);
      this.view.set(view);
      if (view.currentCycle) {
        // Corte UTC por data, o mesmo do backend (Phase 6.5 grupo 5, R4).
        const page = await this.api.listTransactions({
          accountIds: [this.accountId],
          dateFrom: `${view.currentCycle.start}T00:00:00Z`,
          dateTo: `${view.currentCycle.end}T23:59:59Z`,
          pageSize: 100,
        });
        this.movements.set(page.items);
      }
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível carregar o cartão.' });
    } finally {
      this.loading.set(false);
    }
  }

  protected kindLabel(tx: TransactionDto): string {
    switch (tx.kind) {
      case 'Transfer':
        return 'Transferência';
      case 'Adjustment':
        return 'Acerto';
      default:
        return '—';
    }
  }
}
