import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { KpiCard } from '../components/kpi-card';
import { LineBandChart } from '../components/line-band-chart';
import { PageHeader } from '../components/page-header';
import { ApiService } from '../services/api.service';
import { BacktestMetrics, Forecast } from '../types/models';
import { ChartPoint } from '../utils/chart';
import {
  errorMessage,
  formatCompact,
  formatMoney,
  formatPct,
  valueOf,
} from '../utils/format';

@Component({
  selector: 'app-forecast',
  imports: [PageHeader, LineBandChart, KpiCard],
  template: `
    <div class="page">
      <app-page-header
        title="Cash forecast"
        subtitle="Ensemble of damped Holt-Winters and calendar-feature ridge regression with rolling-origin calibrated intervals"
      />
      <section class="card" style="margin-bottom: 16px">
        <div class="form-grid">
          <label
            >Scope
            <select
              [value]="scope()"
              (change)="scope.set(value($event)); reload()"
            >
              <option value="all">Group (all operating accounts)</option>
              @for (c of currencies; track c) {
                <option [value]="'currency:' + c">
                  {{ c }} accounts (native currency)
                </option>
              }
            </select>
          </label>
          <label
            >Horizon
            <select
              [value]="horizon()"
              (change)="horizon.set(+value($event)); reload()"
            >
              @for (h of horizons; track h) {
                <option [value]="h">{{ h }} days</option>
              }
            </select>
          </label>
          <label
            >Confidence
            <select
              [value]="confidence()"
              (change)="confidence.set(+value($event)); reload()"
            >
              @for (c of confidences; track c) {
                <option [value]="c">{{ c * 100 }}%</option>
              }
            </select>
          </label>
          <label
            >View
            <select [value]="view()" (change)="view.set(value($event))">
              <option value="cumulative">Cumulative net cash flow</option>
              <option value="daily">Daily net cash flow</option>
            </select>
          </label>
        </div>
      </section>
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (loading()) {
        <div class="loading">Computing forecast...</div>
      } @else if (forecast(); as f) {
        <div class="grid kpis">
          <app-kpi-card
            label="Expected net flow"
            [value]="money(endPoint(f), f.currency)"
            [hint]="'Next ' + f.horizonDays + ' days'"
          />
          <app-kpi-card
            label="Lower bound"
            [value]="money(endLower(f), f.currency)"
            [hint]="formatPct(f.confidence) + ' interval'"
          />
          <app-kpi-card
            label="Upper bound"
            [value]="money(endUpper(f), f.currency)"
            [hint]="formatPct(f.confidence) + ' interval'"
          />
          <app-kpi-card
            label="Model blend"
            [value]="
              formatPct(f.model.weightHoltWinters) +
              ' / ' +
              formatPct(f.model.weightRidge)
            "
            hint="Holt-Winters / ridge"
          />
        </div>
        <section class="card">
          <div class="card-title">
            <h2>
              {{
                view() === 'daily'
                  ? 'Daily net cash flow'
                  : 'Cumulative net cash flow'
              }}
              ({{ f.currency }})
            </h2>
          </div>
          <app-line-band-chart
            [series]="series()"
            [history]="history()"
            ariaLabel="Cash forecast with confidence band"
          />
          <div class="legend">
            @if (view() === 'daily') {
              <span
                ><span class="dot" style="background: var(--muted)"></span>Last
                60 days actual</span
              >
            }
            <span
              ><span class="dot" style="background: var(--accent)"></span
              >Forecast</span
            >
            <span
              ><span class="dot" style="background: var(--accent-soft)"></span
              >{{ formatPct(f.confidence) }} interval from
              {{ f.model.calibrationOrigins }} calibration origins</span
            >
          </div>
        </section>
      }
      @if (backtest(); as b) {
        <section class="card" style="margin-top: 16px">
          <div class="card-title">
            <h2>
              Backtest, group scope ({{ b.origins }} rolling origins,
              {{ b.horizon }}-day horizon)
            </h2>
          </div>
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Measure</th>
                  <th class="num">Value</th>
                  <th>Reading</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td>MASE vs seasonal naive</td>
                  <td class="num">{{ b.mase.toFixed(2) }}</td>
                  <td class="muted">
                    Below 1.00 means the ensemble beats a seasonal-naive
                    forecast
                  </td>
                </tr>
                <tr>
                  <td>Daily MAE: ensemble</td>
                  <td class="num">{{ compact(b.maeEnsemble) }}</td>
                  <td class="muted">
                    Holt-Winters alone {{ compact(b.maeHoltWinters) }}, ridge
                    alone {{ compact(b.maeRidge) }}, seasonal naive
                    {{ compact(b.maeSeasonalNaive) }}
                  </td>
                </tr>
                <tr>
                  <td>Cumulative error at horizon</td>
                  <td class="num">{{ compact(b.cumulativeMaeEnsemble) }}</td>
                  <td class="muted">
                    Seasonal naive {{ compact(b.cumulativeMaeSeasonalNaive) }}
                  </td>
                </tr>
                <tr>
                  <td>Interval coverage</td>
                  <td class="num">{{ formatPct(b.intervalCoverage, 1) }}</td>
                  <td class="muted">
                    Nominal {{ formatPct(b.confidence) }}. Coverage below
                    nominal means intervals are somewhat optimistic
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>
      }
    </div>
  `,
})
export class ForecastPage {
  private readonly api = inject(ApiService);
  protected readonly currencies = ['USD', 'EUR', 'GBP', 'JPY', 'CHF'];
  protected readonly horizons = [30, 60, 91, 180];
  protected readonly confidences = [0.8, 0.9, 0.95];
  protected readonly scope = signal('all');
  protected readonly horizon = signal(91);
  protected readonly confidence = signal(0.9);
  protected readonly view = signal('cumulative');
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly forecast = signal<Forecast | null>(null);
  protected readonly backtest = signal<BacktestMetrics | null>(null);

  protected readonly money = formatMoney;
  protected readonly formatPct = formatPct;
  protected readonly compact = formatCompact;
  protected readonly value = valueOf;

  protected readonly series = computed<ChartPoint[]>(() => {
    const f = this.forecast();
    if (!f) return [];
    return this.view() === 'daily'
      ? f.daily.map((d) => ({
          label: d.date.slice(5),
          value: d.point,
          lo: d.lower,
          hi: d.upper,
        }))
      : f.daily.map((d) => ({
          label: d.date.slice(5),
          value: d.cumulative,
          lo: d.cumulativeLower,
          hi: d.cumulativeUpper,
        }));
  });

  protected readonly history = computed<ChartPoint[]>(() =>
    this.view() === 'daily'
      ? (this.forecast()?.history ?? []).map((h) => ({
          label: h.date.slice(5),
          value: h.net,
        }))
      : [],
  );

  constructor() {
    void this.load();
  }

  protected endPoint(f: Forecast): number {
    return f.daily[f.daily.length - 1]?.cumulative ?? 0;
  }
  protected endLower(f: Forecast): number {
    return f.daily[f.daily.length - 1]?.cumulativeLower ?? 0;
  }
  protected endUpper(f: Forecast): number {
    return f.daily[f.daily.length - 1]?.cumulativeUpper ?? 0;
  }

  protected reload(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [forecast, backtest] = await Promise.all([
        firstValueFrom(
          this.api.forecast(this.scope(), this.horizon(), this.confidence()),
        ),
        firstValueFrom(this.api.backtest('all', 28)),
      ]);
      this.forecast.set(forecast);
      this.backtest.set(backtest);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
