import { Observable, of } from 'rxjs';
import { ApiService } from '../services/api.service';
import {
  Account,
  CashSummary,
  Forecast,
  Ladder,
  LimitResult,
  Payment,
  RiskMetrics,
  User,
} from '../types/models';

export const users: Record<string, User> = {
  treasurer: {
    id: 'U-002',
    email: 'treasurer@ballast.example',
    name: 'Taylor Treasurer',
    role: 'Treasurer',
  },
  approver: {
    id: 'U-003',
    email: 'approver@ballast.example',
    name: 'Alex Approver',
    role: 'Approver',
  },
  analyst: {
    id: 'U-005',
    email: 'analyst@ballast.example',
    name: 'Jordan Analyst',
    role: 'Analyst',
  },
  admin: { id: 'U-001', email: 'admin@ballast.example', name: 'Avery Admin', role: 'Admin' },
};

export function signIn(user: User): void {
  sessionStorage.setItem('ballast.access', 'access-token');
  sessionStorage.setItem('ballast.refresh', 'refresh-token');
  sessionStorage.setItem('ballast.user', JSON.stringify(user));
}

export const summary: CashSummary = {
  asOf: '2026-11-05',
  baseCurrency: 'USD',
  totalBase: 38_417_400,
  accountCount: 14,
  byCurrency: [
    { currency: 'USD', native: 12_200_000, base: 12_200_000, share: 0.3176 },
    { currency: 'EUR', native: 12_300_000, base: 13_345_500, share: 0.3474 },
  ],
  byEntity: [{ entityId: 'ENT-US', name: 'Ballast Holdings Inc', base: 12_200_000 }],
  byBank: [
    { bank: 'Northern Trust', base: 8_600_000, share: 0.2239 },
    { bank: 'Deutsche Bank', base: 6_300_000, share: 0.1638 },
  ],
};

export const accounts: Account[] = [
  {
    id: 'ACC-US-001',
    entityId: 'ENT-US',
    entityName: 'Ballast Holdings Inc',
    bank: 'Northern Trust',
    iban: 'XX00',
    currency: 'USD',
    balance: 5_600_000,
    balanceBase: 5_600_000,
    kind: 'Operating',
  },
  {
    id: 'ACC-DE-001',
    entityId: 'ENT-DE',
    entityName: 'Ballast Europe GmbH',
    bank: 'Deutsche Bank',
    iban: 'XX01',
    currency: 'EUR',
    balance: 4_000_000,
    balanceBase: 4_340_000,
    kind: 'Operating',
  },
];

export const ladder: Ladder = {
  asOf: '2026-11-05',
  baseCurrency: 'USD',
  currentBalance: 38_417_400,
  buckets: [
    {
      label: '7 days',
      days: 7,
      projectedBalance: 38_900_000,
      lower: 38_100_000,
      upper: 39_500_000,
      scheduledPayments: 0,
    },
    {
      label: '30 days',
      days: 30,
      projectedBalance: 40_100_000,
      lower: 38_900_000,
      upper: 41_300_000,
      scheduledPayments: 0,
    },
  ],
};

export const risk: RiskMetrics = {
  asOf: '2026-11-05',
  baseCurrency: 'USD',
  cashAtRisk: [
    {
      horizonDays: 30,
      confidence: 0.95,
      cashAtRisk: 1_456_139,
      expectedShortfall: 1_700_000,
      samples: 20,
    },
  ],
  liquidityBase: 65_319_400,
  liquidityCoverageRatio: 6.7,
  fxVar: [],
  fxVarTotal: 0,
};

export const limits: LimitResult[] = [
  {
    id: 'a',
    name: 'Bank concentration: Northern Trust',
    actual: 0.22,
    limit: 0.35,
    unit: 'share',
    status: 'Ok',
    detail: 'fine',
  },
  {
    id: 'b',
    name: 'Minimum hedge ratio: 31-60 days',
    actual: 0.1,
    limit: 0.3,
    unit: 'ratio',
    status: 'Breach',
    detail: '10% hedged against a 30% floor',
  },
];

export const forecast: Forecast = {
  scope: 'all',
  currency: 'USD',
  horizonDays: 3,
  confidence: 0.9,
  generatedAt: '2026-11-05T00:00:00Z',
  model: {
    name: 'cash-forecast-ensemble',
    weightHoltWinters: 0.46,
    weightRidge: 0.54,
    calibrationOrigins: 20,
  },
  daily: [0, 1, 2].map((i) => ({
    date: `2026-11-0${5 + i}`,
    point: 100,
    lower: -50,
    upper: 250,
    cumulative: 100 * (i + 1),
    cumulativeLower: 100 * (i + 1) - 300,
    cumulativeUpper: 100 * (i + 1) + 300,
  })),
  history: [],
};

export function payment(overrides: Partial<Payment> = {}): Payment {
  return {
    id: 'PAY-1001',
    accountId: 'ACC-US-001',
    beneficiary: 'Globex Materials',
    amount: 10_000,
    currency: 'USD',
    reference: 'INV-1',
    valueDate: '2026-11-07',
    status: 'PendingApproval',
    createdBy: 'U-002',
    createdAt: '2026-11-05T09:00:00Z',
    approvals: [],
    riskFlags: [],
    requiredApprovals: 1,
    rejectionReason: null,
    ...overrides,
  };
}

export function apiStub(
  overrides: Partial<Record<keyof ApiService, unknown>> = {},
): Partial<ApiService> {
  const ok =
    <T>(v: T) =>
    () =>
      of(v) as Observable<T>;
  return {
    cashSummary: ok(summary),
    accounts: ok(accounts),
    ladder: ok(ladder),
    riskMetrics: ok(risk),
    limits: ok(limits),
    covenants: ok([]),
    forecast: ok(forecast),
    payments: ok([payment()]),
    transactions: ok([]),
    ...(overrides as object),
  } as Partial<ApiService>;
}
