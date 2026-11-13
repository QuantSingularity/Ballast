import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthResponse, User, UserRole } from '../types/models';

const ACCESS_KEY = 'ballast.access';
const REFRESH_KEY = 'ballast.refresh';
const USER_KEY = 'ballast.user';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private refreshing: Promise<boolean> | null = null;

  readonly user = signal<User | null>(this.readUser());
  readonly isAuthenticated = computed(() => this.user() !== null && this.accessToken() !== null);
  readonly canMake = computed(() => this.hasRole('Treasurer', 'Admin'));
  readonly canCheck = computed(() => this.hasRole('Approver', 'Admin'));
  readonly isAdmin = computed(() => this.hasRole('Admin'));

  accessToken(): string | null {
    return sessionStorage.getItem(ACCESS_KEY);
  }
  refreshToken(): string | null {
    return sessionStorage.getItem(REFRESH_KEY);
  }

  hasRole(...roles: UserRole[]): boolean {
    const u = this.user();
    return u !== null && roles.includes(u.role);
  }

  async login(email: string, password: string): Promise<User> {
    const res = await firstValueFrom(
      this.http.post<AuthResponse>('/api/auth/login', { email, password }),
    );
    this.store(res);
    return res.user;
  }

  async refresh(): Promise<boolean> {
    const token = this.refreshToken();
    if (!token) return false;
    this.refreshing ??= firstValueFrom(
      this.http.post<AuthResponse>('/api/auth/refresh', { refreshToken: token }),
    )
      .then((res) => {
        this.store(res);
        return true;
      })
      .catch(() => false)
      .finally(() => {
        this.refreshing = null;
      });
    return this.refreshing;
  }

  logout(navigate = true): void {
    sessionStorage.removeItem(ACCESS_KEY);
    sessionStorage.removeItem(REFRESH_KEY);
    sessionStorage.removeItem(USER_KEY);
    this.user.set(null);
    if (navigate) void this.router.navigateByUrl('/login');
  }

  private store(res: AuthResponse): void {
    sessionStorage.setItem(ACCESS_KEY, res.accessToken);
    sessionStorage.setItem(REFRESH_KEY, res.refreshToken);
    sessionStorage.setItem(USER_KEY, JSON.stringify(res.user));
    this.user.set(res.user);
  }

  private readUser(): User | null {
    try {
      const raw = sessionStorage.getItem(USER_KEY);
      return raw ? (JSON.parse(raw) as User) : null;
    } catch {
      return null;
    }
  }
}
