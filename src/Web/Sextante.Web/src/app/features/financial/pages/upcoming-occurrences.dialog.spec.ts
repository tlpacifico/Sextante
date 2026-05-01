import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { UpcomingOccurrencesDialogComponent } from './upcoming-occurrences.dialog';

describe('UpcomingOccurrencesDialogComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UpcomingOccurrencesDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads upcoming occurrences on init and displays dates', async () => {
    const fixture = TestBed.createComponent(UpcomingOccurrencesDialogComponent);
    fixture.componentInstance.ruleId = 'rule-1';
    fixture.componentInstance.ruleDescription = 'Netflix';
    fixture.detectChanges();

    const req = httpMock.expectOne(
      '/api/financial/recurring-rules/rule-1/upcoming?count=10',
    );
    expect(req.request.method).toBe('GET');
    req.flush(['2026-05-15', '2026-06-15', '2026-07-15']);

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('2026');
    expect(text).toContain('Próxima');
  });

  it('displays empty-state message when no upcoming occurrences', async () => {
    const fixture = TestBed.createComponent(UpcomingOccurrencesDialogComponent);
    fixture.componentInstance.ruleId = 'rule-2';
    fixture.componentInstance.ruleDescription = 'Spotify';
    fixture.detectChanges();

    httpMock
      .expectOne('/api/financial/recurring-rules/rule-2/upcoming?count=10')
      .flush([]);

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Não há ocorrências futuras previstas');
  });
});
