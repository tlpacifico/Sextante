import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';

/**
 * Shell público das rotas não autenticadas (<c>/signup</c>, <c>/login</c>,
 * <c>/forgot-password</c>, <c>/reset-password</c>). Sem header, sem
 * sidebar — apenas um card central sobre fundo neutro com o outlet do
 * <c>p-toast</c>.
 */
@Component({
  selector: 'app-auth-shell',
  standalone: true,
  imports: [RouterOutlet, ToastModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <main
      class="min-h-screen flex items-center justify-center px-4 py-8
             bg-[var(--p-surface-50)] dark:bg-[var(--p-surface-950)]"
    >
      <section
        class="w-full max-w-md rounded-2xl shadow-xl p-8
               bg-[var(--p-surface-0)] dark:bg-[var(--p-surface-900)]
               border border-[var(--p-surface-200)] dark:border-[var(--p-surface-800)]"
      >
        <header class="mb-6 text-center">
          <h1
            class="text-2xl font-semibold tracking-tight
                   text-[var(--p-text-color)]"
          >
            Sextante
          </h1>
          <p class="mt-1 text-sm text-[var(--p-text-muted-color)]">
            Controlo financeiro pessoal
          </p>
        </header>
        <router-outlet></router-outlet>
      </section>
      <p-toast position="top-right"></p-toast>
    </main>
  `,
})
export class AuthShellComponent {}
