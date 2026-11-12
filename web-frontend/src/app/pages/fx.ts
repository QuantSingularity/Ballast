import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PageHeader } from '../components/page-header';
import { StatusBadge } from '../components/status-badge';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import {
  Exposure,
  FxRate,
  Hedge,
  HedgeDirection,
  HedgeRecommendation,
} from '../types/models';
import {
  errorMessage,
  formatMoney,
  formatNumber,
  formatPct,
  valueOf,
} from '../utils/format';

@Component({
  selector: 'app-fx',
  imports: [PageHeader, StatusBadge],
  template: `
    <div class="page">
      <app-page-header
        title="FX exposure and hedging"
        subtitle="Rates are quoted in USD per unit of foreign currency. Forward points follow covered interest parity."
      />
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (notice()) {
        <div class="alert good" role="status">{{ notice() }}</div>
      }
      @if (loading()) {
        <div class="loading">Loading FX data...</div>
      } @else {
        <div class="grid two">
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>Rates and volatility regime</h2>
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Ccy</th>
                    <th class="num">Spot</th>
                    <th class="num">Rate</th>
                    <th class="num">Fwd 30d</th>
                    <th class="num">Fwd 90d</th>
                    <th class="num">P(high vol)</th>
                  </tr>
                </thead>
                <tbody>
                  @for (r of rates(); track r.currency) {
                    <tr>
                      <td>{{ r.currency }}</td>
                      <td class="num">{{ num(r.spotUsd, 4) }}</td>
                      <td class="num">{{ pct(r.interestRate, 2) }}</td>
                      <td class="num">{{ num(r.forward30, 4) }}</td>
                      <td class="num">{{ num(r.forward90, 4) }}</td>
                      <td class="num">
                        <app-status-badge
                          [status]="
                            r.highVolProbability >= 0.5 ? 'Warning' : 'Ok'
                          "
                          [text]="pct(r.highVolProbability)"
                        />
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>

          @if (auth.canMake()) {
            <section class="card">
              <div class="card-title"><h2>Book a forward</h2></div>
              <form (submit)="book($event)">
                <div class="form-grid">
                  <label
                    >Currency
                    <select
                      [value]="form.currency()"
                      (change)="form.currency.set(value($event))"
                    >
                      @for (c of ccys; track c) {
                        <option [value]="c">{{ c }}</option>
                      }
                    </select>
                  </label>
                  <label
                    >Direction
                    <select
                      [value]="form.direction()"
                      (change)="setDirection(value($event))"
                    >
                      <option value="SellForeign">
                        Sell foreign (hedge receivable)
                      </option>
                      <option value="BuyForeign">
                        Buy foreign (hedge payable)
                      </option>
                    </select>
                  </label>
                  <label
                    >Notional
                    <input
                      type="number"
                      min="1"
                      step="1"
                      [value]="form.notional()"
                      (input)="form.notional.set(+value($event))"
                    />
                  </label>
                  <label
                    >Tenor (days)
                    <input
                      type="number"
                      min="1"
                      max="365"
                      [value]="form.tenor()"
                      (input)="form.tenor.set(+value($event))"
                    />
                  </label>
                  <label
                    >Counterparty
                    <input
                      type="text"
                      [value]="form.counterparty()"
                      (input)="form.counterparty.set(value($event))"
                    />
                  </label>
                  <button
                    class="primary"
                    type="submit"
                    [disabled]="busy() || form.notional() <= 0"
                  >
                    Book at forward rate
                  </button>
                </div>
              </form>
            </section>
          }
        </div>

        <section class="card flush" style="margin-bottom: 16px">
          <div class="card-title" style="padding: 16px 18px 0">
            <h2>
              Forecast exposure by horizon ({{ exposure()?.baseCurrency }})
            </h2>
          </div>
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Ccy</th>
                  <th>Horizon</th>
                  <th class="num">Net forecast</th>
                  <th class="num">Hedged</th>
                  <th class="num">Hedge ratio</th>
                  <th class="num">Open ({{ exposure()?.baseCurrency }})</th>
                </tr>
              </thead>
              <tbody>
                @for (
                  l of exposure()?.lines ?? [];
                  track l.currency + l.bucket
                ) {
                  <tr>
                    <td>{{ l.currency }}</td>
                    <td>{{ l.bucket }}</td>
                    <td class="num">{{ money(l.netNative, l.currency) }}</td>
                    <td class="num">{{ money(l.hedgedNative, l.currency) }}</td>
                    <td class="num">{{ pct(l.hedgeRatio) }}</td>
                    <td class="num">
                      {{ money(l.openBase, exposure()?.baseCurrency ?? 'USD') }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>

        <section class="card flush" style="margin-bottom: 16px">
          <div class="card-title" style="padding: 16px 18px 0">
            <h2>Hedge recommendations</h2>
            <span class="muted"
              >Reinforcement learning policy, bounded by treasury policy
              bands</span
            >
          </div>
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Ccy</th>
                  <th>Horizon</th>
                  <th class="num">Current</th>
                  <th class="num">Target</th>
                  <th>Action</th>
                  <th>Rationale</th>
                </tr>
              </thead>
              <tbody>
                @for (r of actionable(); track r.currency + r.bucket) {
                  <tr>
                    <td>{{ r.currency }}</td>
                    <td>{{ r.bucket }}</td>
                    <td class="num">{{ pct(r.currentRatio) }}</td>
                    <td class="num">{{ pct(r.targetRatio) }}</td>
                    <td>
                      {{ r.direction === 'SellForeign' ? 'Sell' : 'Buy' }}
                      {{ money(r.recommendedNotional, r.currency) }}
                    </td>
                    <td class="muted">{{ r.rationale }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td class="empty" colspan="6">
                      Hedge coverage is at or above target in every bucket
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>

        <section class="card flush">
          <div class="card-title" style="padding: 16px 18px 0">
            <h2>Hedge book</h2>
          </div>
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Hedge</th>
                  <th>Ccy</th>
                  <th>Direction</th>
                  <th class="num">Notional</th>
                  <th class="num">Rate</th>
                  <th>Maturity</th>
                  <th class="num">MTM ({{ exposure()?.baseCurrency }})</th>
                  <th>Effectiveness</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (h of hedges(); track h.id) {
                  <tr>
                    <td class="mono">{{ h.id }}</td>
                    <td>{{ h.currency }}</td>
                    <td>
                      {{ h.direction === 'SellForeign' ? 'Sell' : 'Buy' }}
                    </td>
                    <td class="num">{{ money(h.notional, h.currency) }}</td>
                    <td class="num">{{ num(h.contractRate, 4) }}</td>
                    <td>{{ h.maturityDate }}</td>
                    <td
                      class="num"
                      [style.color]="
                        h.markToMarketBase < 0 ? 'var(--bad)' : 'var(--good)'
                      "
                    >
                      {{
                        money(
                          h.markToMarketBase,
                          exposure()?.baseCurrency ?? 'USD'
                        )
                      }}
                    </td>
                    <td>
                      <app-status-badge [status]="h.effectivenessStatus" />
                      @if (h.effectivenessRatio !== null) {
                        <span class="muted">
                          {{ pct(h.effectivenessRatio) }}</span
                        >
                      }
                    </td>
                    <td><app-status-badge [status]="h.status" /></td>
                    <td>
                      @if (auth.canMake() && h.status === 'Active') {
                        <button
                          class="small danger"
                          type="button"
                          [disabled]="busy()"
                          (click)="cancel(h)"
                        >
                          Cancel
                        </button>
                      }
                    </td>
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
export class FxPage {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);
  protected readonly ccys = ['EUR', 'GBP', 'JPY', 'CHF'];
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly rates = signal<FxRate[]>([]);
  protected readonly exposure = signal<Exposure | null>(null);
  protected readonly recommendations = signal<HedgeRecommendation[]>([]);
  protected readonly hedges = signal<Hedge[]>([]);
  protected readonly actionable = computed(() =>
    this.recommendations().filter(
      (r) => r.direction !== null && r.recommendedNotional > 0,
    ),
  );

  protected readonly form = {
    currency: signal('EUR'),
    direction: signal<HedgeDirection>('SellForeign'),
    notional: signal(100000),
    tenor: signal(60),
    counterparty: signal('Treasury desk'),
  };

  protected readonly money = formatMoney;
  protected readonly num = formatNumber;
  protected readonly pct = formatPct;
  protected readonly value = valueOf;

  constructor() {
    void this.load();
  }

  protected setDirection(v: string): void {
    this.form.direction.set(v as HedgeDirection);
  }

  protected async book(event: Event): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      const h = await firstValueFrom(
        this.api.bookHedge({
          currency: this.form.currency(),
          direction: this.form.direction(),
          notional: this.form.notional(),
          tenorDays: this.form.tenor(),
          counterparty: this.form.counterparty(),
        }),
      );
      this.notice.set(`Booked ${h.id} at ${formatNumber(h.contractRate, 4)}`);
    });
  }

  protected async cancel(h: Hedge): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.cancelHedge(h.id));
      this.notice.set(`Cancelled ${h.id}`);
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await action();
      await this.load();
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [rates, exposure, recs, hedges] = await Promise.all([
        firstValueFrom(this.api.fxRates()),
        firstValueFrom(this.api.exposures()),
        firstValueFrom(this.api.recommendations()),
        firstValueFrom(this.api.hedges()),
      ]);
      this.rates.set(rates);
      this.exposure.set(exposure);
      this.recommendations.set(recs);
      this.hedges.set(hedges);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
