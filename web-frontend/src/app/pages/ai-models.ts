import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { KpiCard } from '../components/kpi-card';
import { PageHeader } from '../components/page-header';
import { ApiService } from '../services/api.service';
import { AiOverview, AnomalyItem } from '../types/models';
import { errorMessage, formatMoney, formatNumber, formatPct } from '../utils/format';

@Component({
  selector: 'app-ai-models',
  imports: [PageHeader, KpiCard],
  template: `
    <div class="page">
      <app-page-header
        title="AI models"
        subtitle="Models run inside the API process. They are trained at startup on the seeded history and are research-grade, not validated for production decisions."
      />
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (loading()) {
        <div class="loading">Loading model status...</div>
      } @else if (overview(); as o) {
        <div class="grid kpis">
          <app-kpi-card
            label="Forecast MASE"
            [value]="o.forecastBacktest.mase.toFixed(2)"
            hint="vs seasonal naive, lower is better"
            [tone]="o.forecastBacktest.mase < 1 ? 'good' : 'warn'"
          />
          <app-kpi-card
            label="Interval coverage"
            [value]="pct(o.forecastBacktest.intervalCoverage, 1)"
            [hint]="'Nominal ' + pct(o.forecastBacktest.confidence)"
          />
          <app-kpi-card
            label="Anomaly baseline"
            [value]="o.anomalyThreshold.toFixed(2)"
            hint="99.5th percentile of training scores"
          />
          <app-kpi-card
            label="RL training episodes"
            [value]="o.hedgingAgentEpisodes.toLocaleString('en-US')"
            hint="Hedging agent"
          />
        </div>
        <div class="grid three">
          @for (m of o.models; track m.name) {
            <section class="card">
              <h3>{{ m.kind }}</h3>
              <h2>
                {{ m.name }} <span class="muted">v{{ m.version }}</span>
              </h2>
              <p class="muted">{{ m.description }}</p>
            </section>
          }
        </div>
        <div class="grid two">
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>FX volatility regimes (hidden Markov model)</h2>
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Ccy</th>
                    <th class="num">Calm sigma</th>
                    <th class="num">Stress sigma</th>
                    <th class="num">Calm persistence</th>
                    <th class="num">Stress persistence</th>
                    <th class="num">P(high vol)</th>
                  </tr>
                </thead>
                <tbody>
                  @for (r of o.regimes; track r.currency) {
                    <tr>
                      <td>{{ r.currency }}</td>
                      <td class="num">{{ pct(r.sigmas[0] ?? 0, 2) }}</td>
                      <td class="num">{{ pct(r.sigmas[1] ?? 0, 2) }}</td>
                      <td class="num">{{ pct(r.calmPersistence) }}</td>
                      <td class="num">{{ pct(r.stressPersistence) }}</td>
                      <td class="num">{{ pct(r.highVolProbability) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>Flagged items (last 30 days of history plus open payments)</h2>
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Item</th>
                    <th>Counterparty</th>
                    <th class="num">Amount</th>
                    <th>Why</th>
                  </tr>
                </thead>
                <tbody>
                  @for (a of anomalies(); track a.reference) {
                    <tr>
                      <td class="mono">
                        {{ a.reference }}
                        <div class="muted">{{ a.source }}</div>
                      </td>
                      <td>{{ a.counterparty }}</td>
                      <td class="num">{{ money(a.amountBase, 'USD') }}</td>
                      <td class="muted">{{ a.reasons.join('; ') }}</td>
                    </tr>
                  } @empty {
                    <tr>
                      <td class="empty" colspan="4">Nothing flagged</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        </div>
        <section class="card">
          <div class="card-title"><h2>Forecast backtest detail</h2></div>
          <p class="muted">
            {{ o.forecastBacktest.origins }} rolling origins, {{ o.forecastBacktest.horizon }}-day
            horizon. Daily MAE: ensemble {{ num(o.forecastBacktest.maeEnsemble, 0) }}, Holt-Winters
            {{ num(o.forecastBacktest.maeHoltWinters, 0) }}, ridge
            {{ num(o.forecastBacktest.maeRidge, 0) }}, seasonal naive
            {{ num(o.forecastBacktest.maeSeasonalNaive, 0) }}.
          </p>
        </section>
      }
    </div>
  `,
})
export class AiModelsPage {
  private readonly api = inject(ApiService);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly overview = signal<AiOverview | null>(null);
  protected readonly anomalies = signal<AnomalyItem[]>([]);
  protected readonly pct = formatPct;
  protected readonly num = formatNumber;
  protected readonly money = formatMoney;

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const [overview, anomalies] = await Promise.all([
        firstValueFrom(this.api.aiOverview()),
        firstValueFrom(this.api.anomalies()),
      ]);
      this.overview.set(overview);
      this.anomalies.set(anomalies);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
