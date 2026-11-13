import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BarItem, BarList } from '../components/bar-list';
import { KpiCard } from '../components/kpi-card';
import { LineBandChart } from '../components/line-band-chart';
import { PageHeader } from '../components/page-header';
import { StatusBadge } from '../components/status-badge';
import { ApiService } from '../services/api.service';
import {
  CashSummary,
  CovenantResult,
  Forecast,
  Ladder,
  LimitResult,
  Payment,
  RiskMetrics,
} from '../types/models';
import { ChartPoint } from '../utils/chart';
import { errorMessage, formatCompact, formatMoney, formatPct } from '../utils/format';

@Component({
  selector: 'app-dashboard',
  imports: [PageHeader, KpiCard, LineBandChart, BarList, StatusBadge, RouterLink],
  template: `
    <div class="page">
      <app-page-header title="Treasury dashboard" [subtitle]="subtitle()" />
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (loading()) {
        <div class="loading">Loading treasury data...</div>
      } @else if (summary(); as s) {
        <div class="grid kpis">
          <app-kpi-card
            label="Total cash"
            [value]="money(s.totalBase, s.baseCurrency)"
            [hint]="s.accountCount + ' accounts'"
          />
          <app-kpi-card
            label="Projected in 30 days"
            [value]="money(projected30(), s.baseCurrency)"
            hint="Ensemble forecast"
          />
          <app-kpi-card
            label="Liquidity coverage"
            [value]="coverage()"
            hint="Cash plus undrawn lines vs 30d outflows"
            [tone]="coverageTone()"
          />
          <app-kpi-card
            label="Cash at risk (95%, 30d)"
            [value]="money(car30(), s.baseCurrency)"
            hint="Forecast shortfall risk"
          />
          <app-kpi-card
            label="Covenant watch"
            [value]="covenantSummary().text"
            [tone]="covenantSummary().tone"
            hint="Facilities with headroom under 10%"
          />
          <app-kpi-card
            label="Awaiting approval"
            [value]="'' + pending().length"
            hint="Payments pending checker"
            [tone]="pending().length > 0 ? 'warn' : 'good'"
          />
        </div>

        <div class="grid two">
          <section class="card">
            <div class="card-title"><h2>Projected cash balance, next 91 days</h2></div>
            <app-line-band-chart
              [series]="chart()"
              ariaLabel="Projected group cash balance with confidence band"
            />
            <div class="legend">
              <span><span class="dot" style="background: var(--accent)"></span>Point forecast</span>
              <span
                ><span class="dot" style="background: var(--accent-soft)"></span>90% interval</span
              >
            </div>
          </section>
          <section class="card">
            <div class="card-title"><h2>Cash by currency</h2></div>
            <app-bar-list [items]="currencyBars()" />
            <div class="card-title" style="margin-top: 20px"><h2>Cash by bank</h2></div>
            <app-bar-list [items]="bankBars()" />
          </section>
        </div>

        <div class="grid two">
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0"><h2>Liquidity ladder</h2></div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Horizon</th>
                    <th class="num">Projected</th>
                    <th class="num">Lower</th>
                    <th class="num">Upper</th>
                  </tr>
                </thead>
                <tbody>
                  @for (b of ladder()?.buckets ?? []; track b.days) {
                    <tr>
                      <td>{{ b.label }}</td>
                      <td class="num">{{ money(b.projectedBalance, s.baseCurrency) }}</td>
                      <td class="num">{{ money(b.lower, s.baseCurrency) }}</td>
                      <td class="num">{{ money(b.upper, s.baseCurrency) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>Limits needing attention</h2>
              <a routerLink="/risk">All limits</a>
            </div>
            <div class="scroll">
              <table>
                <tbody>
                  @for (l of attention(); track l.id) {
                    <tr>
                      <td><app-status-badge [status]="l.status" /></td>
                      <td>
                        {{ l.name }}
                        <div class="muted">{{ l.detail }}</div>
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td class="empty" colspan="2">All limits are within policy</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        </div>

        @if (pending().length > 0) {
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>Payments awaiting approval</h2>
              <a routerLink="/payments">Open payments</a>
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Payment</th>
                    <th>Beneficiary</th>
                    <th class="num">Amount</th>
                    <th>Value date</th>
                    <th>Approvals</th>
                  </tr>
                </thead>
                <tbody>
                  @for (p of pending(); track p.id) {
                    <tr>
                      <td class="mono">{{ p.id }}</td>
                      <td>
                        {{ p.beneficiary }}
                        @if (p.riskFlags.length > 0) {
                          <app-status-badge status="Warning" text="Flagged" />
                        }
                      </td>
                      <td class="num">{{ money(p.amount, p.currency, 2) }}</td>
                      <td>{{ p.valueDate }}</td>
                      <td>{{ p.approvals.length }} of {{ p.requiredApprovals }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        }
      }
    </div>
  `,
})
export class DashboardPage {
  private readonly api = inject(ApiService);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly summary = signal<CashSummary | null>(null);
  protected readonly ladder = signal<Ladder | null>(null);
  protected readonly risk = signal<RiskMetrics | null>(null);
  protected readonly limits = signal<LimitResult[]>([]);
  protected readonly pending = signal<Payment[]>([]);
  protected readonly covenants = signal<CovenantResult[]>([]);
  protected readonly forecast = signal<Forecast | null>(null);

