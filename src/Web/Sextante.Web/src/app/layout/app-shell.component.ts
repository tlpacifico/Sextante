import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import {
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { filter } from 'rxjs/operators';
import { MenuItem, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DrawerModule } from 'primeng/drawer';
import { MenuModule } from 'primeng/menu';
import { ToastModule } from 'primeng/toast';
import { AuthService } from '../auth/auth.service';
import { ThemeService } from '../core/theme.service';
import { BudgetAlertsBannerComponent } from '../features/financial/pages/components/budget-alerts-banner.component';

interface NavItem {
  readonly label: string;
  readonly icon: string;
  readonly routerLink: string;
}

interface NavGroup {
  readonly id: string;
  readonly label: string;
  readonly items: readonly NavItem[];
}

/**
 * Shell autenticada — direcção estética "Cartografia do capital":
 * sidebar persistente em desktop com navegação agrupada (Visão geral /
 * Movimento / Estrutura / Administração), topbar com título de secção
 * em serif editorial, drawer apenas em mobile. Active link assinalado
 * com bordo latão à esquerda + peso medium, sem preenchimento.
 */
@Component({
  selector: 'app-app-shell',
  standalone: true,
  imports: [
    NgTemplateOutlet,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MenuModule,
    ButtonModule,
    DrawerModule,
    ToastModule,
    BudgetAlertsBannerComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  styles: [
    `
      :host {
        display: block;
      }

      /*
       * Sidebar nav link. A barra latão à esquerda (--rail) cresce de 0
       * → 3px no estado activo para criar uma ancoragem visual subtil.
       * Inactive: bordo invisível + texto suave. Hover: bg paper-100.
       */
      .nav-link {
        position: relative;
        display: flex;
        align-items: center;
        gap: 0.75rem;
        height: 2.25rem;
        padding: 0 0.75rem 0 1.25rem;
        margin: 1px 0;
        font-size: 0.875rem;
        font-weight: 400;
        color: var(--color-neutral-600);
        border-radius: 0;
        text-decoration: none;
        transition: background-color 120ms ease, color 120ms ease;
      }

      .nav-link::before {
        content: '';
        position: absolute;
        left: 0;
        top: 0.4rem;
        bottom: 0.4rem;
        width: 0;
        background: var(--color-primary-500);
        border-radius: 0 2px 2px 0;
        transition: width 160ms ease;
      }

      .nav-link:hover {
        background-color: var(--color-neutral-100);
        color: var(--color-neutral-800);
      }

      .nav-link.is-active {
        color: var(--color-neutral-900);
        font-weight: 500;
        background-color: var(--color-neutral-100);
      }

      .nav-link.is-active::before {
        width: 3px;
      }

      .nav-link i {
        width: 1rem;
        font-size: 0.875rem;
        color: var(--color-neutral-500);
        transition: color 120ms ease;
      }

      .nav-link.is-active i {
        color: var(--color-primary-600);
      }

      .dark .nav-link.is-active i {
        color: var(--color-primary-500);
      }

      /*
       * Sextant glyph — pequeno arco + crosshair com origem em (16,28).
       * Usado ao lado do wordmark. Cor latão. currentColor permite que
       * o componente herde da cor de texto pai se necessário.
       */
      .sextant {
        flex-shrink: 0;
        color: var(--color-primary-500);
      }

      /*
       * Wordmark — Sextante em Fraunces semibold com optical sizing
       * agressivo + soft alto. Acento sobre o "e" final é deixado ao
       * char nativo (ã não tem) — o nome PT-PT é Sextante (sem til).
       */
      .wordmark {
        font-family: var(--font-display);
        font-variation-settings: 'opsz' 144, 'SOFT' 100;
        font-weight: 600;
        font-style: italic;
        font-size: 1.5rem;
        letter-spacing: -0.02em;
        line-height: 1;
        color: var(--color-neutral-900);
      }

      .dark .wordmark {
        color: var(--color-neutral-800);
      }

      /*
       * Section title no topbar — Fraunces, romano (não itálico), com
       * optical sizing menor para corpo legível em tamanhos médios.
       */
      .section-title {
        font-family: var(--font-display);
        font-variation-settings: 'opsz' 48, 'SOFT' 40;
        font-weight: 500;
        font-size: 1.25rem;
        letter-spacing: -0.01em;
        color: var(--color-neutral-900);
      }

      .dark .section-title {
        color: var(--color-neutral-800);
      }

      /*
       * Avatar circle com iniciais. Borda hairline + leve gradient
       * latão→cream. Em dark mode inverte.
       */
      .avatar {
        width: 2rem;
        height: 2rem;
        border-radius: 999px;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        font-family: var(--font-display);
        font-weight: 600;
        font-size: 0.75rem;
        letter-spacing: 0.04em;
        color: var(--color-primary-800);
        background: linear-gradient(
          135deg,
          var(--color-primary-100) 0%,
          var(--color-primary-200) 100%
        );
        box-shadow: inset 0 0 0 1px var(--color-primary-300);
      }

      .dark .avatar {
        color: var(--color-primary-100);
        background: linear-gradient(
          135deg,
          var(--color-primary-200) 0%,
          var(--color-primary-300) 100%
        );
        box-shadow: inset 0 0 0 1px var(--color-primary-400);
      }

      /*
       * Topbar fica acima do conteúdo mas mantém a textura de papel
       * visível por baixo via backdrop-filter. Hairline em baixo.
       */
      .topbar {
        position: sticky;
        top: 0;
        z-index: 10;
        height: 3.5rem;
        backdrop-filter: saturate(180%) blur(8px);
        -webkit-backdrop-filter: saturate(180%) blur(8px);
        background-color: color-mix(in oklab, var(--color-neutral-0) 86%, transparent);
        border-bottom: var(--hairline);
      }

      .icon-btn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 2.25rem;
        height: 2.25rem;
        border-radius: 0.375rem;
        color: var(--color-neutral-700);
        transition: background-color 120ms ease, color 120ms ease;
      }

      .icon-btn:hover {
        background-color: var(--color-neutral-100);
        color: var(--color-neutral-900);
      }

      .dark .icon-btn:hover {
        background-color: var(--color-neutral-100);
      }

      /*
       * Tenant pill — não é PrimeNG tag, é um chip caps. Cor latão
       * suave sobre superfície papel.
       */
      .tenant-chip {
        display: inline-flex;
        align-items: center;
        gap: 0.375rem;
        padding: 0.125rem 0.5rem;
        font-size: 0.625rem;
        font-weight: 600;
        letter-spacing: var(--letter-spacing-widest);
        text-transform: uppercase;
        color: var(--color-primary-700);
        background-color: var(--color-primary-50);
        border: 1px solid var(--color-primary-200);
        border-radius: 999px;
      }

      .dark .tenant-chip {
        color: var(--color-primary-700);
        background-color: var(--color-primary-100);
        border-color: var(--color-primary-300);
      }

      /*
       * Stagger fade-in dos grupos da nav no primeiro render. Apenas
       * 200–500ms total para não atrasar interacção.
       */
      @keyframes stagger-in {
        from {
          opacity: 0;
          transform: translateY(4px);
        }
        to {
          opacity: 1;
          transform: translateY(0);
        }
      }

      .nav-group {
        animation: stagger-in 360ms ease both;
      }

      @media (prefers-reduced-motion: reduce) {
        .nav-link::before,
        .nav-group {
          transition: none;
          animation: none;
        }
      }
    `,
  ],
  template: `
    <div class="relative flex min-h-screen">
      <!-- Sidebar persistente em lg+ — escondida em viewports menores -->
      <aside
        class="hidden lg:flex flex-col fixed inset-y-0 left-0 z-20 w-[264px]
               border-r border-[var(--color-neutral-200)]
               bg-[var(--color-neutral-0)]"
      >
        <ng-container *ngTemplateOutlet="brandHeader"></ng-container>
        <ng-container *ngTemplateOutlet="navContent"></ng-container>
        <ng-container *ngTemplateOutlet="userFooter"></ng-container>
      </aside>

      <!-- Drawer mobile — mesmo conteúdo, sliding from left -->
      <p-drawer
        [(visible)]="sidebarVisibleModel"
        position="left"
        [showCloseIcon]="false"
        [modal]="true"
        styleClass="lg:hidden"
        [style]="{ width: '264px', padding: 0 }"
      >
        <ng-template pTemplate="headless">
          <div class="flex flex-col h-full bg-[var(--color-neutral-0)]">
            <ng-container *ngTemplateOutlet="brandHeader"></ng-container>
            <ng-container *ngTemplateOutlet="navContent"></ng-container>
            <ng-container *ngTemplateOutlet="userFooter"></ng-container>
          </div>
        </ng-template>
      </p-drawer>

      <!-- Coluna principal — offset pela sidebar em lg+ -->
      <div class="flex-1 flex flex-col min-w-0 lg:pl-[264px]">
        <header class="topbar flex items-center px-4 md:px-6 gap-3">
          <button
            type="button"
            class="icon-btn lg:hidden"
            aria-label="Abrir navegação"
            (click)="sidebarVisible.set(true)"
          >
            <i class="pi pi-bars"></i>
          </button>

          <div class="flex-1 min-w-0 flex items-baseline gap-3">
            <span class="eyebrow hidden md:inline">Painel</span>
            <h2 class="section-title truncate">{{ sectionTitle() }}</h2>
          </div>

          <div class="flex items-center gap-1">
            <button
              type="button"
              class="icon-btn"
              [attr.aria-label]="theme.themeLabel()"
              [title]="theme.themeLabel()"
              (click)="theme.toggle()"
            >
              <i
                class="pi"
                [class.pi-moon]="!theme.isDark()"
                [class.pi-sun]="theme.isDark()"
              ></i>
            </button>
          </div>
        </header>

        <app-budget-alerts-banner></app-budget-alerts-banner>

        <main class="relative flex-1 px-4 py-6 md:px-8 md:py-10">
          <router-outlet></router-outlet>
        </main>
      </div>

      <p-toast position="top-right"></p-toast>
    </div>

    <!-- Templates partilhados entre sidebar persistente e drawer mobile -->

    <ng-template #brandHeader>
      <div
        class="flex items-center gap-3 px-5 h-[80px]
               border-b border-[var(--color-neutral-200)]"
      >
        <svg
          class="sextant"
          width="34"
          height="34"
          viewBox="0 0 34 34"
          aria-hidden="true"
          fill="none"
          stroke="currentColor"
          stroke-width="1.4"
          stroke-linecap="round"
        >
          <!-- arco do sextante (60° de um círculo) -->
          <path d="M3 27 A22 22 0 0 1 31 27" />
          <!-- linha do horizonte / régua -->
          <path d="M3 27 L31 27" />
          <!-- braço do índice -->
          <path d="M17 5 L17 27" stroke-width="1.6" />
          <!-- estrela alvo -->
          <path d="M17 5 L18.5 8 L17 11 L15.5 8 Z" fill="currentColor" stroke="none" />
          <circle cx="17" cy="27" r="1.6" fill="currentColor" stroke="none" />
        </svg>
        <div class="flex flex-col leading-none gap-1.5 min-w-0">
          <span class="wordmark truncate">Sextante</span>
          @if (tenantLabel(); as label) {
            <span class="tenant-chip truncate">
              <i class="pi pi-circle-fill" style="font-size:0.4rem"></i>
              {{ label }}
            </span>
          }
        </div>
      </div>
    </ng-template>

    <ng-template #navContent>
      <nav class="flex-1 overflow-y-auto py-4">
        @for (group of navGroups(); track group.id; let i = $index) {
          <div
            class="nav-group mb-5"
            [style.animation-delay.ms]="i * 60"
          >
            <div class="eyebrow px-5 pb-2">{{ group.label }}</div>
            <ul class="flex flex-col">
              @for (item of group.items; track item.routerLink) {
                <li>
                  <a
                    class="nav-link"
                    [routerLink]="item.routerLink"
                    routerLinkActive="is-active"
                    [routerLinkActiveOptions]="{ exact: false }"
                    (click)="sidebarVisible.set(false)"
                  >
                    <i [class]="item.icon"></i>
                    <span class="truncate">{{ item.label }}</span>
                  </a>
                </li>
              }
            </ul>
          </div>
        }
      </nav>
    </ng-template>

    <ng-template #userFooter>
      <div
        class="border-t border-[var(--color-neutral-200)] px-3 py-3
               bg-[var(--color-neutral-0)]"
      >
        <button
          type="button"
          class="w-full flex items-center gap-3 px-2 py-1.5 rounded-md
                 hover:bg-[var(--color-neutral-100)]
                 text-left transition-colors"
          aria-label="Menu do utilizador"
          (click)="userMenu.toggle($event)"
        >
          <span class="avatar">{{ userInitials() }}</span>
          <span class="flex-1 min-w-0 flex flex-col leading-tight">
            <span
              class="text-sm font-medium truncate
                     text-[var(--color-neutral-800)]"
            >
              {{ auth.userEmail() ?? 'Utilizador' }}
            </span>
            @if (auth.tenantRole(); as role) {
              <span
                class="text-[0.6875rem] truncate
                       text-[var(--color-neutral-500)]"
              >
                {{ translateRole(role) }}
              </span>
            }
          </span>
          <i class="pi pi-ellipsis-h text-[var(--color-neutral-500)]"></i>
        </button>
        <p-menu
          #userMenu
          [model]="userMenuItems()"
          [popup]="true"
          appendTo="body"
        ></p-menu>
      </div>
    </ng-template>
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

  /** URL actual — usada para derivar o título de secção no topbar. */
  private readonly currentUrl = signal(this.router.url);

  protected readonly tenantLabel = computed(() => {
    const name = this.auth.tenantName();
    if (!name) return null;
    const role = this.auth.tenantRole();
    return role ? `${name} · ${this.translateRole(role)}` : name;
  });

  protected readonly userInitials = computed(() => {
    const email = this.auth.userEmail();
    if (!email) return 'SX';
    const local = email.split('@')[0];
    const parts = local.split(/[._-]+/).filter(Boolean);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return local.slice(0, 2).toUpperCase();
  });

  protected readonly navGroups = computed<readonly NavGroup[]>(() => {
    const isOwner = this.auth.tenantRole() === 'Owner';

    const groups: NavGroup[] = [
      {
        id: 'overview',
        label: 'Visão geral',
        items: [
          { label: 'Dashboard', icon: 'pi pi-home', routerLink: '/app/dashboard' },
        ],
      },
      {
        id: 'movement',
        label: 'Movimento',
        items: [
          { label: 'Transações', icon: 'pi pi-list', routerLink: '/app/transactions' },
          { label: 'Recorrentes', icon: 'pi pi-sync', routerLink: '/app/recurrings' },
          { label: 'Importações', icon: 'pi pi-upload', routerLink: '/app/imports' },
        ],
      },
      {
        id: 'structure',
        label: 'Estrutura',
        items: [
          { label: 'Contas', icon: 'pi pi-wallet', routerLink: '/app/accounts' },
          { label: 'Categorias', icon: 'pi pi-tag', routerLink: '/app/categories' },
          { label: 'Orçamentos', icon: 'pi pi-chart-bar', routerLink: '/app/budgets' },
          { label: 'Regras', icon: 'pi pi-filter', routerLink: '/app/categorization-rules' },
          { label: 'Perfis de importação', icon: 'pi pi-file-import', routerLink: '/app/import-profiles' },
        ],
      },
    ];

    // Admin entries: o backend valida o role no endpoint (defesa em
    // profundidade); aqui é só UX — escondemos para Member/ReadOnly.
    if (isOwner) {
      groups.push({
        id: 'admin',
        label: 'Administração',
        items: [
          { label: 'Moedas', icon: 'pi pi-globe', routerLink: '/app/admin/currencies' },
          { label: 'Taxas de câmbio', icon: 'pi pi-chart-line', routerLink: '/app/admin/exchange-rates' },
          { label: 'Definições', icon: 'pi pi-cog', routerLink: '/app/settings/general' },
        ],
      });
    } else {
      groups.push({
        id: 'personal',
        label: 'Pessoal',
        items: [
          { label: 'Definições', icon: 'pi pi-cog', routerLink: '/app/settings/general' },
        ],
      });
    }

    return groups;
  });

  /**
   * Título da secção activa derivado do URL actual. Faz match por
   * prefixo no <c>routerLink</c> de cada item; isto cobre rotas filhas
   * tipo <c>/app/transactions/:id</c>. Fallback: "Sextante".
   */
  protected readonly sectionTitle = computed(() => {
    const url = this.currentUrl();
    const groups = this.navGroups();
    let bestMatch: NavItem | null = null;
    let bestLength = 0;
    for (const group of groups) {
      for (const item of group.items) {
        if (url.startsWith(item.routerLink) && item.routerLink.length > bestLength) {
          bestMatch = item;
          bestLength = item.routerLink.length;
        }
      }
    }
    // Rotas extra que não estão no menu principal (ex: imports/new).
    if (!bestMatch) {
      if (url.startsWith('/app/imports/new')) return 'Nova importação';
      return 'Sextante';
    }
    return bestMatch.label;
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

  constructor() {
    // Mantém o título de secção sincronizado com a navegação. Usamos
    // urlAfterRedirects para apanhar o destino final em redirects do
    // router (ex: /app → /app/dashboard).
    this.router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => {
        this.currentUrl.set(event.urlAfterRedirects);
        // Em mobile, fechar drawer ao navegar.
        if (this.sidebarVisible()) {
          this.sidebarVisible.set(false);
        }
      });
  }

  protected translateRole(role: 'Owner' | 'Member' | 'ReadOnly'): string {
    switch (role) {
      case 'Owner':
        return 'Proprietário';
      case 'Member':
        return 'Membro';
      case 'ReadOnly':
        return 'Leitura';
    }
  }

  private async handleLogout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
