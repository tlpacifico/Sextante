import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  NgZone,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../../../auth/auth.service';
import { FinancialApiService } from '../../../../core/api/financial-api.service';
import { BudgetAlertDto } from '../../../../core/api/financial.types';

const POLL_INTERVAL_MS = 60_000;

/**
 * Banner persistente acima do conteúdo principal. Faz polling a cada
 * 60s ao endpoint <c>/api/financial/budgets/alerts/active</c> e
 * permite acknowledge em batch. Visível apenas em rotas autenticadas.
 */
@Component({
  selector: 'app-budget-alerts-banner',
  standalone: true,
  imports: [CommonModule, ButtonModule, TagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.isAuthenticated() && alerts().length > 0) {
      <div
        class="px-4 py-2 border-b flex flex-wrap items-center justify-between gap-2"
        [class]="bannerClass()"
        data-testid="budget-alerts-banner"
      >
        <div class="flex items-center gap-2 min-w-0">
          <i class="pi pi-bell"></i>
          <span class="font-medium truncate">
            {{ summaryText() }}
          </span>
        </div>
        <div class="flex items-center gap-2">
          <p-button
            [label]="expanded() ? 'Esconder' : 'Ver'"
            severity="secondary"
            [text]="true"
            size="small"
            (onClick)="toggleExpanded()"
          ></p-button>
          <p-button
            label="Reconhecer"
            severity="secondary"
            size="small"
            (onClick)="acknowledgeAll()"
            [disabled]="acknowledging()"
            data-testid="budget-alerts-acknowledge"
          ></p-button>
        </div>
        @if (expanded()) {
          <ul class="w-full mt-2 flex flex-col gap-1 text-sm">
            @for (alert of alerts().slice(0, 5); track alert.id) {
              <li class="flex items-center gap-2">
                <p-tag
                  [value]="alert.threshold + '%'"
                  [severity]="alert.threshold >= 100 ? 'danger' : 'warn'"
                  [rounded]="true"
                ></p-tag>
                <span>
                  {{ alert.spentAtTriggerAmount | number:'1.2-2' }}
                  {{ alert.spentAtTriggerCurrency }}
                </span>
              </li>
            }
            @if (alerts().length > 5) {
              <li class="text-[var(--p-text-muted-color)]">
                +{{ alerts().length - 5 }} mais — abre /app/budgets
              </li>
            }
          </ul>
        }
      </div>
    }
  `,
})
export class BudgetAlertsBannerComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(FinancialApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly zone = inject(NgZone);

  protected readonly alerts = signal<BudgetAlertDto[]>([]);
  protected readonly expanded = signal(false);
  protected readonly acknowledging = signal(false);

  protected readonly hasCritical = computed(() =>
    this.alerts().some((a) => a.threshold >= 100),
  );

  protected readonly summaryText = computed(() => {
    const count = this.alerts().length;
    if (count === 0) return '';
    if (this.hasCritical()) {
      return count === 1
        ? '1 orçamento atingiu o limite'
        : `${count} orçamentos atingiram o limite`;
    }
    return count === 1
      ? '1 orçamento próximo do limite'
      : `${count} orçamentos próximos do limite`;
  });

  protected readonly bannerClass = computed(() =>
    this.hasCritical()
      ? 'bg-red-50 dark:bg-red-950/30 border-red-200 dark:border-red-900 text-red-900 dark:text-red-100'
      : 'bg-yellow-50 dark:bg-yellow-950/30 border-yellow-200 dark:border-yellow-900 text-yellow-900 dark:text-yellow-100',
  );

  ngOnInit(): void {
    void this.refresh();
    // Corre o setInterval fora do NgZone — caso contrário tasks longos
    // bloqueiam <c>fixture.whenStable()</c> nos Karma tests e impedem
    // server-side rendering / pré-render. O refresh chamado dentro do
    // callback re-entra no zone via <c>this.zone.run</c>.
    this.zone.runOutsideAngular(() => {
      const intervalId = setInterval(
        () => this.zone.run(() => void this.refresh()),
        POLL_INTERVAL_MS,
      );
      this.destroyRef.onDestroy(() => clearInterval(intervalId));
    });
  }

  protected toggleExpanded(): void {
    this.expanded.update((v) => !v);
  }

  protected async acknowledgeAll(): Promise<void> {
    if (this.acknowledging()) return;
    this.acknowledging.set(true);
    try {
      const current = this.alerts();
      await Promise.all(current.map((a) => this.api.acknowledgeBudgetAlert(a.id)));
      this.alerts.set([]);
      this.expanded.set(false);
    } catch {
      // Silencioso — próximo poll volta a mostrar se não acknowledged.
    } finally {
      this.acknowledging.set(false);
    }
  }

  private async refresh(): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.alerts.set([]);
      return;
    }
    try {
      const list = await this.api.listActiveBudgetAlerts();
      this.alerts.set(list);
    } catch {
      // Silencioso — não bloquear navegação por erro do banner.
    }
  }
}