  protected readonly money = formatMoney;

  protected readonly subtitle = computed(() => {
    const s = this.summary();
    return s ? `As of ${s.asOf}. Reporting currency ${s.baseCurrency}.` : '';
  });

  protected readonly projected30 = computed(
    () => this.ladder()?.buckets.find((b) => b.days === 30)?.projectedBalance ?? 0,
  );
  protected readonly car30 = computed(
    () =>
      this.risk()?.cashAtRisk.find((c) => c.horizonDays === 30 && c.confidence === 0.95)
        ?.cashAtRisk ?? 0,
  );
  protected readonly coverage = computed(
    () => `${(this.risk()?.liquidityCoverageRatio ?? 0).toFixed(1)}x`,
  );
  protected readonly coverageTone = computed<'good' | 'warn' | 'bad'>(() => {
    const c = this.risk()?.liquidityCoverageRatio ?? 0;
    return c < 1 ? 'bad' : c < 2 ? 'warn' : 'good';
  });

  protected readonly covenantSummary = computed<{ text: string; tone: 'good' | 'warn' | 'bad' }>(
    () => {
      const all = this.covenants();
      const breach = all.filter((c) => c.status === 'Breach').length;
      const warn = all.filter((c) => c.status === 'Warning').length;
      if (breach > 0) return { text: `${breach} breach${breach > 1 ? 'es' : ''}`, tone: 'bad' };
      if (warn > 0) return { text: `${warn} warning${warn > 1 ? 's' : ''}`, tone: 'warn' };
      return { text: 'All clear', tone: 'good' };
    },
  );

  protected readonly attention = computed(() => this.limits().filter((l) => l.status !== 'Ok'));

  protected readonly chart = computed<ChartPoint[]>(() => {
    const f = this.forecast();
    const current = this.ladder()?.currentBalance ?? 0;
    return (f?.daily ?? []).map((d) => ({
      label: d.date.slice(5),
      value: current + d.cumulative,
      lo: current + d.cumulativeLower,
      hi: current + d.cumulativeUpper,
    }));
  });

  protected readonly currencyBars = computed<BarItem[]>(() =>
    (this.summary()?.byCurrency ?? []).map((c) => ({
      label: c.currency,
      share: c.share,
      display: `${formatCompact(c.base)} (${formatPct(c.share)})`,
    })),
  );

  protected readonly bankBars = computed<BarItem[]>(() =>
    (this.summary()?.byBank ?? []).slice(0, 6).map((b) => ({
      label: b.bank,
      share: b.share,
      display: formatPct(b.share, 1),
      tone: b.share > 0.3 ? 'warn' : undefined,
    })),
  );

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const [summary, ladder, risk, limits, pending, covenants, forecast] = await Promise.all([
        firstValueFrom(this.api.cashSummary()),
        firstValueFrom(this.api.ladder()),
        firstValueFrom(this.api.riskMetrics()),
        firstValueFrom(this.api.limits()),
        firstValueFrom(this.api.payments('PendingApproval')),
        firstValueFrom(this.api.covenants()),
        firstValueFrom(this.api.forecast('all', 91, 0.9)),
      ]);
      this.summary.set(summary);
      this.ladder.set(ladder);
      this.risk.set(risk);
      this.limits.set(limits);
      this.pending.set(pending);
      this.covenants.set(covenants);
      this.forecast.set(forecast);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
