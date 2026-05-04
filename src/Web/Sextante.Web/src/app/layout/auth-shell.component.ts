import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';

/**
 * Shell público para rotas não autenticadas. Layout editorial em split:
 * painel esquerdo decorativo (sextante + reticulado cartográfico em
 * latão sobre tinta navy) e painel direito com o formulário sobre creme.
 * Em mobile colapsa para uma coluna com cabeçalho compacto acima do
 * formulário.
 */
@Component({
  selector: 'app-auth-shell',
  standalone: true,
  imports: [RouterOutlet, ToastModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  styles: [
    `
      :host {
        display: block;
      }

      /* Tinta navy independente do tema — o painel decorativo é sempre
         escuro mesmo em modo claro, para criar contraste editorial. */
      .navy-panel {
        --ink: #0a121d;
        --ink-2: #142031;
        --paper: #f4efe3;
        --brass: #c79b48;
        --brass-soft: #b8843e;

        position: relative;
        background:
          radial-gradient(ellipse 60% 80% at 30% 30%, rgba(199, 155, 72, 0.08) 0%, transparent 60%),
          radial-gradient(ellipse 40% 50% at 80% 80%, rgba(199, 155, 72, 0.06) 0%, transparent 70%),
          linear-gradient(135deg, var(--ink) 0%, var(--ink-2) 100%);
        color: var(--paper);
        overflow: hidden;
      }

      /* Latitude / longitude grid em pontilhado, baixíssima opacidade. */
      .navy-panel::before {
        content: '';
        position: absolute;
        inset: 0;
        pointer-events: none;
        background-image:
          radial-gradient(circle at 1px 1px, rgba(199, 155, 72, 0.18) 1px, transparent 1.5px);
        background-size: 24px 24px;
        mask-image: radial-gradient(ellipse 90% 80% at 50% 50%, black 30%, transparent 100%);
        -webkit-mask-image: radial-gradient(ellipse 90% 80% at 50% 50%, black 30%, transparent 100%);
        opacity: 0.5;
      }

      /* Estrelas distantes — três pontos brilhantes posicionados como
         constelação subtil. */
      .navy-panel::after {
        content: '';
        position: absolute;
        inset: 0;
        pointer-events: none;
        background-image:
          radial-gradient(2px 2px at 18% 24%, rgba(244, 239, 227, 0.9) 50%, transparent 100%),
          radial-gradient(1.5px 1.5px at 72% 18%, rgba(244, 239, 227, 0.7) 50%, transparent 100%),
          radial-gradient(2.5px 2.5px at 88% 62%, rgba(199, 155, 72, 0.9) 50%, transparent 100%),
          radial-gradient(1.5px 1.5px at 12% 78%, rgba(244, 239, 227, 0.6) 50%, transparent 100%),
          radial-gradient(1px 1px at 60% 88%, rgba(244, 239, 227, 0.5) 50%, transparent 100%);
      }

      .navy-panel > * {
        position: relative;
        z-index: 1;
      }

      .wordmark-display {
        font-family: var(--font-display);
        font-variation-settings: 'opsz' 144, 'SOFT' 60;
        font-style: italic;
        font-weight: 600;
        font-size: clamp(2.5rem, 6vw, 4rem);
        letter-spacing: -0.025em;
        line-height: 1;
      }

      .tagline {
        font-family: var(--font-display);
        font-variation-settings: 'opsz' 36, 'SOFT' 80;
        font-weight: 400;
        font-style: italic;
        font-size: clamp(1rem, 1.4vw, 1.125rem);
        line-height: 1.45;
        color: var(--brass);
        letter-spacing: 0.005em;
      }

      .meta-line {
        font-family: var(--font-sans);
        font-size: 0.6875rem;
        font-weight: 500;
        letter-spacing: 0.18em;
        text-transform: uppercase;
        color: rgba(244, 239, 227, 0.55);
      }

      /* Sextante grande — SVG decorativo central. Linhas em latão. */
      .sextant-art {
        color: var(--brass);
        opacity: 0.95;
        filter: drop-shadow(0 0 30px rgba(199, 155, 72, 0.18));
      }

      /* Auth card (painel direito) — sem sombra forte, hairline border,
         max-width controlado, espaçamento generoso. */
      .auth-card-title {
        font-family: var(--font-display);
        font-variation-settings: 'opsz' 72, 'SOFT' 40;
        font-weight: 500;
        font-size: 2rem;
        letter-spacing: -0.02em;
        line-height: 1.1;
        color: var(--color-neutral-900);
      }

      .auth-card-subtitle {
        font-family: var(--font-sans);
        font-size: 0.9375rem;
        color: var(--color-neutral-500);
        line-height: 1.5;
      }

      /* Em mobile, brand strip compacto. */
      .mobile-brand {
        font-family: var(--font-display);
        font-variation-settings: 'opsz' 96, 'SOFT' 60;
        font-style: italic;
        font-weight: 600;
        font-size: 1.75rem;
        letter-spacing: -0.02em;
        color: var(--color-neutral-900);
      }

      .footer-meta {
        font-family: var(--font-sans);
        font-size: 0.6875rem;
        font-weight: 500;
        letter-spacing: 0.18em;
        text-transform: uppercase;
        color: var(--color-neutral-400);
      }

      /* Stagger entrada dos elementos do painel decorativo. */
      @keyframes drift-in {
        from {
          opacity: 0;
          transform: translateY(12px);
        }
        to {
          opacity: 1;
          transform: translateY(0);
        }
      }

      .drift-1 { animation: drift-in 720ms ease 80ms both; }
      .drift-2 { animation: drift-in 720ms ease 240ms both; }
      .drift-3 { animation: drift-in 720ms ease 400ms both; }
      .drift-4 { animation: drift-in 720ms ease 560ms both; }

      @media (prefers-reduced-motion: reduce) {
        .drift-1, .drift-2, .drift-3, .drift-4 {
          animation: none;
        }
      }
    `,
  ],
  template: `
    <main class="min-h-screen flex bg-[var(--color-neutral-50)]">
      <!-- Painel decorativo — visível apenas em md+ -->
      <aside
        class="navy-panel hidden md:flex md:w-[42%] lg:w-[48%] xl:w-[44%]
               flex-col justify-between p-10 lg:p-14"
      >
        <header class="flex items-center gap-3 drift-1">
          <svg
            width="28"
            height="28"
            viewBox="0 0 34 34"
            aria-hidden="true"
            fill="none"
            stroke="currentColor"
            stroke-width="1.4"
            stroke-linecap="round"
            class="sextant-art"
          >
            <path d="M3 27 A22 22 0 0 1 31 27" />
            <path d="M3 27 L31 27" />
            <path d="M17 5 L17 27" stroke-width="1.6" />
            <path d="M17 5 L18.5 8 L17 11 L15.5 8 Z" fill="currentColor" stroke="none" />
            <circle cx="17" cy="27" r="1.6" fill="currentColor" stroke="none" />
          </svg>
          <span class="meta-line">Sextante · Cartografia do capital</span>
        </header>

        <!-- Bloco central: arte + tagline -->
        <div class="flex flex-col gap-8 max-w-md">
          <div class="drift-2">
            <svg
              class="sextant-art"
              width="240"
              height="240"
              viewBox="0 0 240 240"
              aria-hidden="true"
              fill="none"
              stroke="currentColor"
              stroke-linecap="round"
              stroke-linejoin="round"
            >
              <!-- Arco principal (60°) — a alma do sextante -->
              <path
                d="M30 200 A150 150 0 0 1 210 200"
                stroke-width="1.5"
              />
              <!-- Marcações de graus no arco — pequenas linhas radiais -->
              <g stroke-width="1" opacity="0.7">
                <line x1="40" y1="180" x2="46" y2="174" />
                <line x1="58" y1="148" x2="65" y2="144" />
                <line x1="84" y1="120" x2="92" y2="118" />
                <line x1="120" y1="106" x2="120" y2="98" />
                <line x1="156" y1="120" x2="148" y2="118" />
                <line x1="182" y1="148" x2="175" y2="144" />
                <line x1="200" y1="180" x2="194" y2="174" />
              </g>
              <!-- Régua horizontal -->
              <line
                x1="20"
                y1="200"
                x2="220"
                y2="200"
                stroke-width="1.5"
              />
              <!-- Braço do índice -->
              <line
                x1="120"
                y1="40"
                x2="120"
                y2="200"
                stroke-width="2"
              />
              <!-- Telescópio diagonal -->
              <line
                x1="120"
                y1="200"
                x2="60"
                y2="220"
                stroke-width="2"
              />
              <circle cx="60" cy="220" r="5" fill="currentColor" stroke="none" />
              <!-- Estrela alvo (vega) no topo -->
              <g transform="translate(120 40)">
                <path d="M0 -8 L2.5 -2.5 L8 0 L2.5 2.5 L0 8 L-2.5 2.5 L-8 0 L-2.5 -2.5 Z" fill="currentColor" stroke="none" />
                <circle r="1.5" fill="rgba(244,239,227,0.9)" stroke="none" />
              </g>
              <!-- Pivô central -->
              <circle cx="120" cy="200" r="5" fill="currentColor" stroke="none" />
              <!-- Linha de horizonte estendida (ar) -->
              <path
                d="M0 200 L20 200 M220 200 L240 200"
                stroke-width="1"
                stroke-dasharray="2 6"
                opacity="0.6"
              />
            </svg>
          </div>

          <h1 class="wordmark-display drift-3">Sextante</h1>

          <p class="tagline drift-3">
            Encontre o rumo das suas finanças.<br />
            Latitude após latitude — uma carta clara do seu património.
          </p>
        </div>

        <footer class="flex items-end justify-between gap-6 drift-4">
          <p class="text-xs leading-relaxed max-w-xs" style="color: rgba(244, 239, 227, 0.7); font-family: var(--font-display); font-style: italic;">
            "Não se governa um navio sem rumo;
            tampouco um capital sem registo."
          </p>
          <span class="meta-line whitespace-nowrap">EST. MMXXVI</span>
        </footer>
      </aside>

      <!-- Painel do formulário -->
      <section
        class="flex-1 flex flex-col items-stretch md:items-center
               justify-center px-6 py-10 md:px-10 md:py-14"
      >
        <!-- Brand strip mobile (escondido em md+) -->
        <div
          class="md:hidden flex items-center gap-3 mb-8 pb-6
                 border-b border-[var(--color-neutral-200)]"
        >
          <svg
            width="28"
            height="28"
            viewBox="0 0 34 34"
            aria-hidden="true"
            fill="none"
            stroke="currentColor"
            stroke-width="1.4"
            stroke-linecap="round"
            style="color: var(--color-primary-500)"
          >
            <path d="M3 27 A22 22 0 0 1 31 27" />
            <path d="M3 27 L31 27" />
            <path d="M17 5 L17 27" stroke-width="1.6" />
            <path d="M17 5 L18.5 8 L17 11 L15.5 8 Z" fill="currentColor" stroke="none" />
            <circle cx="17" cy="27" r="1.6" fill="currentColor" stroke="none" />
          </svg>
          <span class="mobile-brand">Sextante</span>
        </div>

        <div class="w-full max-w-[420px] flex flex-col gap-8">
          <header class="flex flex-col gap-2">
            <span class="eyebrow">Acesso</span>
            <h2 class="auth-card-title">Bem-vindo a bordo</h2>
            <p class="auth-card-subtitle">
              Inicie sessão para consultar a sua carta financeira.
            </p>
          </header>

          <router-outlet></router-outlet>
        </div>

        <p class="footer-meta mt-10 hidden md:block">
          © MMXXVI · Sextante · Lisboa
        </p>
      </section>

      <p-toast position="top-right"></p-toast>
    </main>
  `,
})
export class AuthShellComponent {}
