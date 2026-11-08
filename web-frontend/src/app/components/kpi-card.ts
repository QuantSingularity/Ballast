import { Component, input } from '@angular/core';

@Component({
  selector: 'app-kpi-card',
  template: `
    <div class="card kpi" [class]="'card kpi ' + tone()">
      <div class="kpi-label">{{ label() }}</div>
      <div class="kpi-value">{{ value() }}</div>
      @if (hint()) {
        <div class="kpi-hint">{{ hint() }}</div>
      }
    </div>
  `,
})
export class KpiCard {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly hint = input<string>('');
  readonly tone = input<'good' | 'warn' | 'bad' | 'neutral'>('neutral');
}
