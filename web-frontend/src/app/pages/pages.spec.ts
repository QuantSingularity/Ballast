import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { apiStub, payment, signIn, users } from '../testing/fixtures';
import { DashboardPage } from './dashboard';
import { LoginPage } from './login';
import { PaymentsPage } from './payments';
import { RiskPage } from './risk';

function setup(user: keyof typeof users, stub = apiStub()) {
  sessionStorage.clear();
  signIn(users[user]!);
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: ApiService, useValue: stub },
    ],
  });
}

const settle = async (fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) => {
  await fixture.whenStable();
  await new Promise((r) => setTimeout(r, 0));
  fixture.detectChanges();
  await fixture.whenStable();
};

describe('LoginPage', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  it('signs in and navigates to the dashboard', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    const login = vi.spyOn(auth, 'login').mockResolvedValue(users['treasurer']!);
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const [email, password] = Array.from(el.querySelectorAll('input'));
    email!.value = 'treasurer@ballast.example';
    email!.dispatchEvent(new Event('input'));
    password!.value = 'secret-pass';
    password!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    expect(login).toHaveBeenCalledWith('treasurer@ballast.example', 'secret-pass');
    expect(nav).toHaveBeenCalledWith('/dashboard');
  });

  it('shows the server error and does not navigate on failure', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    vi.spyOn(auth, 'login').mockRejectedValue(
      new HttpErrorResponse({
        status: 401,
        error: { code: 'unauthorized', message: 'Invalid email or password' },
      }),
    );
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const [email, password] = Array.from(el.querySelectorAll('input'));
    email!.value = 'a@b.co';
    email!.dispatchEvent(new Event('input'));
    password!.value = 'x';
    password!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    expect(el.querySelector('[role=alert]')?.textContent).toContain('Invalid email or password');
    expect(nav).not.toHaveBeenCalled();
  });

  it('disables the submit button until both fields are filled', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('button')!.disabled).toBe(true);
  });
});

describe('DashboardPage', () => {
  it('renders KPIs and limits needing attention from the API', async () => {
    setup('treasurer');
    const fixture = TestBed.createComponent(DashboardPage);
    await settle(fixture);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('$38,417,400');
    expect(text).toContain('6.7x');
    expect(text).toContain('Minimum hedge ratio: 31-60 days');
    expect(text).not.toContain('Bank concentration: Northern Trust');
    expect(text).toContain('Payments awaiting approval');
    expect((fixture.nativeElement as HTMLElement).querySelector('svg')).not.toBeNull();
  });

  it('shows an error banner when the API fails', async () => {
    setup(
      'treasurer',
      apiStub({
        cashSummary: () =>
          throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'Boom' } })),
      }),
    );
    const fixture = TestBed.createComponent(DashboardPage);
    await settle(fixture);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent,
    ).toContain('Boom');
  });
});

describe('PaymentsPage role-aware actions', () => {
  const buttons = (fixture: { nativeElement: unknown }) =>
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tbody button')).map((b) =>
      b.textContent?.trim(),
    );

  it('lets an approver approve and reject a pending payment created by someone else', async () => {
    setup('approver');
    const fixture = TestBed.createComponent(PaymentsPage);
    await settle(fixture);
    expect(buttons(fixture)).toEqual(['Approve', 'Reject']);
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('New payment');
  });

  it('hides approve from the maker but shows submit for drafts and the creation form', async () => {
    setup(
      'treasurer',
      apiStub({
        payments: () =>
          of([
            payment({ status: 'PendingApproval' }),
            payment({ id: 'PAY-1002', status: 'Draft' }),
          ]),
      }),
    );
    const fixture = TestBed.createComponent(PaymentsPage);
    await settle(fixture);
    expect(buttons(fixture)).toEqual(['Submit']);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('New payment');
  });

  it('offers release only to a maker who did not approve', async () => {
    setup(
      'treasurer',
      apiStub({
        payments: () =>
          of([
            payment({
              status: 'Approved',
              approvals: [{ userId: 'U-003', userName: 'Alex Approver', at: '' }],
            }),
          ]),
      }),
    );
    const fixture = TestBed.createComponent(PaymentsPage);
    await settle(fixture);
    expect(buttons(fixture)).toEqual(['Release']);
  });

  it('gives an analyst a read-only view and shows risk flags', async () => {
    setup(
      'analyst',
      apiStub({
        payments: () =>
          of([
            payment({
              riskFlags: ['Anomaly screening: Large payment to a new beneficiary'],
              requiredApprovals: 2,
            }),
          ]),
      }),
    );
    const fixture = TestBed.createComponent(PaymentsPage);
    await settle(fixture);
    expect(buttons(fixture)).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Large payment to a new beneficiary',
    );
  });

  it('prevents an approver from approving a payment they already approved', async () => {
    setup(
      'approver',
      apiStub({
        payments: () =>
          of([
            payment({
              requiredApprovals: 2,
              approvals: [{ userId: 'U-003', userName: 'Alex Approver', at: '' }],
            }),
          ]),
      }),
    );
    const fixture = TestBed.createComponent(PaymentsPage);
    await settle(fixture);
    expect(buttons(fixture)).toEqual(['Reject']);
  });

  it('calls the approve endpoint and refreshes', async () => {
    const paymentAction = vi.fn().mockReturnValue(of(payment({ status: 'Approved' })));
    setup('approver', apiStub({ paymentAction }));
    const fixture = TestBed.createComponent(PaymentsPage);
    await settle(fixture);
    const approve = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody button'),
    ).find((b) => b.textContent?.trim() === 'Approve') as HTMLButtonElement;
    approve.click();
    await settle(fixture);
    expect(paymentAction).toHaveBeenCalledWith('PAY-1001', 'approve');
  });
});

describe('RiskPage', () => {
  it('shows how reliable each cash at risk estimate is and warns when samples are thin', async () => {
    setup('treasurer', apiStub({ stress: () => of([]) }));
    const fixture = TestBed.createComponent(RiskPage);
    await settle(fixture);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="car-quality"]')?.textContent).toContain('low');
    expect(el.querySelector('[data-testid="car-quality"]')?.textContent).toContain('9.3 effective');
    expect(el.querySelector('[data-testid="car-caveat"]')?.textContent).toContain('indicative');
  });
});
