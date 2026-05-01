import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ImportProfilesPage } from './import-profiles.page';

describe('ImportProfilesPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportProfilesPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads profiles on init via GET /api/financial/import-profiles', async () => {
    const fixture = TestBed.createComponent(ImportProfilesPage);
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/financial/import-profiles');
    expect(req.request.method).toBe('GET');
    req.flush([
      {
        id: 'p1', name: 'Millennium', delimiter: ';', hasHeaderRow: true,
        dateFormat: 'dd-MM-yyyy', decimalSeparator: ',', skipRows: 0,
        columnMappings: [{ csvColumnName: 'Data', transactionField: 'Date' }],
        createdAt: '2026-04-01T00:00:00Z', updatedAt: '2026-04-01T00:00:00Z',
      },
    ]);

    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Millennium');
  });

  it('renders the "Novo perfil" action button', () => {
    const fixture = TestBed.createComponent(ImportProfilesPage);
    fixture.detectChanges();
    httpMock.expectOne('/api/financial/import-profiles').flush([]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Novo perfil');
  });
});
