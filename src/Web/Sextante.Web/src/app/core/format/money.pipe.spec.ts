import { MoneyPipe } from './money.pipe';

describe('MoneyPipe', () => {
  let pipe: MoneyPipe;

  beforeEach(() => {
    pipe = new MoneyPipe();
  });

  it('formats EUR in PT-PT', () => {
    const result = pipe.transform({ amount: 12.34, currency: 'EUR' });
    // Tolerar variações de espaço (NBSP/regular) entre número e símbolo.
    expect(result).toMatch(/^12,34/);
    expect(result).toContain('€');
  });

  it('formats USD with $ symbol', () => {
    const result = pipe.transform({ amount: 100, currency: 'USD' });
    expect(result).toContain('US$');
  });

  it('returns empty string for null', () => {
    expect(pipe.transform(null)).toBe('');
  });

  it('returns empty string for undefined', () => {
    expect(pipe.transform(undefined)).toBe('');
  });

  it('formats plain number with default EUR currency', () => {
    const result = pipe.transform(50);
    expect(result).toContain('€');
  });
});
