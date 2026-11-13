import { Component, computed, input } from '@angular/core';
import { ChartPoint, bandPath, linePath, niceTicks } from '../utils/chart';
import { formatCompact } from '../utils/format';

const W = 800;
const H = 300;
const M = { l: 64, r: 16, t: 12, b: 28 };

@Component({
  selector: 'app-line-band-chart',
  template: `
    @if (geo(); as g) {
      <svg
        class="chart"
        [attr.viewBox]="'0 0 ' + width + ' ' + height"
        role="img"
        [attr.aria-label]="ariaLabel()"
      >
        @for (t of g.yTicks; track t.y) {
          <line
            class="grid-line"
            [attr.x1]="margin.l"
            [attr.x2]="width - margin.r"
            [attr.y1]="t.y"
            [attr.y2]="t.y"
          />
          <text [attr.x]="margin.l - 8" [attr.y]="t.y + 4" text-anchor="end">{{ t.label }}</text>
        }
        @for (t of g.xTicks; track t.x) {
          <text [attr.x]="t.x" [attr.y]="height - 8" text-anchor="middle">{{ t.label }}</text>
        }
        @if (g.zeroY !== null) {
          <line
            class="zero-line"
            [attr.x1]="margin.l"
            [attr.x2]="width - margin.r"
            [attr.y1]="g.zeroY"
            [attr.y2]="g.zeroY"
          />
        }
        @if (g.bandPath) {
          <path class="band" [attr.d]="g.bandPath" />
        }
        @if (g.historyPath) {
          <path class="history" [attr.d]="g.historyPath" />
        }
        <path class="line" [attr.d]="g.linePath" />
      </svg>
    } @else {
      <div class="empty">No data to chart</div>
    }
  `,
})
export class LineBandChart {
  readonly series = input<ChartPoint[]>([]);
  readonly history = input<ChartPoint[]>([]);
  readonly ariaLabel = input('Line chart with confidence band');

  protected readonly width = W;
  protected readonly height = H;
  protected readonly margin = M;

  protected readonly geo = computed(() => {
    const hist = this.history();
    const fc = this.series();
    const all = [...hist, ...fc];
    if (all.length === 0) return null;

    let min = Infinity;
    let max = -Infinity;
    for (const p of all) {
      min = Math.min(min, p.value, p.lo ?? p.value);
      max = Math.max(max, p.value, p.hi ?? p.value);
    }
    if (min === max) {
      min -= 1;
      max += 1;
    }
    const ticks = niceTicks(min, max, 5);
    const lo = ticks[0] ?? min;
    const hi = ticks[ticks.length - 1] ?? max;
    const innerW = W - M.l - M.r;
    const innerH = H - M.t - M.b;
    const xOf = (i: number) =>
      M.l + (all.length === 1 ? innerW / 2 : (i / (all.length - 1)) * innerW);
    const yOf = (v: number) => M.t + (1 - (v - lo) / (hi - lo || 1)) * innerH;

    const histPts = hist.map((p, i) => ({ x: xOf(i), y: yOf(p.value) }));
    const fcPts = fc.map((p, i) => ({ x: xOf(hist.length + i), y: yOf(p.value) }));
    const upper = fc.map((p, i) => ({ x: xOf(hist.length + i), y: yOf(p.hi ?? p.value) }));
    const lower = fc.map((p, i) => ({ x: xOf(hist.length + i), y: yOf(p.lo ?? p.value) }));
    const hasBand = fc.some((p) => p.lo !== undefined && p.hi !== undefined);

    const step = Math.max(1, Math.round(all.length / 6));
    const xTicks = all
      .map((p, i) => ({ x: xOf(i), label: p.label, i }))
      .filter((t) => t.i % step === 0)
      .map(({ x, label }) => ({ x, label }));

    return {
      yTicks: ticks.map((t) => ({ y: yOf(t), label: formatCompact(t) })),
      xTicks,
      zeroY: lo < 0 && hi > 0 ? yOf(0) : null,
      historyPath: histPts.length > 1 ? linePath(histPts) : '',
      linePath: linePath(fcPts),
      bandPath: hasBand ? bandPath(upper, lower) : '',
    };
  });
}
