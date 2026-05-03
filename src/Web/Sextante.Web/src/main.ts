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

function scrubFinancialFields(event: Sentry.ErrorEvent, _hint?: Sentry.EventHint): Sentry.ErrorEvent {
  if (event.breadcrumbs) {
    for (const crumb of event.breadcrumbs) {
      if (crumb.data) {
        for (const key of Object.keys(crumb.data)) {
          if (['amount', 'limit', 'description', 'notes', 'password'].includes(key.toLowerCase())) {
            crumb.data[key] = '[redacted]';
          }
        }
      }
    }
  }
  return event;
}

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
