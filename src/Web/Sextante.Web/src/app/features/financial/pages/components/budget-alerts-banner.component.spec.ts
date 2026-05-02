import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { signal } from '@angular/core';
import { BudgetAlertsBannerComponent } from './budget-alerts-banner.component';
import { AuthService } from '../../../../auth/auth.service';
import { BudgetAlertDto } from '../../../../core/api/financial.types';

class StubAuthService {
  isAuthenticated = signal(true);
}

const ALERT_80: BudgetAlertDto = {
  id: 'a1',
  budgetId: 'b1',
  categoryId: 'c1',
  threshold: 80,
  triggeredAt: '2026-05-15T10:00:00Z',
  spentAtTriggerAmount: 400,
  spentAtTriggerCurrency: 'EUR',
  acknowledged: false,
  acknowledgedAt: null,
};

const ALERT_100: BudgetAlertDto = {
  ...ALERT_80,
  id: 'a2',
  threshold: 100,
  spentAtTriggerAmount: 550,
};

describe('BudgetAlertsBannerComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BudgetAlertsBannerComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: AuthService, useClass: StubAuthService },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches active alerts on init and renders count', async () => {
    const fixture = TestBed.createComponent(BudgetAlertsBannerComponent);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/budgets/alerts/active');
    expect(req.request.method).toBe('GET');
    req.flush([ALERT_80]);

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('próximo do limite');
  });

  it('shows critical message when any alert at 100%', async () => {
    const fixture = TestBed.createComponent(BudgetAlertsBannerComponent);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/budgets/alerts/active');
    req.flush([ALERT_80, ALERT_100]);

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('atingiram o limite');
  });

  it('hides banner when no alerts', async () => {
    const fixture = TestBed.createComponent(BudgetAlertsBannerComponent);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/budgets/alerts/active');
    req.flush([]);

    await fixture.whenStable();
    fixture.detectChanges();

    const banner = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="budget-alerts-banner"]',
    );
    expect(banner).toBeNull();
  });

  it('acknowledge button posts to acknowledge endpoint for each alert', async () => {
    const fixture = TestBed.createComponent(BudgetAlertsBannerComponent);
    fixture.detectChanges();

    httpMock.expectOne('/api/financial/budgets/alerts/active').flush([ALERT_80, ALERT_100]);
    await fixture.whenStable();
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="budget-alerts-acknowledge"] button',
    ) as HTMLButtonElement | null;
    expect(button).toBeTruthy();
    button!.click();

    const reqA = httpMock.expectOne('/api/financial/budgets/alerts/a1/acknowledge');
    expect(reqA.request.method).toBe('POST');
    reqA.flush(null);

    const reqB = httpMock.expectOne('/api/financial/budgets/alerts/a2/acknowledge');
    expect(reqB.request.method).toBe('POST');
    reqB.flush(null);
  });
});
