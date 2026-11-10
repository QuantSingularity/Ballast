import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="shell">
      <aside class="sidebar">
        <div class="brand">Ballast<span>.</span></div>
        <nav class="nav" aria-label="Primary">
          @for (item of nav; track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active">{{ item.label }}</a>
          }
        </nav>
        @if (auth.user(); as user) {
          <div class="userbox">
            <div>{{ user.name }}</div>
            <div class="muted">{{ user.role }}</div>
            <button class="small" type="button" (click)="auth.logout()">Sign out</button>
          </div>
        }
      </aside>
      <main class="main"><router-outlet /></main>
    </div>
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly nav = [
    { path: '/dashboard', label: 'Dashboard' },
    { path: '/cash', label: 'Cash positions' },
    { path: '/forecast', label: 'Forecast' },
    { path: '/fx', label: 'FX and hedging' },
    { path: '/risk', label: 'Risk' },
    { path: '/inhouse-bank', label: 'In-house bank' },
    { path: '/debt', label: 'Debt and covenants' },
    { path: '/payments', label: 'Payments' },
    { path: '/ai', label: 'AI models' },
    { path: '/audit', label: 'Audit log' },
  ];
}
