import { Component, inject, isDevMode, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { errorMessage, valueOf } from '../utils/format';

@Component({
  selector: 'app-login',
  template: `
    <div class="login-wrap">
      <form
        class="card login-card"
        (submit)="submit($event)"
        aria-labelledby="login-title"
      >
        <h1 id="login-title">Ballast<span class="muted">.</span></h1>
        <p class="subtitle">Treasury and liquidity management</p>
        @if (error()) {
          <div class="alert bad" role="alert">{{ error() }}</div>
        }
        <div class="grid" style="margin-top: 16px">
          <label>
            Email
            <input
              type="email"
              name="email"
              autocomplete="username"
              required
              [value]="email()"
              (input)="email.set(value($event))"
            />
          </label>
          <label>
            Password
            <input
              type="password"
              name="password"
              autocomplete="current-password"
              required
              [value]="password()"
              (input)="password.set(value($event))"
            />
          </label>
        </div>
        <button
          class="primary"
          type="submit"
          [disabled]="busy() || !email() || !password()"
          style="width: 100%"
        >
          {{ busy() ? 'Signing in...' : 'Sign in' }}
        </button>
        @if (dev) {
          <p class="muted" style="margin-top: 14px">
            Development build. Demo users such as treasurer&#64;ballast.example
            and approver&#64;ballast.example are seeded by the API.
          </p>
        }
      </form>
    </div>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dev = isDevMode();
  protected readonly value = valueOf;

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set(null);
    this.busy.set(true);
    try {
      await this.auth.login(this.email().trim(), this.password());
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
