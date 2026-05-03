import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MenuItem, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DrawerModule } from 'primeng/drawer';
import { MenubarModule } from 'primeng/menubar';
import { MenuModule } from 'primeng/menu';
import { ToastModule } from 'primeng/toast';
import { AuthService } from '../auth/auth.service';
import { ThemeService } from '../core/theme.service';
import { BudgetAlertsBannerComponent } from '../features/financial/pages/components/budget-alerts-banner.component';

/**
 * Shell autenticada — topbar (PrimeNG <c>p-menubar</c>) com o nome do
 * tenant + menu do utilizador (toggle dark mode + terminar sessão),
 * sidebar placeholder ("Phase 2 chega aí"), <c>p-toast</c> outlet, e o
 * <c>&lt;router-outlet /&gt;</c> para rotas filhas.
 */
@Component({
  selector: 'app-app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    MenubarModule,
    MenuModule,
    ButtonModule,
    DrawerModule,
    ToastModule,
    BudgetAlertsBannerComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <div class="min-h-screen flex flex-col bg-[var(--p-surface-50)] dark:bg-[var(--p-surface-950)]">
      <p-menubar [model]="menuItems()" class="border-b border-[var(--p-surface-200)] dark:border-[var(--p-surface-800)]">
        <ng-template pTemplate="start">
          <button
            type="button"
            class="mr-2 inline-flex items-center justify-center w-9 h-9 rounded-lg
                   hover:bg-[var(--p-surface-100)] dark:hover:bg-[var(--p-surface-800)]
                   text-[var(--p-text-color)]"
            aria-label="Menu lateral"
            (click)="sidebarVisible.set(true)"
          >
            <i class="pi pi-bars"></i>
          </button>
          <span class="font-semibold text-lg text-[var(--p-text-color)]">Sextante</span>
          @if (tenantLabel(); as label) {
            <span
              class="ml-3 px-2 py-0.5 text-xs rounded-full
                     bg-[var(--p-primary-100)] text-[var(--p-primary-700)]
                     dark:bg-[var(--p-primary-900)] dark:text-[var(--p-primary-200)]"
            >
              {{ label }}
            </span>
          }
        </ng-template>
        <ng-template pTemplate="end">
          <button
            type="button"
            class="inline-flex items-center justify-center w-9 h-9 rounded-lg
                   hover:bg-[var(--p-surface-100)] dark:hover:bg-[var(--p-surface-800)]
                   text-[var(--p-text-color)]"
            [attr.aria-label]="theme.themeLabel()"
            (click)="theme.toggle()"
          >
            <i class="pi" [class.pi-moon]="!theme.isDark()" [class.pi-sun]="theme.isDark()"></i>
          </button>
          <button
            type="button"
            class="ml-1 inline-flex items-center gap-2 px-3 h-9 rounded-lg
                   hover:bg-[var(--p-surface-100)] dark:hover:bg-[var(--p-surface-800)]
                   text-[var(--p-text-color)]"
            aria-label="Menu do utilizador"
            (click)="userMenu.toggle($event)"
          >
            <i class="pi pi-user"></i>
            <span class="hidden md:inline text-sm">{{ auth.userEmail() }}</span>
          </button>
          <p-menu #userMenu [model]="userMenuItems()" [popup]="true"></p-menu>
        </ng-template>
      </p-menubar>

      <p-drawer
        [(visible)]="sidebarVisibleModel"
        position="left"
        [showCloseIcon]="true"
      >
        <ng-template pTemplate="header">
          <span class="font-semibold">Navegação</span>
        </ng-template>
        <ul class="flex flex-col gap-1 p-3">
          @for (item of menuItems(); track item.label) {
            <li>
              <a
                [routerLink]="item.routerLink"
                (click)="sidebarVisible.set(false)"
                class="flex items-center gap-2 px-3 py-2 rounded-md
                       text-[var(--p-text-color)]
                       hover:bg-[var(--p-surface-100)]
                       dark:hover:bg-[var(--p-surface-800)]"
              >
                <i [class]="item.icon"></i>
                <span>{{ item.label }}</span>
              </a>
            </li>
          }
        </ul>
      </p-drawer>

      <app-budget-alerts-banner></app-budget-alerts-banner>

      <section class="flex-1 px-4 py-6 md:px-8 md:py-8">
        <router-outlet></router-outlet>
      </section>

      <p-toast position="top-right"></p-toast>
    </div>
  `,
})
export class AppShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  private readonly router = inject(Router);

  protected readonly sidebarVisible = signal(false);

  protected get sidebarVisibleModel(): boolean {
    return this.sidebarVisible();
  }
  protected set sidebarVisibleModel(value: boolean) {
    this.sidebarVisible.set(value);
  }

  protected readonly tenantLabel = computed(() => {
    const name = this.auth.tenantName();
    if (!name) return null;
    const role = this.auth.tenantRole();
    return role ? `${name} · ${this.translateRole(role)}` : name;
  });

  protected readonly menuItems = computed<MenuItem[]>(() => {
    const items: MenuItem[] = [
      {
        label: 'Dashboard',
        icon: 'pi pi-home',
        routerLink: ['/app/dashboard'],
      },
      {
        label: 'Transações',
        icon: 'pi pi-list',
        routerLink: ['/app/transactions'],
      },
      {
        label: 'Contas',
        icon: 'pi pi-wallet',
        routerLink: ['/app/accounts'],
      },
      {
        label: 'Categorias',
        icon: 'pi pi-tag',
        routerLink: ['/app/categories'],
      },
      {
        label: 'Recorrentes',
        icon: 'pi pi-sync',
        routerLink: ['/app/recurrings'],
      },
      {
        label: 'Orçamentos',
        icon: 'pi pi-chart-bar',
        routerLink: ['/app/budgets'],
      },
      {
        label: 'Regras',
        icon: 'pi pi-filter',
        routerLink: ['/app/categorization-rules'],
      },
      {
        label: 'Perfis de importação',
        icon: 'pi pi-file-import',
        routerLink: ['/app/import-profiles'],
      },
      {
        label: 'Importações',
        icon: 'pi pi-upload',
        routerLink: ['/app/imports'],
      },
      {
        label: 'Definições',
        icon: 'pi pi-cog',
        routerLink: ['/app/settings/general'],
      },
    ];

    // Admin entries: o backend valida o role SystemAdmin no endpoint
    // (defesa em profundidade); o frontend mostra-os a Owners por
    // heurística (UX) — não é boundary de segurança.
    if (this.auth.tenantRole() === 'Owner') {
      items.push(
        {
          label: 'Moedas',
          icon: 'pi pi-globe',
          routerLink: ['/app/admin/currencies'],
        },
        {
          label: 'Taxas de câmbio',
          icon: 'pi pi-chart-line',
          routerLink: ['/app/admin/exchange-rates'],
        },
      );
    }

    return items;
  });

  protected readonly userMenuItems = computed<MenuItem[]>(() => [
    {
      label: 'Definições',
      icon: 'pi pi-cog',
      routerLink: ['/app/settings/general'],
    },
    {
      label: this.theme.themeLabel(),
      icon: this.theme.isDark() ? 'pi pi-sun' : 'pi pi-moon',
      command: () => this.theme.toggle(),
    },
    { separator: true },
    {
      label: 'Terminar sessão',
      icon: 'pi pi-sign-out',
      command: () => void this.handleLogout(),
    },
  ]);

  private async handleLogout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }

  private translateRole(role: 'Owner' | 'Member' | 'ReadOnly'): string {
    switch (role) {
      case 'Owner':
        return 'Proprietário';
      case 'Member':
        return 'Membro';
      case 'ReadOnly':
        return 'Leitura';
    }
  }
}
