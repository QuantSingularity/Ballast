import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PageHeader } from '../components/page-header';
import { StatusBadge } from '../components/status-badge';
import { ApiService, PaymentAction } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { Account, AiAssessment, Payment } from '../types/models';
import { errorMessage, formatMoney, todayPlus, valueOf } from '../utils/format';

@Component({
  selector: 'app-payments',
  imports: [PageHeader, StatusBadge],
  template: `
    <div class="page">
      <app-page-header
        title="Payments"
        subtitle="Maker-checker workflow with AI screening, dual approval above the configured threshold, and an audit trail"
      >
        <label
          >Status
          <select [value]="status()" (change)="status.set(value($event)); reload()">
            <option value="">All</option>
            @for (s of statuses; track s) {
              <option [value]="s">{{ s }}</option>
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

      @if (auth.canMake()) {
        <section class="card" style="margin-bottom: 16px">
          <div class="card-title"><h2>New payment</h2></div>
          <form (submit)="create($event)">
            <div class="form-grid">
              <label
                >Debit account
                <select [value]="form.accountId()" (change)="form.accountId.set(value($event))">
                  @for (a of accounts(); track a.id) {
                    <option [value]="a.id">{{ a.id }} ({{ a.currency }}, {{ a.bank }})</option>
                  }
                </select>
              </label>
              <label
                >Beneficiary
                <input
                  type="text"
                  required
                  [value]="form.beneficiary()"
                  (input)="form.beneficiary.set(value($event))"
                />
              </label>
              <label
                >Amount
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  required
                  [value]="form.amount()"
                  (input)="form.amount.set(+value($event))"
                />
              </label>
              <label
                >Reference
                <input
                  type="text"
                  [value]="form.reference()"
                  (input)="form.reference.set(value($event))"
                />
              </label>
              <label
                >Value date
                <input
                  type="date"
                  required
                  [value]="form.valueDate()"
                  (input)="form.valueDate.set(value($event))"
                />
              </label>
              <div class="row">
                <button type="button" [disabled]="busy() || !canSubmitForm()" (click)="assess()">
                  Screen
                </button>
                <button class="primary" type="submit" [disabled]="busy() || !canSubmitForm()">
                  Create draft
                </button>
              </div>
            </div>
          </form>
          @if (assessment(); as a) {
            <div
              class="alert"
              [class]="'alert ' + (a.flagged ? 'warn' : 'good')"
              style="margin: 14px 0 0"
              role="status"
            >
              <strong>{{ a.flagged ? 'Flagged for review' : 'Passes screening' }}</strong> (anomaly
              score {{ a.score.toFixed(2) }}, baseline {{ a.threshold.toFixed(2) }})
              @if (a.reasons.length > 0) {
                <ul class="flags">
                  @for (r of a.reasons; track r) {
                    <li>{{ r }}</li>
                  }
                </ul>
              }
            </div>
          }
        </section>
      }

      @if (loading()) {
        <div class="loading">Loading payments...</div>
      } @else {
        <section class="card flush">
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>Payment</th>
                  <th>Beneficiary</th>
                  <th class="num">Amount</th>
                  <th>Value date</th>
                  <th>Status</th>
                  <th>Approvals</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (p of payments(); track p.id) {
                  <tr>
                    <td class="mono">
                      {{ p.id }}
                      <div class="muted">{{ p.accountId }}</div>
                    </td>
                    <td>
                      {{ p.beneficiary }}
                      <div class="muted">{{ p.reference }}</div>
                      @if (p.riskFlags.length > 0) {
                        <ul class="flags">
                          @for (f of p.riskFlags; track f) {
                            <li>{{ f }}</li>
                          }
                        </ul>
                      }
                      @if (p.rejectionReason) {
                        <div class="muted">Rejected: {{ p.rejectionReason }}</div>
                      }
                    </td>
                    <td class="num">{{ money(p.amount, p.currency, 2) }}</td>
                    <td>{{ p.valueDate }}</td>
                    <td><app-status-badge [status]="p.status" /></td>
                    <td>
                      {{ p.approvals.length }} of {{ p.requiredApprovals }}
                      @for (a of p.approvals; track a.userId) {
                        <div class="muted">{{ a.userName }}</div>
                      }
                    </td>
                    <td>
                      <div class="row">
                        @if (canSubmit(p)) {
                          <button
                            class="small"
                            type="button"
                            [disabled]="busy()"
                            (click)="act(p, 'submit')"
                          >
                            Submit
                          </button>
                        }
                        @if (canApprove(p)) {
                          <button
                            class="small primary"
                            type="button"
                            [disabled]="busy()"
                            (click)="act(p, 'approve')"
                          >
                            Approve
                          </button>
                        }
                        @if (canReject(p)) {
                          <button
                            class="small danger"
                            type="button"
                            [disabled]="busy()"
                            (click)="startReject(p)"
                          >
                            Reject
                          </button>
                        }
                        @if (canRelease(p)) {
                          <button
                            class="small primary"
                            type="button"
                            [disabled]="busy()"
                            (click)="act(p, 'release')"
                          >
                            Release
                          </button>
                        }
                      </div>
                      @if (rejecting() === p.id) {
                        <div class="row" style="margin-top: 6px">
                          <input
                            type="text"
                            placeholder="Reason"
                            [value]="reason()"
                            (input)="reason.set(value($event))"
                            aria-label="Rejection reason"
                          />
                          <button
                            class="small danger"
                            type="button"
                            [disabled]="busy() || !reason().trim()"
                            (click)="confirmReject(p)"
                          >
                            Confirm
                          </button>
                        </div>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td class="empty" colspan="7">No payments found</td>
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
export class PaymentsPage {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);
  protected readonly statuses = ['Draft', 'PendingApproval', 'Approved', 'Released', 'Rejected'];
  protected readonly status = signal('');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly payments = signal<Payment[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly assessment = signal<AiAssessment | null>(null);
  protected readonly rejecting = signal<string | null>(null);
  protected readonly reason = signal('');

  protected readonly form = {
    accountId: signal(''),
    beneficiary: signal(''),
    amount: signal(0),
    reference: signal(''),
    valueDate: signal(todayPlus(2)),
  };

  protected readonly money = formatMoney;
  protected readonly value = valueOf;
  protected readonly canSubmitForm = computed(
    () =>
      !!this.form.accountId() &&
      this.form.beneficiary().trim().length > 0 &&
      this.form.amount() > 0 &&
      !!this.form.valueDate(),
  );

  constructor() {
    void this.init();
  }

  private get userId(): string {
    return this.auth.user()?.id ?? '';
  }

  protected canSubmit(p: Payment): boolean {
    return this.auth.canMake() && p.status === 'Draft';
  }

  protected canApprove(p: Payment): boolean {
    return (
      this.auth.canCheck() &&
      p.status === 'PendingApproval' &&
      p.createdBy !== this.userId &&
      !p.approvals.some((a) => a.userId === this.userId)
    );
  }

  protected canReject(p: Payment): boolean {
    return this.auth.canCheck() && p.status === 'PendingApproval';
  }

  protected canRelease(p: Payment): boolean {
    return (
      this.auth.canMake() &&
      p.status === 'Approved' &&
      !p.approvals.some((a) => a.userId === this.userId)
    );
  }

  protected reload(): void {
    void this.refresh();
  }

  protected async assess(): Promise<void> {
    await this.run(async () => {
      this.assessment.set(
        await firstValueFrom(
          this.api.assessPayment({
            accountId: this.form.accountId(),
            beneficiary: this.form.beneficiary().trim(),
            amount: this.form.amount(),
            valueDate: this.form.valueDate(),
          }),
        ),
      );
    }, false);
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      const p = await firstValueFrom(
        this.api.createPayment({
          accountId: this.form.accountId(),
          beneficiary: this.form.beneficiary().trim(),
          amount: this.form.amount(),
          reference: this.form.reference().trim(),
          valueDate: this.form.valueDate(),
        }),
      );
      this.notice.set(
        `Created ${p.id}, requires ${p.requiredApprovals} approval${p.requiredApprovals > 1 ? 's' : ''}`,
      );
      this.assessment.set(null);
      this.form.beneficiary.set('');
      this.form.amount.set(0);
      this.form.reference.set('');
    });
  }

  protected async act(p: Payment, action: PaymentAction): Promise<void> {
    await this.run(async () => {
      const updated = await firstValueFrom(this.api.paymentAction(p.id, action));
      this.notice.set(`${updated.id} is now ${updated.status}`);
    });
  }

  protected startReject(p: Payment): void {
    this.rejecting.set(this.rejecting() === p.id ? null : p.id);
    this.reason.set('');
  }

  protected async confirmReject(p: Payment): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.paymentAction(p.id, 'reject', this.reason().trim()));
      this.notice.set(`${p.id} rejected`);
      this.rejecting.set(null);
    });
  }

  private async run(action: () => Promise<void>, refresh = true): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await action();
      if (refresh) await this.refresh();
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async refresh(): Promise<void> {
    try {
      this.payments.set(await firstValueFrom(this.api.payments(this.status() || undefined)));
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  private async init(): Promise<void> {
    try {
      const accounts = await firstValueFrom(this.api.accounts());
      this.accounts.set(accounts);
      this.form.accountId.set(
        accounts.find((a) => a.kind === 'Operating')?.id ?? accounts[0]?.id ?? '',
      );
    } catch (e) {
      this.error.set(errorMessage(e));
    }
    await this.refresh();
  }
}
