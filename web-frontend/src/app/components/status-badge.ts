import { Component, computed, input } from '@angular/core';
import { statusTone } from '../utils/format';

@Component({
  selector: 'app-status-badge',
  template: `<span class="badge" [class]="'badge ' + tone()">{{
    label()
  }}</span>`,
})
export class StatusBadge {
  readonly status = input.required<string>();
  readonly text = input<string | null>(null);
  protected readonly tone = computed(() => statusTone(this.status()));
  protected readonly label = computed(
    () => this.text() ?? this.status().replace(/([a-z])([A-Z])/g, '$1 $2'),
  );
}
