import {
  ApplicationConfig,
  ErrorHandler,
  LOCALE_ID,
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

registerLocaleData(localePt, 'pt-PT');

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    providePrimeNG({
      theme: {
        preset: definePreset(Aura, {
          semantic: {
            primary: {
              50: '{indigo.50}',
              100: '{indigo.100}',
              200: '{indigo.200}',
              300: '{indigo.300}',
              400: '{indigo.400}',
              500: '{indigo.500}',
              600: '{indigo.600}',
              700: '{indigo.700}',
              800: '{indigo.800}',
              900: '{indigo.900}',
              950: '{indigo.950}',
            },
            colorScheme: {
              light: {
                surface: {
                  0: '#ffffff',
                  50: '#f8fafc',
                  100: '#f1f5f9',
                  200: '#e2e8f0',
                  300: '#cbd5e1',
                  400: '#94a3b8',
                  500: '#64748b',
                  600: '#475569',
                  700: '#334155',
                  800: '#1e293b',
                  900: '#0f172a',
                  950: '#020617',
                },
              },
              dark: {
                surface: {
                  0: '#0f172a',
                  50: '#1e293b',
                  100: '#334155',
                  200: '#475569',
                  300: '#64748b',
                  400: '#94a3b8',
                  500: '#cbd5e1',
                  600: '#e2e8f0',
                  700: '#f1f5f9',
                  800: '#f8fafc',
                  900: '#ffffff',
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
