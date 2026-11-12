import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { KpiCard } from '../components/kpi-card';
import { PageHeader } from '../components/page-header';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import {
  IntercompanyBalance,
  NettingCycle,
  NettingResult,
} from '../types/models';
import { errorMessage, formatMoney, formatPct, valueOf } from '../utils/format';

@Component({
  selector: 'app-inhouse-bank',
  imports: [PageHeader, KpiCard],
  template: `
    <div class="page">
      <app-page-header
        title="In-house bank"
        subtitle="Intercompany balances and multilateral netting"
      >
        <label
          >Settlement currency
          <select
            [value]="currency()"
            (change)="currency.set(value($event)); preview()"
          >
            @for (c of ccys; track c) {
              <option [value]="c">{{ c }}</option>
            }
          </select>
        </label>
      </app-page-header>
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (notice()) {
        <div class="alert good" role="status">{{ notice() }}</div>
      }
      @if (loading()) {
        <div class="loading">Loading intercompany positions...</div>
      } @else {
        @if (result(); as r) {
          <div class="grid kpis">
            <app-kpi-card
              label="Gross intercompany volume"
              [value]="money(r.grossVolume, r.settlementCurrency)"
            />
            <app-kpi-card
              label="Net settlement volume"
              [value]="money(r.netVolume, r.settlementCurrency)"
            />
            <app-kpi-card
              label="Volume reduction"
              [value]="pct(r.reductionPct, 1)"
              tone="good"
              [hint]="
                r.transfers.length +
                ' transfers replace ' +
                balances().length +
                ' balances'
              "
            />
          </div>
        }
        <div class="grid two">
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>Open intercompany balances</h2>
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Lender</th>
                    <th>Borrower</th>
                    <th class="num">Amount</th>
                  </tr>
                </thead>
                <tbody>
                  @for (b of balances(); track b.id) {
                    <tr>
                      <td>{{ b.lenderName }}</td>
                      <td>{{ b.borrowerName }}</td>
                      <td class="num">{{ money(b.amount, b.currency) }}</td>
                    </tr>
                  } @empty {
                    <tr>
                      <td class="empty" colspan="3">
                        No open intercompany balances
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
          <section class="card flush">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>Netting settlement instructions</h2>
              @if (auth.canMake() && balances().length > 0) {
                <button
                  class="primary small"
                  type="button"
                  [disabled]="busy()"
                  (click)="run()"
                >
                  Run netting cycle
                </button>
              }
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Pays</th>
                    <th>Receives</th>
                    <th class="num">Amount</th>
                  </tr>
                </thead>
                <tbody>
                  @for (
                    t of result()?.transfers ?? [];
                    track t.fromEntityId + t.toEntityId
                  ) {
                    <tr>
                      <td>{{ t.fromEntityId }}</td>
                      <td>{{ t.toEntityId }}</td>
                      <td class="num">
                        {{
                          money(t.amount, result()?.settlementCurrency ?? 'USD')
                        }}
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td class="empty" colspan="3">Nothing to settle</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        </div>
        <section class="card flush">
          <div class="card-title" style="padding: 16px 18px 0">
            <h2>Completed netting cycles</h2>
          </div>
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Cycle</th>
                  <th>Run at</th>
                  <th>Run by</th>
                  <th class="num">Gross</th>
                  <th class="num">Net</th>
                  <th class="num">Transfers</th>
                </tr>
              </thead>
              <tbody>
                @for (c of cycles(); track c.id) {
                  <tr>
                    <td class="mono">{{ c.id }}</td>
                    <td>{{ c.runAt.slice(0, 16).replace('T', ' ') }}</td>
                    <td>{{ c.runBy }}</td>
                    <td class="num">
                      {{ money(c.grossVolume, c.settlementCurrency) }}
                    </td>
                    <td class="num">
                      {{ money(c.netVolume, c.settlementCurrency) }}
                    </td>
                    <td class="num">{{ c.transfers.length }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td class="empty" colspan="6">
                      No netting cycles have been run
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
export class InHouseBankPage {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);
  protected readonly ccys = ['USD', 'EUR', 'GBP', 'JPY', 'CHF'];
  protected readonly currency = signal('USD');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly balances = signal<IntercompanyBalance[]>([]);
  protected readonly result = signal<NettingResult | null>(null);
  protected readonly cycles = signal<NettingCycle[]>([]);

  protected readonly money = formatMoney;
  protected readonly pct = formatPct;
  protected readonly value = valueOf;

  constructor() {
    void this.load();
  }

  protected preview(): void {
    void this.load();
  }

  protected async run(): Promise<void> {
    if (
      !confirm(
        'Run a netting cycle? Intercompany balances will be settled and cleared.',
      )
    )
      return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const cycle = await firstValueFrom(this.api.runNetting(this.currency()));
      this.notice.set(
        `Cycle ${cycle.id} completed with ${cycle.transfers.length} transfers`,
      );
      await this.load();
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [balances, result, cycles] = await Promise.all([
        firstValueFrom(this.api.icBalances()),
        firstValueFrom(this.api.nettingPreview(this.currency())),
        firstValueFrom(this.api.nettingCycles()),
      ]);
      this.balances.set(balances);
      this.result.set(result);
      this.cycles.set(cycles);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
