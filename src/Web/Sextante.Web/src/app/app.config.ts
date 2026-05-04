import {
  ApplicationConfig,
  ErrorHandler,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { registerLocaleData } from '@angular/common';
import {
  provideHttpClient,
  withFetch,
  withInterceptors,
} from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import localePt from '@angular/common/locales/pt-PT';
import { providePrimeNG } from 'primeng/config';
import Aura from '@primeuix/themes/aura';
import { definePreset } from '@primeuix/themes';
import * as Sentry from '@sentry/angular';

import { routes } from './app.routes';
import { authInterceptor } from './auth/auth.interceptor';
import { AuthService } from './auth/auth.service';

registerLocaleData(localePt, 'pt-PT');

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    // Phase 5.5 — bloqueia bootstrap até a sessão ser re-hidratada a partir
    // de localStorage / refresh cookie. Sem isto, o AuthGuard corre antes
    // do AuthService saber que há uma sessão válida e redirecciona para
    // /login depois de F5 (regressão da DoD #27).
    provideAppInitializer(() => {
      const auth = inject(AuthService);
      return auth.rehydrateFromStorage();
    }),
    providePrimeNG({
      theme: {
        // Sextante — paleta editorial "Cartografia do capital".
        // Primary: latão polido (substitui indigo). Surface: papel
        // creme em modo claro / tinta navy Oxford em modo escuro. Os
        // valores aqui têm de espelhar `tokens.css` — alterar ambos
        // em conjunto.
        preset: definePreset(Aura, {
          semantic: {
            primary: {
              50: '#fbf6ec',
              100: '#f3e6c8',
              200: '#e7cf95',
              300: '#d8b466',
              400: '#c79b48',
              500: '#b8843e',
              600: '#9a6c33',
              700: '#7a5429',
              800: '#553a1d',
              900: '#392612',
              950: '#1f150a',
            },
            colorScheme: {
              light: {
                surface: {
                  0: '#fbf8f1',
                  50: '#f4efe3',
                  100: '#ebe3d0',
                  200: '#ddd0b5',
                  300: '#c7b797',
                  400: '#998b6e',
                  500: '#73685a',
                  600: '#544d45',
                  700: '#3a3530',
                  800: '#241f1a',
                  900: '#171410',
                  950: '#0a0805',
                },
              },
              dark: {
                surface: {
                  0: '#0a121d',
                  50: '#0e1622',
                  100: '#142031',
                  200: '#1f2c40',
                  300: '#2d3b53',
                  400: '#4d5b73',
                  500: '#7886a0',
                  600: '#a8b2c5',
                  700: '#cdd3df',
                  800: '#e3e6ec',
                  900: '#f0f1f4',
                  950: '#ffffff',
                },
              },
            },
          },
        }),
        options: {
          darkModeSelector: '.dark',
          cssLayer: {
            name: 'primeng',
            order: 'tailwind-base, primeng, tailwind-utilities',
          },
        },
      },
    }),
    { provide: LOCALE_ID, useValue: 'pt-PT' },
    { provide: ErrorHandler, useValue: Sentry.createErrorHandler() },
    { provide: Sentry.TraceService, deps: [] },
  ],
};
