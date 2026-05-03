import { bootstrapApplication } from '@angular/platform-browser';
import * as Sentry from '@sentry/angular';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { environment } from './environments/environment';

if (environment.sentryDsn) {
  Sentry.init({
    dsn: environment.sentryDsn,
    environment: environment.production ? 'production' : 'development',
    tracesSampleRate: 0.1,
    replaysSessionSampleRate: 0,
    beforeSend(event) {
      return scrubFinancialFields(event);
    },
    integrations: [Sentry.browserTracingIntegration()],
  });
}

// Phase 5.5 — campos financeiros / PII a mascarar em qualquer payload
// que vá para Sentry. Lista canónica usada por breadcrumbs, request.data,
// extra, contexts e tags.
const SENSITIVE_KEYS = new Set([
  'amount',
  'amountamount',
  'limit',
  'limitamount',
  'description',
  'notes',
  'password',
  'email',
]);
const REDACTED = '[redacted]';

function scrubFinancialFields(event: Sentry.ErrorEvent, _hint?: Sentry.EventHint): Sentry.ErrorEvent {
  if (event.breadcrumbs) {
    for (const crumb of event.breadcrumbs) {
      if (crumb.data) {
        scrubObject(crumb.data);
      }
      if (typeof crumb.message === 'string') {
        crumb.message = scrubString(crumb.message);
      }
    }
  }

  if (event.request) {
    if (event.request.data) {
      event.request.data = scrubAny(event.request.data);
    }
    if (event.request.headers) {
      // Authorization é sempre Bearer <token> — corta de propósito.
      for (const h of Object.keys(event.request.headers)) {
        if (h.toLowerCase() === 'authorization' || h.toLowerCase() === 'cookie') {
          event.request.headers[h] = REDACTED;
        }
      }
    }
  }

  if (event.extra) {
    scrubObject(event.extra as Record<string, unknown>);
  }
  if (event.contexts) {
    for (const ctx of Object.values(event.contexts)) {
      if (ctx && typeof ctx === 'object') {
        scrubObject(ctx as Record<string, unknown>);
      }
    }
  }
  if (event.tags) {
    for (const k of Object.keys(event.tags)) {
      if (SENSITIVE_KEYS.has(k.toLowerCase())) {
        event.tags[k] = REDACTED;
      }
    }
  }
  if (event.exception?.values) {
    for (const ex of event.exception.values) {
      if (typeof ex.value === 'string') {
        ex.value = scrubString(ex.value);
      }
    }
  }
  if (typeof event.message === 'string') {
    event.message = scrubString(event.message);
  }

  return event;
}

function scrubObject(obj: Record<string, unknown>): void {
  for (const key of Object.keys(obj)) {
    if (SENSITIVE_KEYS.has(key.toLowerCase())) {
      obj[key] = REDACTED;
    } else if (obj[key] && typeof obj[key] === 'object') {
      scrubObject(obj[key] as Record<string, unknown>);
    }
  }
}

function scrubAny(value: unknown): unknown {
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    scrubObject(value as Record<string, unknown>);
    return value;
  }
  if (Array.isArray(value)) {
    for (const item of value) {
      if (item && typeof item === 'object') {
        scrubObject(item as Record<string, unknown>);
      }
    }
  }
  return value;
}

function scrubString(text: string): string {
  // Mascarar emails embutidos em strings (logs, mensagens, tracebacks).
  return text.replace(/[\w.+-]+@[\w.-]+\.[a-zA-Z]{2,}/g, '[email]');
}

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
