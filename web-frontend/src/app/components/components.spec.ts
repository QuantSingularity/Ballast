import { TestBed } from '@angular/core/testing';
import { BarList } from './bar-list';
import { KpiCard } from './kpi-card';
import { LineBandChart } from './line-band-chart';
import { StatusBadge } from './status-badge';

describe('LineBandChart', () => {
  it('renders line, band and axis labels for a forecast with intervals', async () => {
    const fixture = TestBed.createComponent(LineBandChart);
    fixture.componentRef.setInput('series', [
      { label: '11-05', value: 100, lo: 50, hi: 150 },
      { label: '11-06', value: 120, lo: 60, hi: 190 },
      { label: '11-07', value: 90, lo: -20, hi: 160 },
    ]);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('svg')?.getAttribute('role')).toBe('img');
    expect(el.querySelector('path.line')?.getAttribute('d')).toMatch(/^M/);
    expect(el.querySelector('path.band')?.getAttribute('d')).toMatch(/Z$/);
    expect(el.querySelectorAll('text').length).toBeGreaterThan(3);
    expect(el.querySelector('.zero-line')).not.toBeNull();
  });

  it('draws history without a band and shows an empty state with no data', async () => {
    const fixture = TestBed.createComponent(LineBandChart);
    fixture.componentRef.setInput('series', [
      { label: 'a', value: 1 },
      { label: 'b', value: 2 },
    ]);
    fixture.componentRef.setInput('history', [
      { label: 'x', value: 0 },
      { label: 'y', value: 1 },
    ]);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('path.band')).toBeNull();
    expect(el.querySelector('path.history')).not.toBeNull();

    const empty = TestBed.createComponent(LineBandChart);
    await empty.whenStable();
    expect((empty.nativeElement as HTMLElement).textContent).toContain(
      'No data',
    );
  });
});

describe('StatusBadge, KpiCard and BarList', () => {
  it('renders a toned, human-readable badge', async () => {
    const f = TestBed.createComponent(StatusBadge);
    f.componentRef.setInput('status', 'PendingApproval');
    await f.whenStable();
    const span = (f.nativeElement as HTMLElement).querySelector('span')!;
    expect(span.textContent?.trim()).toBe('Pending Approval');
    expect(span.className).toContain('warn');
  });

  it('renders a KPI with hint and tone', async () => {
    const f = TestBed.createComponent(KpiCard);
    f.componentRef.setInput('label', 'Liquidity');
    f.componentRef.setInput('value', '6.7x');
    f.componentRef.setInput('hint', 'vs outflows');
    f.componentRef.setInput('tone', 'good');
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.textContent).toContain('6.7x');
    expect(el.textContent).toContain('vs outflows');
    expect(el.querySelector('.kpi.good')).not.toBeNull();
  });

  it('clamps bar widths into 0 to 100 percent', async () => {
    const f = TestBed.createComponent(BarList);
    f.componentRef.setInput('items', [
      { label: 'A', share: 1.7, display: 'a' },
      { label: 'B', share: -0.2, display: 'b' },
      { label: 'C', share: 0.25, display: 'c', tone: 'bad' },
    ]);
    await f.whenStable();
    const widths = Array.from(
      (f.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        '.bar-fill',
      ),
    ).map((e) => e.style.width);
    expect(widths).toEqual(['100%', '0%', '25%']);
  });
});
