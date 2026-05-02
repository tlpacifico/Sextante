import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ProgressBarModule } from 'primeng/progressbar';
import { TagModule } from 'primeng/tag';
import { BudgetDto } from '../../../../core/api/financial.types';
import { MoneyPipe } from '../../../../core/format/money.pipe';

/**
 * Cartão reutilizável que exibe o progresso de um Budget.
 * Cor da barra varia por % consumido: < 80 verde, 80–99 amarelo,
 * ≥ 100 vermelho. Inclui badges para projeção e cálculo parcial.
 */
@Component({
  selector: 'app-budget-progress-card',
  standalone: true,
  imports: [CommonModule, ProgressBarModule, TagModule, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let b = budget();
    @let percent = displayPercent();
    @let severity = severityForPercent();
    <div
      class="border rounded-md p-4 flex flex-col gap-2 bg-[var(--p-surface-0)] dark:bg-[var(--p-surface-800)] border-[var(--p-surface-200)] dark:border-[var(--p-surface-700)]"
    >
      <div class="flex items-center justify-between gap-2">
        <span class="font-semibold truncate" data-testid="budget-card-name">
          {{ categoryName() ?? 'Categoria' }}
        </span>
        <p-tag
          [value]="percent.toFixed(0) + '%'"
          [severity]="severity"
          [rounded]="true"
        ></p-tag>
      </div>

      <p-progressbar
        [value]="Math.min(percent, 100)"
        [showValue]="false"
        [styleClass]="progressStyleClass()"
      ></p-progressbar>

      <div class="text-sm text-[var(--p-text-muted-color)]">
        {{ b.progress.spentAmount | money: b.progress.limitCurrency }}
        de
        {{ b.progress.limitAmount | money: b.progress.limitCurrency }}
      </div>

      <div class="flex items-center gap-2 flex-wrap">
        @if (b.progress.projectedAmount != null) {
          <p-tag
            [value]="'Projeção: ' + ((b.progress.projectedAmount) | money: b.progress.limitCurrency)"
            severity="secondary"
            [rounded]="true"
          ></p-tag>
        }
        @if (b.progress.hasIncompleteRates) {
          <p-tag
            value="Cálculo parcial"
            severity="warn"
            [rounded]="true"
            pTooltip="Algumas transações foram ignoradas por falta de câmbio."
          ></p-tag>
        }
      </div>
    </div>
  `,
})
export class BudgetProgressCardComponent {
  readonly budget = input.required<BudgetDto>();
  readonly categoryName = input<string | null>(null);

  protected readonly Math = Math;

  protected readonly displayPercent = computed(() =>
    this.budget().progress.percentUsed,
  );

  protected readonly severityForPercent = computed(() => {
    const p = this.displayPercent();
    if (p >= 100) return 'danger';
    if (p >= this.budget().alertThresholdPercent) return 'warn';
    return 'success';
  });

  protected readonly progressStyleClass = computed(() => {
    const p = this.displayPercent();
    if (p >= 100) return 'budget-progress-danger';
    if (p >= this.budget().alertThresholdPercent) return 'budget-progress-warning';
    return 'budget-progress-success';
  });
}
