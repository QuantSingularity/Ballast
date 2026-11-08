import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PageHeader } from '../components/page-header';
import { StatusBadge } from '../components/status-badge';
import { ApiService } from '../services/api.service';
import { AuditEntry, AuditVerification } from '../types/models';
import { errorMessage } from '../utils/format';

@Component({
  selector: 'app-audit',
  imports: [PageHeader, StatusBadge],
  template: `
    <div class="page">
      <app-page-header
        title="Audit log"
        subtitle="Hash-chained, append-only record of authentication, payment, hedge and netting events"
      >
        @if (verification(); as v) {
          <app-status-badge
            [status]="v.valid ? 'Ok' : 'Breach'"
            [text]="
              v.valid
                ? 'Chain valid, ' + v.entries + ' entries'
                : 'Chain broken at #' + v.brokenAtSequence
            "
          />
        }
        <button type="button" [disabled]="busy()" (click)="verify()">
          Verify chain
        </button>
      </app-page-header>
      @if (error()) {
        <div class="alert bad" role="alert">{{ error() }}</div>
      }
      @if (loading()) {
        <div class="loading">Loading audit entries...</div>
      } @else {
        <section class="card flush">
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th class="num">#</th>
                  <th>Time (UTC)</th>
                  <th>Actor</th>
                  <th>Action</th>
                  <th>Subject</th>
                  <th>Detail</th>
                  <th>Hash</th>
                </tr>
              </thead>
              <tbody>
                @for (e of entries(); track e.sequence) {
                  <tr>
                    <td class="num">{{ e.sequence }}</td>
                    <td>{{ e.timestamp.slice(0, 19).replace('T', ' ') }}</td>
                    <td>{{ e.actor }}</td>
                    <td class="mono">{{ e.action }}</td>
                    <td class="mono">{{ e.subject }}</td>
                    <td>{{ e.detail }}</td>
                    <td class="mono muted">{{ e.hash.slice(0, 10) }}</td>
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
export class AuditPage {
  private readonly api = inject(ApiService);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly verification = signal<AuditVerification | null>(null);

  constructor() {
    void this.load();
  }

  protected async verify(): Promise<void> {
    this.busy.set(true);
    try {
      this.verification.set(await firstValueFrom(this.api.verifyAudit()));
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.entries.set(await firstValueFrom(this.api.audit(100)));
      this.verification.set(await firstValueFrom(this.api.verifyAudit()));
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
