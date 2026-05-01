import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { ImportBatchesPage } from './import-batches.page';

describe('ImportBatchesPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportBatchesPage],
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

  it('loads batches on init via GET /api/financial/imports', async () => {
    const fixture = TestBed.createComponent(ImportBatchesPage);
    fixture.detectChanges(); // triggers ngOnInit

    const req = httpMock.expectOne('/api/financial/imports');
    expect(req.request.method).toBe('GET');
    req.flush([
      { id: 'b1', fileName: 'extrato.csv', status: 'Completed', totalRows: 10, importedRows: 10, errorRows: 0, duplicateRows: 0, createdAt: '2026-04-01T00:00:00Z', updatedAt: '2026-04-01T00:00:00Z' },
    ]);

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('extrato.csv');
  });

  it('shows empty-state message when no batches', async () => {
    const fixture = TestBed.createComponent(ImportBatchesPage);
    fixture.detectChanges();

    httpMock.expectOne('/api/financial/imports').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Sem importações recentes');
  });
});
