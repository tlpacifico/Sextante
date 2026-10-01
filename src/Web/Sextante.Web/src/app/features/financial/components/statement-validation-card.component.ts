import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { StatementFormat, StatementSummary } from '../../../core/api/financial.types';

const FORMAT_LABELS: Record<StatementFormat, string> = {
  ActivoBankAccountXlsx: 'Conta à ordem ActivoBank (XLSX)',
  ActivoBankCardPdf: 'Cartão de crédito ActivoBank (PDF)',
  CoverflexJson: 'Coverflex (JSON)',
};

/**
 * Phase 6.6 (D9) — o que o conversor do backend garantiu sobre o extrato:
 * formato, período, saldos e as verificações com o esperado/calculado. Só
 * aparece quando o upload foi um XLSX, PDF ou JSON.
 */
@Component({
  selector: 'sxt-statement-validation-card',
  standalone: true,
  imports: [MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      class="mb-4 rounded-lg border border-[var(--p-surface-300)] dark:border-[var(--p-surface-700)] p-4"
      aria-label="Validação do extrato"
      data-testid="statement-validation-card"
    >
      <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 mb-3">
        <h3 class="text-base font-medium m-0">
          <i class="pi pi-verified text-green-500 mr-2" aria-hidden="true"></i>Extrato validado
        </h3>
        <span class="text-sm text-[var(--p-text-muted-color)]">{{ formatLabel() }}</span>
      </div>

      <dl class="grid grid-cols-1 sm:grid-cols-3 gap-3 text-sm m-0 mb-3">
        <div>
          <dt class="text-[var(--p-text-muted-color)]">Período</dt>
          <dd class="m-0 font-medium">{{ period() }}</dd>
        </div>
        <div>
          <dt class="text-[var(--p-text-muted-color)]">Saldo antes</dt>
          <dd class="m-0 font-medium">{{ statement().balanceBefore | money: statement().currency }}</dd>
        </div>
        <div>
          <dt class="text-[var(--p-text-muted-color)]">Saldo depois</dt>
          <dd class="m-0 font-medium">{{ statement().balanceAfter | money: statement().currency }}</dd>
        </div>
      </dl>

      <ul class="list-none p-0 m-0 flex flex-col gap-2 text-sm">
        @for (check of statement().checks; track check.name) {
          <li class="flex items-start gap-2" [attr.data-passed]="check.passed">
            <i
              class="pi mt-1"
              [class.pi-check-circle]="check.passed"
              [class.text-green-500]="check.passed"
              [class.pi-times-circle]="!check.passed"
              [class.text-red-500]="!check.passed"
              aria-hidden="true"
            ></i>
            <span class="min-w-0">
              <span class="font-medium">{{ check.name }}</span>
              <span class="block text-[var(--p-text-muted-color)] break-words">
                esperado {{ check.expected }} · calculado {{ check.actual }}
              </span>
            </span>
          </li>
        }
      </ul>

      @if (statement().rowsTrimmed > 0) {
        <p class="text-sm mt-3 mb-0" data-testid="trimmed-note">
          <i class="pi pi-info-circle mr-1" aria-hidden="true"></i>
          {{ statement().rowsTrimmed }}
          {{ statement().rowsTrimmed === 1 ? 'linha cortada' : 'linhas cortadas' }} por já estarem importadas.
        </p>
      }
      @if (statement().pendingIgnored > 0) {
        <p class="text-sm mt-2 mb-0" data-testid="pending-note">
          <i class="pi pi-clock mr-1" aria-hidden="true"></i>
          {{ statement().pendingIgnored }}
          {{ statement().pendingIgnored === 1 ? 'movimento pendente não importado' : 'movimentos pendentes não importados' }}
          (entram quando forem confirmados).
        </p>
      }
    </section>
  `,
})
export class StatementValidationCardComponent {
  readonly statement = input.required<StatementSummary>();

  protected readonly formatLabel = computed(() => FORMAT_LABELS[this.statement().format] ?? this.statement().format);

  protected readonly period = computed(() => {
    const { periodStart, periodEnd } = this.statement();
    if (!periodStart || !periodEnd) return '—';
    return `${formatDate(periodStart)} a ${formatDate(periodEnd)}`;
  });
}

/** `yyyy-MM-dd` → `dd/MM/yyyy` sem passar por `Date` (evita deslocamentos de fuso). */
function formatDate(iso: string): string {
  const [year, month, day] = iso.split('-');
  return `${day}/${month}/${year}`;
}
