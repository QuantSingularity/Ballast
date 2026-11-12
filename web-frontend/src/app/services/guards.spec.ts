import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { signIn, users } from '../testing/fixtures';
import { authGuard, guestGuard, roleGuard } from './guards';

const run = (guard: ReturnType<typeof roleGuard> | typeof authGuard) =>
  TestBed.runInInjectionContext(() =>
    guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
  );

describe('route guards', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  });

  it('redirects anonymous users to login and lets signed-in users through', () => {
    expect(run(authGuard)).toBeInstanceOf(UrlTree);
    signIn(users['treasurer']!);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    expect(run(authGuard)).toBe(true);
  });

  it('keeps signed-in users away from the login page', () => {
    signIn(users['analyst']!);
    expect(
      TestBed.runInInjectionContext(() =>
        guestGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
      ),
    ).toBeInstanceOf(UrlTree);
  });

  it('enforces roles', () => {
    signIn(users['analyst']!);
    expect(run(roleGuard('Treasurer', 'Admin'))).toBeInstanceOf(UrlTree);
    expect(run(roleGuard('Analyst'))).toBe(true);
  });
});
