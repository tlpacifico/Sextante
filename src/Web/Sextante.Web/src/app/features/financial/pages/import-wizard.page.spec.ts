import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { ImportWizardPage } from './import-wizard.page';

describe('ImportWizardPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportWizardPage],
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

  it('loads profiles on init for the profile picker', async () => {
    const fixture = TestBed.createComponent(ImportWizardPage);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/import-profiles');
    expect(req.request.method).toBe('GET');
    req.flush([]);

    await fixture.whenStable();
    fixture.detectChanges();

    // Wizard renders Step 1 — file upload affordance.
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text.length).toBeGreaterThan(0);
  });
});
