import { bandPath, linePath, niceTicks } from './chart';

describe('chart utils', () => {
  it('produces ascending nice ticks that cover the range', () => {
    const ticks = niceTicks(-130, 870, 5);
    expect(ticks[0]).toBeLessThanOrEqual(-130);
    expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(870);
    for (let i = 1; i < ticks.length; i++)
      expect(ticks[i]).toBeGreaterThan(ticks[i - 1] ?? 0);
  });

  it('handles degenerate ranges', () => {
    expect(niceTicks(5, 5)).toEqual([5]);
    expect(niceTicks(Number.NaN, 5)).toEqual([0]);
  });

  it('builds line and band paths', () => {
    expect(
      linePath([
        { x: 0, y: 1 },
        { x: 10, y: 2 },
      ]),
    ).toBe('M0.0 1.0 L10.0 2.0');
    const band = bandPath(
      [
        { x: 0, y: 0 },
        { x: 10, y: 0 },
      ],
      [
        { x: 0, y: 5 },
        { x: 10, y: 5 },
      ],
    );
    expect(band.startsWith('M0.0 0.0')).toBe(true);
    expect(band.endsWith('Z')).toBe(true);
    expect(bandPath([], [])).toBe('');
  });
});
