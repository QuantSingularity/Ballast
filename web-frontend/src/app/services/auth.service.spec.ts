import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { users } from '../testing/fixtures';
import { AuthService } from './auth.service';

const response = (role: 'Treasurer' | 'Admin' = 'Treasurer') => ({
  user: { ...users['treasurer']!, role },
  accessToken: 'a1',
  refreshToken: 'r1',
  expiresIn: 1800,
});

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('stores tokens and user after login and derives role flags', async () => {
    const promise = auth.login('t@x.com', 'pw');
    http.expectOne('/api/auth/login').flush(response());
    await promise;
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessToken()).toBe('a1');
    expect(auth.canMake()).toBe(true);
    expect(auth.canCheck()).toBe(false);
    expect(auth.isAdmin()).toBe(false);
  });

  it('treats admins as both maker and checker', async () => {
    const promise = auth.login('a@x.com', 'pw');
    http.expectOne('/api/auth/login').flush(response('Admin'));
    await promise;
    expect(auth.canMake() && auth.canCheck() && auth.isAdmin()).toBe(true);
  });

  it('clears everything on logout', async () => {
    const promise = auth.login('t@x.com', 'pw');
    http.expectOne('/api/auth/login').flush(response());
    await promise;
    auth.logout(false);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.accessToken()).toBeNull();
    expect(auth.user()).toBeNull();
  });

  it('rejects a failed login without storing anything', async () => {
    const promise = auth.login('t@x.com', 'bad');
    http
      .expectOne('/api/auth/login')
      .flush(
        { code: 'unauthorized', message: 'Invalid email or password' },
        { status: 401, statusText: 'Unauthorized' },
      );
    await expect(promise).rejects.toBeTruthy();
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('deduplicates concurrent refresh calls', async () => {
    sessionStorage.setItem('ballast.refresh', 'r0');
    const first = auth.refresh();
    const second = auth.refresh();
    http.expectOne('/api/auth/refresh').flush(response());
    expect(await first).toBe(true);
    expect(await second).toBe(true);
  });

  it('returns false when there is no refresh token', async () => {
    expect(await auth.refresh()).toBe(false);
  });
});
