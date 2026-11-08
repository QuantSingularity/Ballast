import {
  HttpClient,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { users } from '../testing/fixtures';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    sessionStorage.clear();
    sessionStorage.setItem('ballast.access', 'old-access');
    sessionStorage.setItem('ballast.refresh', 'refresh-1');
    sessionStorage.setItem('ballast.user', JSON.stringify(users['treasurer']));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', children: [] }]),
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => ctrl.verify());

  it('attaches the bearer token to API calls but not to auth calls', () => {
    http.get('/api/v1/cash/summary').subscribe();
    expect(
      ctrl
        .expectOne('/api/v1/cash/summary')
        .request.headers.get('Authorization'),
    ).toBe('Bearer old-access');
    http.post('/api/auth/login', {}).subscribe();
    expect(
      ctrl.expectOne('/api/auth/login').request.headers.has('Authorization'),
    ).toBe(false);
  });

  it('refreshes once on 401 and retries with the new token', async () => {
    let result: unknown;
    http.get('/api/v1/cash/summary').subscribe((r) => (result = r));
    ctrl
      .expectOne('/api/v1/cash/summary')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    ctrl.expectOne('/api/auth/refresh').flush({
      user: users['treasurer'],
      accessToken: 'new-access',
      refreshToken: 'refresh-2',
      expiresIn: 1800,
    });
    await new Promise((r) => setTimeout(r, 0));
    const retry = ctrl.expectOne('/api/v1/cash/summary');
    expect(retry.request.headers.get('Authorization')).toBe(
      'Bearer new-access',
    );
    retry.flush({ ok: true });
    expect(result).toEqual({ ok: true });
  });

  it('logs out when the refresh fails', async () => {
    let failed = false;
    http
      .get('/api/v1/cash/summary')
      .subscribe({ error: () => (failed = true) });
    ctrl
      .expectOne('/api/v1/cash/summary')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    ctrl
      .expectOne('/api/auth/refresh')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await new Promise((r) => setTimeout(r, 0));
    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('does not try to refresh when a login attempt returns 401', () => {
    let failed = false;
    http
      .post('/api/auth/login', {})
      .subscribe({ error: () => (failed = true) });
    ctrl
      .expectOne('/api/auth/login')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(failed).toBe(true);
  });
});
