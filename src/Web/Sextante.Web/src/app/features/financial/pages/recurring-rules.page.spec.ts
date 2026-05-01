import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { ConfirmationService } from 'primeng/api';
import { RecurringRulesPage } from './recurring-rules.page';

const MOCK_RULE = {
  id: 'rule-1',
  description: 'Netflix',
  amount: { amount: 15.99, currency: 'EUR' },
  accountId: 'acc-1',
  categoryId: 'cat-1',
  frequency: 'Monthly',
  interval: 1,
  startDate: '2026-01-01',
  endDate: null,
  nextOccurrence: '2026-05-15',
  isActive: true,
  tags: ['streaming'],
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

describe('RecurringRulesPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RecurringRulesPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads rules on init via GET /api/financial/recurring-rules', async () => {
    const fixture = TestBed.createComponent(RecurringRulesPage);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/recurring-rules');
    expect(req.request.method).toBe('GET');
    req.flush([MOCK_RULE]);

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Netflix');
  });

  it('archive confirms and calls DELETE then refreshes list', async () => {
    const fixture = TestBed.createComponent(RecurringRulesPage);
    fixture.detectChanges();

    // flush initial load
    httpMock.expectOne('/api/financial/recurring-rules').flush([MOCK_RULE]);
    await fixture.whenStable();
    fixture.detectChanges();

    // spy on component-level ConfirmationService to auto‑accept
    const confirmService = fixture.debugElement.injector.get(ConfirmationService);
    spyOn(confirmService, 'confirm').and.callFake(
      (options: { accept?: () => void }) => {
        options.accept?.();
        return confirmService;
      },
    );

    // click the archive button
    const archiveBtn = fixture.debugElement.query(By.css('[aria-label="Apagar"]'));
    (archiveBtn.nativeElement as HTMLElement).click();

    // expect DELETE
    const deleteReq = httpMock.expectOne('/api/financial/recurring-rules/rule-1');
    expect(deleteReq.request.method).toBe('DELETE');
    deleteReq.flush(null);

    // after archive, loadRules reloads the list
    await fixture.whenStable();
    const refreshReq = httpMock.expectOne('/api/financial/recurring-rules');
    refreshReq.flush([]);

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Sem regras recorrentes');
  });
});
