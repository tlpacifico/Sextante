import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CardModule } from 'primeng/card';
import { AuthService } from '../../auth/auth.service';

/**
 * Placeholder do dashboard — existe apenas para o <c>authGuard</c> ter
 * destino na Phase 1b. Phase 2 substitui por widgets reais (saldo,
 * gráficos, últimas transações).
 */
@Component({
  selector: 'app-dashboard-placeholder',
  standalone: true,
  imports: [CardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="max-w-3xl mx-auto">
      <p-card header="Dashboard">
        <p class="text-base">
          Olá, <strong>{{ auth.userEmail() }}</strong>. A sessão está ativa.
        </p>
        <p class="mt-3 text-sm text-[var(--p-text-muted-color)]">
          <i class="pi pi-clock mr-1"></i>
          Phase 2 — saldo, gráficos e transações chegam aí.
        </p>
      </p-card>
    </div>
  `,
})
export class DashboardPlaceholderComponent {
  protected readonly auth = inject(AuthService);
}
