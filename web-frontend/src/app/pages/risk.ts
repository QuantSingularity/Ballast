import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BarItem, BarList } from '../components/bar-list';
import { KpiCard } from '../components/kpi-card';
import { PageHeader } from '../components/page-header';
import { StatusBadge } from '../components/status-badge';
import { ApiService } from '../services/api.service';
import { LimitResult, RiskMetrics, StressResult } from '../types/models';
import { errorMessage, formatCompact, formatMoney, formatNumber, formatPct } from '../utils/format';

@Component({
  selector: 'app-risk',
  imports: [PageHeader, KpiCard, StatusBadge, BarList],
  template: `
    <div class="page">
      <app-page-header
        title="Risk"
        subtitle="Cash at risk, FX value at risk, liquidity stress tests and policy limits"
      />
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (loading()) {
        <div class="loading">Running risk models...</div>
      } @else if (metrics(); as m) {
        <div class="grid kpis">
          <app-kpi-card
            label="Liquidity"
            [value]="money(m.liquidityBase, m.baseCurrency)"
            hint="Cash plus undrawn committed lines"
          />
          <app-kpi-card
            label="Liquidity coverage"
            [value]="m.liquidityCoverageRatio.toFixed(1) + 'x'"
            hint="vs trailing 30 day outflows"
            [tone]="
              m.liquidityCoverageRatio < 1 ? 'bad' : m.liquidityCoverageRatio < 2 ? 'warn' : 'good'
            "
          />
          <app-kpi-card
            label="FX VaR (95%, 21d)"
            [value]="money(m.fxVarTotal, m.baseCurrency)"
            hint="Sum across currencies, no diversification credit"
          />
          <app-kpi-card
            label="Limit breaches"
            [value]="'' + breaches()"
            [tone]="breaches() > 0 ? 'bad' : 'good'"
            [hint]="warnings() + ' warnings'"
          />
        </div>

        <div class="grid two">
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>Cash at risk (forecast shortfall)</h2>
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Horizon</th>
                    <th class="num">Confidence</th>
                    <th class="num">Cash at risk</th>
                    <th class="num">Expected shortfall</th>
                  </tr>
                </thead>
                <tbody>
                  @for (c of m.cashAtRisk; track c.horizonDays + '-' + c.confidence) {
                    <tr>
                      <td>{{ c.horizonDays }} days</td>
                      <td class="num">{{ pct(c.confidence) }}</td>
                      <td class="num">{{ money(c.cashAtRisk, m.baseCurrency) }}</td>
                      <td class="num">{{ money(c.expectedShortfall, m.baseCurrency) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>FX value at risk by currency</h2>
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Ccy</th>
                    <th class="num">Open 30d exposure</th>
                    <th class="num">VaR 95%</th>
                    <th class="num">ES 95%</th>
                    <th class="num">P(high vol)</th>
                  </tr>
                </thead>
                <tbody>
                  @for (v of m.fxVar; track v.currency) {
                    <tr>
                      <td>{{ v.currency }}</td>
                      <td class="num">{{ money(v.exposureBase, m.baseCurrency) }}</td>
                      <td class="num">{{ money(v.var95, m.baseCurrency) }}</td>
                      <td class="num">{{ money(v.expectedShortfall95, m.baseCurrency) }}</td>
                      <td class="num">{{ pct(v.highVolProbability) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        </div>

        <div class="grid two">
          <section class="card">
            <div class="card-title">
              <h2>Liquidity stress scenarios</h2>
              <span class="muted">Coverage after shock</span>
            </div>
            <app-bar-list [items]="stressBars()" />
            <div style="margin-top: 14px">
              @for (s of stress(); track s.id) {
                <p class="muted">
                  <strong>{{ s.name }}.</strong> {{ s.description }}. Impact
                  {{ money(s.liquidityImpactBase, m.baseCurrency) }}.
                </p>
              }
            </div>
          </section>
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0"><h2>Policy limits</h2></div>
            <div class="scroll">
              <table>
                <tbody>
                  @for (l of limits(); track l.id) {
                    <tr>
                      <td><app-status-badge [status]="l.status" /></td>
                      <td>
                        {{ l.name }}
                        <div class="muted">{{ l.detail }}</div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        </div>
      }
    </div>
  `,
})
export class RiskPage {
  private readonly api = inject(ApiService);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly metrics = signal<RiskMetrics | null>(null);
  protected readonly stress = signal<StressResult[]>([]);
  protected readonly limits = signal<LimitResult[]>([]);

  protected readonly money = formatMoney;
  protected readonly pct = formatPct;
  protected readonly breaches = computed(
    () => this.limits().filter((l) => l.status === 'Breach').length,
  );
  protected readonly warnings = computed(
    () => this.limits().filter((l) => l.status === 'Warning').length,
  );
  protected readonly stressBars = computed<BarItem[]>(() => {
    const rows = this.stress();
    const max = Math.max(1, ...rows.map((r) => r.coverageRatioAfter));
    return rows.map((r) => ({
      label: r.name,
      share: r.coverageRatioAfter / max,
      display: `${formatNumber(r.coverageRatioAfter, 1)}x, ${formatCompact(r.liquidityAfterBase)}`,
      tone: r.coverageRatioAfter < 1 ? 'bad' : r.coverageRatioAfter < 2 ? 'warn' : 'good',
    }));
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const [metrics, stress, limits] = await Promise.all([
        firstValueFrom(this.api.riskMetrics()),
        firstValueFrom(this.api.stress()),
        firstValueFrom(this.api.limits()),
      ]);
      this.metrics.set(metrics);
      this.stress.set(stress);
      this.limits.set(limits);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
