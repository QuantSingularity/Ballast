import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PageHeader } from '../components/page-header';
import { ApiService } from '../services/api.service';
import { Account, CashSummary, Transaction } from '../types/models';
import { errorMessage, formatMoney, formatPct, valueOf } from '../utils/format';

@Component({
  selector: 'app-cash',
  imports: [PageHeader],
  template: `
    <div class="page">
      <app-page-header title="Cash positions" [subtitle]="subtitle()">
        <label>
          Currency
          <select
            [value]="currency()"
            (change)="currency.set(value($event))"
            aria-label="Filter by currency"
          >
            <option value="">All currencies</option>
            @for (c of currencies(); track c) {
              <option [value]="c">{{ c }}</option>
            }
          </select>
        </label>
      </app-page-header>
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (loading()) {
        <div class="loading">Loading accounts...</div>
      } @else {
        <section class="card flush">
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Account</th>
                  <th>Entity</th>
                  <th>Bank</th>
                  <th>Type</th>
                  <th>Ccy</th>
                  <th class="num">Balance</th>
                  <th class="num">Balance ({{ base() }})</th>
                </tr>
              </thead>
              <tbody>
                @for (a of filtered(); track a.id) {
                  <tr
                    class="clickable"
                    [class.selected]="selected()?.id === a.id"
                    (click)="select(a)"
                    tabindex="0"
                    (keydown.enter)="select(a)"
                  >
                    <td class="mono">{{ a.id }}</td>
                    <td>{{ a.entityName }}</td>
                    <td>{{ a.bank }}</td>
                    <td>{{ a.kind }}</td>
                    <td>{{ a.currency }}</td>
                    <td class="num">{{ money(a.balance, a.currency) }}</td>
                    <td class="num">{{ money(a.balanceBase, base()) }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td class="empty" colspan="7">
                      No accounts match the filter
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>

        @if (selected(); as acc) {
          <section class="card flush" style="margin-top: 16px">
            <div class="card-title" style="padding: 16px 18px 0">
              <h2>Recent transactions: {{ acc.id }}</h2>
              <span class="muted">{{ acc.iban }}</span>
            </div>
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Category</th>
                    <th>Counterparty</th>
                    <th class="num">Amount</th>
                  </tr>
                </thead>
                <tbody>
                  @for (t of transactions(); track t.id) {
                    <tr>
                      <td>{{ t.date }}</td>
                      <td>{{ t.category }}</td>
                      <td>{{ t.counterparty }}</td>
                      <td
                        class="num"
                        [style.color]="
                          t.amount < 0 ? 'var(--bad)' : 'var(--good)'
                        "
                      >
                        {{ money(t.amount, t.currency, 2) }}
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td class="empty" colspan="4">
                        No transactions in the last 30 days (non-operating
                        account)
                      </td>
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
export class CashPage {
  private readonly api = inject(ApiService);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly summary = signal<CashSummary | null>(null);
  protected readonly currency = signal('');
  protected readonly selected = signal<Account | null>(null);
  protected readonly transactions = signal<Transaction[]>([]);

  protected readonly money = formatMoney;
  protected readonly value = valueOf;
  protected readonly base = computed(
    () => this.summary()?.baseCurrency ?? 'USD',
  );
  protected readonly currencies = computed(() =>
    [...new Set(this.accounts().map((a) => a.currency))].sort(),
  );
  protected readonly filtered = computed(() =>
    this.accounts().filter(
      (a) => !this.currency() || a.currency === this.currency(),
    ),
  );
  protected readonly subtitle = computed(() => {
    const s = this.summary();
    return s
      ? `${s.accountCount} accounts, ${formatMoney(s.totalBase, s.baseCurrency)} in total (largest bank ${formatPct(s.byBank[0]?.share ?? 0, 1)})`
      : '';
  });

  constructor() {
    void this.load();
  }

  protected async select(a: Account): Promise<void> {
    this.selected.set(a);
    try {
      this.transactions.set(
        await firstValueFrom(this.api.transactions(a.id, 30, 50)),
      );
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }

  private async load(): Promise<void> {
    try {
      const [accounts, summary] = await Promise.all([
        firstValueFrom(this.api.accounts()),
        firstValueFrom(this.api.cashSummary()),
      ]);
      this.accounts.set(accounts);
      this.summary.set(summary);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
