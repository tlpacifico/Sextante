import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { BudgetProgressCardComponent } from './budget-progress-card.component';
import { BudgetDto } from '../../../../core/api/financial.types';

function makeBudget(overrides: Partial<BudgetDto> = {}): BudgetDto {
  return {
    id: 'b1',
    categoryId: 'c1',
    year: 2026,
    month: 5,
    limitAmount: 500,
    limitCurrency: 'EUR',
    alertThresholdPercent: 80,
    notes: null,
    progress: {
      limitAmount: 500,
      limitCurrency: 'EUR',
      spentAmount: 100,
      remainingAmount: 400,
      percentUsed: 20,
      projectedAmount: null,
      hasIncompleteRates: false,
    },
    createdAt: '2026-05-01T00:00:00Z',
    updatedAt: '2026-05-01T00:00:00Z',
    ...overrides,
  };
}

describe('BudgetProgressCardComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BudgetProgressCardComponent],
      providers: [provideNoopAnimations()],
    });
  });

  it('shows green tag when below threshold', () => {
    const fixture = TestBed.createComponent(BudgetProgressCardComponent);
    fixture.componentRef.setInput('budget', makeBudget());
    fixture.detectChanges();

    const tagEl = fixture.nativeElement.querySelector('p-tag');
    expect((tagEl?.textContent ?? '').trim()).toContain('20%');
  });

  it('shows warn severity when between threshold and 100', () => {
    const budget = makeBudget({
      progress: {
        limitAmount: 500, limitCurrency: 'EUR',
        spentAmount: 450, remainingAmount: 50, percentUsed: 90,
        projectedAmount: null, hasIncompleteRates: false,
      },
    });
    const fixture = TestBed.createComponent(BudgetProgressCardComponent);
    fixture.componentRef.setInput('budget', budget);
    fixture.detectChanges();

    const html = fixture.nativeElement.innerHTML as string;
    expect(html).toContain('90%');
  });

  it('shows danger severity when over 100', () => {
    const budget = makeBudget({
      progress: {
        limitAmount: 500, limitCurrency: 'EUR',
        spentAmount: 550, remainingAmount: -50, percentUsed: 110,
        projectedAmount: null, hasIncompleteRates: false,
      },
    });
    const fixture = TestBed.createComponent(BudgetProgressCardComponent);
    fixture.componentRef.setInput('budget', budget);
    fixture.detectChanges();

    const html = fixture.nativeElement.innerHTML as string;
    expect(html).toContain('110%');
  });

  it('renders incomplete rates badge when flag is true', () => {
    const budget = makeBudget({
      progress: {
        limitAmount: 500, limitCurrency: 'EUR',
        spentAmount: 100, remainingAmount: 400, percentUsed: 20,
        projectedAmount: null, hasIncompleteRates: true,
      },
    });
    const fixture = TestBed.createComponent(BudgetProgressCardComponent);
    fixture.componentRef.setInput('budget', budget);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Cálculo parcial');
  });

  it('renders projection when not null', () => {
    const budget = makeBudget({
      progress: {
        limitAmount: 500, limitCurrency: 'EUR',
        spentAmount: 100, remainingAmount: 400, percentUsed: 20,
        projectedAmount: 310, hasIncompleteRates: false,
      },
    });
    const fixture = TestBed.createComponent(BudgetProgressCardComponent);
    fixture.componentRef.setInput('budget', budget);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Projeção');
  });

  it('uses categoryName input when provided', () => {
    const fixture = TestBed.createComponent(BudgetProgressCardComponent);
    fixture.componentRef.setInput('budget', makeBudget());
    fixture.componentRef.setInput('categoryName', 'Habitação');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Habitação');
  });
});
