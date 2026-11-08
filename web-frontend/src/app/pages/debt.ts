import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { KpiCard } from '../components/kpi-card';
import { PageHeader } from '../components/page-header';
import { StatusBadge } from '../components/status-badge';
import { ApiService } from '../services/api.service';
import { CovenantResult, DebtSummary } from '../types/models';
import {
  errorMessage,
  formatMoney,
  formatNumber,
  formatPct,
} from '../utils/format';

@Component({
  selector: 'app-debt',
  imports: [PageHeader, KpiCard, StatusBadge],
  template: `
    <div class="page">
      <app-page-header
        title="Debt and covenants"
        subtitle="EBITDA is a proxy: trailing 12 month receipts less supplier, payroll, rent and fee outflows"
      />
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (loading()) {
        <div class="loading">Loading facilities...</div>
      } @else if (summary(); as s) {
        <div class="grid kpis">
          <app-kpi-card
            label="Total debt drawn"
            [value]="money(s.totalDebtBase, s.baseCurrency)"
          />
          <app-kpi-card
            label="Net debt"
            [value]="money(s.netDebtBase, s.baseCurrency)"
          />
          <app-kpi-card
            label="EBITDA proxy (12m)"
            [value]="money(s.ebitdaProxyBase, s.baseCurrency)"
          />
          <app-kpi-card
            label="Annual interest"
            [value]="money(s.annualInterestBase, s.baseCurrency)"
          />
          <app-kpi-card
            label="Liquidity"
            [value]="money(s.liquidityBase, s.baseCurrency)"
            hint="Cash plus undrawn committed"
          />
        </div>
        <section class="card flush" style="margin-bottom: 16px">
          <div class="card-title" style="padding: 16px 18px 0">
            <h2>Facilities</h2>
          </div>
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Facility</th>
                  <th>Entity</th>
                  <th>Ccy</th>
                  <th class="num">Commitment</th>
                  <th class="num">Drawn</th>
                  <th class="num">Utilization</th>
                  <th class="num">Rate</th>
                  <th>Type</th>
                  <th>Maturity</th>
                </tr>
              </thead>
              <tbody>
                @for (f of s.facilities; track f.id) {
                  <tr>
                    <td>{{ f.name }}</td>
                    <td>{{ f.entityId }}</td>
                    <td>{{ f.currency }}</td>
                    <td class="num">{{ money(f.commitment, f.currency) }}</td>
                    <td class="num">{{ money(f.drawn, f.currency) }}</td>
                    <td class="num">{{ pct(f.utilization) }}</td>
                    <td class="num">{{ pct(f.annualRate, 2) }}</td>
                    <td>{{ f.floating ? 'Floating' : 'Fixed' }}</td>
                    <td>{{ f.maturity }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
        <section class="card flush">
          <div class="card-title" style="padding: 16px 18px 0">
            <h2>Covenant compliance</h2>
          </div>
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Facility</th>
                  <th>Covenant</th>
                  <th class="num">Actual</th>
                  <th class="num">Threshold</th>
                  <th class="num">Headroom</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                @for (c of covenants(); track c.covenantId) {
                  <tr>
                    <td>{{ c.facilityName }}</td>
                    <td>{{ c.name }}</td>
                    <td class="num">{{ display(c, c.actual) }}</td>
                    <td class="num">{{ display(c, c.threshold) }}</td>
                    <td class="num">{{ pct(c.headroom, 1) }}</td>
                    <td><app-status-badge [status]="c.status" /></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    </div>
  `,
})
export class DebtPage {
  private readonly api = inject(ApiService);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly summary = signal<DebtSummary | null>(null);
  protected readonly covenants = signal<CovenantResult[]>([]);
  protected readonly money = formatMoney;
  protected readonly pct = formatPct;

  constructor() {
    void this.load();
  }

  protected display(c: CovenantResult, v: number): string {
    return c.kind === 'MinLiquidity'
      ? formatMoney(v, this.summary()?.baseCurrency ?? 'USD')
      : `${formatNumber(v, 2)}x`;
  }

  private async load(): Promise<void> {
    try {
      const s = await firstValueFrom(this.api.debtSummary());
      this.summary.set(s);
      this.covenants.set(s.covenants);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
