import { Component, computed, input } from '@angular/core';

export interface BarItem {
  label: string;
  share: number;
  display: string;
  tone?: 'good' | 'warn' | 'bad';
}

@Component({
  selector: 'app-bar-list',
  template: `
    <div class="bars">
      @for (item of normalized(); track item.label) {
        <div class="bar-row">
          <span>{{ item.label }}</span>
          <div class="bar-track" role="presentation">
            <div
              class="bar-fill"
              [class]="'bar-fill ' + (item.tone ?? '')"
              [style.width.%]="item.width"
            ></div>
          </div>
          <span class="right">{{ item.display }}</span>
        </div>
      } @empty {
        <div class="empty">No data</div>
      }
    </div>
  `,
})
export class BarList {
  readonly items = input<BarItem[]>([]);
  protected readonly normalized = computed(() =>
    this.items().map((i) => ({
      ...i,
      width: Math.max(0, Math.min(100, i.share * 100)),
    })),
  );
}
