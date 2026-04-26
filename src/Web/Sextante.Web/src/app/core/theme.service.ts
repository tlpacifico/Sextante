import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';

const STORAGE_KEY = 'sextante.theme';
const DARK_CLASS = 'dark';

type ThemePreference = 'dark' | 'light';

/**
 * Mantém o estado de dark mode num signal e sincroniza com:
 * 1. <c>document.documentElement</c> (classe <c>.dark</c> — Aura preset
 *    e Tailwind <c>darkMode: 'class'</c> ambos olham para aqui).
 * 2. <c>localStorage</c> sob a chave <c>sextante.theme</c> ('dark'|'light').
 *
 * <p>Inicialização: <c>localStorage</c> &gt; <c>prefers-color-scheme:
 * dark</c> &gt; <c>light</c>. Aplica a classe imediatamente para evitar
 * "flash of unstyled content".</p>
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  private readonly darkSignal = signal<boolean>(this.resolveInitialTheme());

  readonly isDark = this.darkSignal.asReadonly();
  readonly themeIcon = computed(() => (this.darkSignal() ? 'pi-sun' : 'pi-moon'));
  readonly themeLabel = computed(() =>
    this.darkSignal() ? 'Tema claro' : 'Tema escuro',
  );

  constructor() {
    // Aplicar classe imediatamente (antes do primeiro paint quando este
    // service é instanciado pelo bootstrap do app).
    this.applyClass(this.darkSignal());

    effect(() => {
      const dark = this.darkSignal();
      this.applyClass(dark);
      this.persist(dark);
    });
  }

  toggle(): void {
    this.darkSignal.update((value) => !value);
  }

  set(value: boolean): void {
    this.darkSignal.set(value);
  }

  private resolveInitialTheme(): boolean {
    if (typeof window === 'undefined') {
      return false;
    }

    const stored = window.localStorage?.getItem(STORAGE_KEY) as ThemePreference | null;
    if (stored === 'dark') {
      return true;
    }
    if (stored === 'light') {
      return false;
    }

    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
  }

  private applyClass(dark: boolean): void {
    const root = this.document.documentElement;
    if (dark) {
      root.classList.add(DARK_CLASS);
    } else {
      root.classList.remove(DARK_CLASS);
    }
  }

  private persist(dark: boolean): void {
    try {
      window.localStorage?.setItem(STORAGE_KEY, dark ? 'dark' : 'light');
    } catch {
      // SSR ou storage desactivado — silenciar.
    }
  }
}
