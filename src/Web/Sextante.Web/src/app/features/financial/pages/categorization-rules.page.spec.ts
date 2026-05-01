import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { CategorizationRulesPage } from './categorization-rules.page';

describe('CategorizationRulesPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CategorizationRulesPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads rules and categories on init', async () => {
    const fixture = TestBed.createComponent(CategorizationRulesPage);
    fixture.detectChanges();

    httpMock.expectOne('/api/financial/categorization-rules').flush([
      {
        id: 'r1', name: 'Continente', pattern: 'CONTINENTE',
        matchType: 'Contains', categoryId: 'cat-1',
        categoryName: 'Supermercado', categoryIcon: 'pi-shopping-cart',
        categoryColor: '#0EA5E9', priority: 1, isActive: true,
        createdAt: '2026-04-01T00:00:00Z', updatedAt: '2026-04-01T00:00:00Z',
      },
    ]);
    httpMock.expectOne('/api/financial/categories').flush([
      { id: 'cat-1', name: 'Supermercado', kind: 'Expense', iconName: 'pi-shopping-cart', colorHex: '#0EA5E9', createdAt: '2026-04-01T00:00:00Z', updatedAt: '2026-04-01T00:00:00Z' },
    ]);

    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Continente');
  });

  it('renders the "Nova regra" action', async () => {
    const fixture = TestBed.createComponent(CategorizationRulesPage);
    fixture.detectChanges();

    httpMock.expectOne('/api/financial/categorization-rules').flush([]);
    httpMock.expectOne('/api/financial/categories').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Nova regra');
  });
});
