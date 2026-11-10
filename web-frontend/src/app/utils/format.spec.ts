import {
  errorMessage,
  formatCompact,
  formatMoney,
  formatPct,
  statusTone,
  todayPlus,
} from './format';

describe('format utils', () => {
  it('formats money with currency and handles missing values', () => {
    expect(formatMoney(1234567, 'USD')).toBe('$1,234,567');
    expect(formatMoney(1234.5, 'EUR', 2)).toContain('1,234.50');
    expect(formatMoney(null)).toBe('-');
    expect(formatMoney(Number.NaN)).toBe('-');
  });

  it('compacts large numbers with sign', () => {
    expect(formatCompact(1_500_000)).toBe('1.5M');
    expect(formatCompact(-2_000)).toBe('-2K');
    expect(formatCompact(999)).toBe('999');
    expect(formatCompact(3_200_000_000)).toBe('3.2B');
  });

  it('formats percentages', () => {
    expect(formatPct(0.1234)).toBe('12%');
    expect(formatPct(0.1234, 1)).toBe('12.3%');
    expect(formatPct(undefined)).toBe('-');
  });

  it('maps statuses to tones', () => {
    expect(statusTone('Pass')).toBe('good');
    expect(statusTone('Warning')).toBe('warn');
    expect(statusTone('Breach')).toBe('bad');
    expect(statusTone('Ineffective')).toBe('bad');
    expect(statusTone('whatever')).toBe('neutral');
  });

  it('extracts API error messages', () => {
    expect(errorMessage({ error: { message: 'Bad thing' }, status: 400 })).toBe('Bad thing');
    expect(errorMessage({ status: 0 })).toBe('The server could not be reached');
    expect(errorMessage(null)).toBe('Something went wrong');
  });

  it('adds days to a date', () => {
    expect(todayPlus(2, new Date('2026-11-05T00:00:00Z'))).toBe('2026-11-07');
  });
});
