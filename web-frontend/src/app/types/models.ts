export type UserRole = 'Analyst' | 'Treasurer' | 'Approver' | 'Admin';
export type PaymentStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Released' | 'Rejected';
export type HedgeDirection = 'SellForeign' | 'BuyForeign';

export interface User {
  id: string;
  email: string;
  name: string;
  role: UserRole;
}

export interface AuthResponse {
  user: User;
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

export interface CurrencyPosition {
  currency: string;
  native: number;
  base: number;
  share: number;
}
export interface EntityPosition {
  entityId: string;
  name: string;
  base: number;
}
export interface BankPosition {
  bank: string;
  base: number;
  share: number;
}

export interface CashSummary {
  asOf: string;
  baseCurrency: string;
  totalBase: number;
  accountCount: number;
  byCurrency: CurrencyPosition[];
  byEntity: EntityPosition[];
  byBank: BankPosition[];
}

export interface Account {
  id: string;
  entityId: string;
  entityName: string;
  bank: string;
  iban: string;
  currency: string;
  balance: number;
  balanceBase: number;
  kind: string;
}

export interface Transaction {
  id: string;
  accountId: string;
  date: string;
  amount: number;
  currency: string;
  category: string;
  counterparty: string;
}

export interface LadderBucket {
  label: string;
  days: number;
  projectedBalance: number;
  lower: number;
  upper: number;
  scheduledPayments: number;
}

export interface Ladder {
  asOf: string;
  baseCurrency: string;
  currentBalance: number;
  buckets: LadderBucket[];
}

export interface ForecastPoint {
  date: string;
  point: number;
  lower: number;
  upper: number;
  cumulative: number;
  cumulativeLower: number;
  cumulativeUpper: number;
}

export interface HistoryPoint {
  date: string;
  net: number;
}

export interface Forecast {
  scope: string;
  currency: string;
  horizonDays: number;
  confidence: number;
  generatedAt: string;
  model: {
    name: string;
    weightHoltWinters: number;
    weightRidge: number;
    calibrationOrigins: number;
  };
  daily: ForecastPoint[];
  history: HistoryPoint[];
}

export interface BacktestMetrics {
  origins: number;
  horizon: number;
  maeEnsemble: number;
  maeHoltWinters: number;
  maeRidge: number;
  maeSeasonalNaive: number;
  mase: number;
  cumulativeMaeEnsemble: number;
  cumulativeMaeSeasonalNaive: number;
  intervalCoverage: number;
  confidence: number;
}

export interface FxRate {
  currency: string;
  spotUsd: number;
  interestRate: number;
  forward30: number;
  forward90: number;
  highVolProbability: number;
}

export interface ExposureLine {
  currency: string;
  bucket: string;
  bucketIndex: number;
  netNative: number;
  netBase: number;
  hedgedNative: number;
  hedgeRatio: number;
  openNative: number;
  openBase: number;
}

export interface Exposure {
  asOf: string;
  baseCurrency: string;
  lines: ExposureLine[];
}

export interface Hedge {
  id: string;
  currency: string;
  direction: HedgeDirection;
  notional: number;
  contractRate: number;
  tradeDate: string;
  maturityDate: string;
  counterparty: string;
  status: string;
  markToMarketBase: number;
  effectivenessRatio: number | null;
  effectivenessStatus: string;
}

export interface CreateHedgeRequest {
  currency: string;
  direction: HedgeDirection;
  notional: number;
  tenorDays: number;
  counterparty: string;
}

export interface HedgeRecommendation {
  currency: string;
  bucket: string;
  targetRatio: number;
  currentRatio: number;
  direction: HedgeDirection | null;
  recommendedNotional: number;
  highVolProbability: number;
  rationale: string;
}

export interface HmmStatus {
  currency: string;
  sigmas: number[];
  means: number[];
  highVolProbability: number;
  calmPersistence: number;
  stressPersistence: number;
}

export interface CashAtRisk {
  horizonDays: number;
  confidence: number;
  cashAtRisk: number;
  expectedShortfall: number;
  samples: number;
  /** Approximate number of independent error windows behind the figure (overlapping windows count less). */
  effectiveSamples: number;
  reliability: 'low' | 'moderate' | 'adequate';
  method: 'empirical' | 'empirical-floored-by-normal';
}

export interface FxVar {
  currency: string;
  exposureBase: number;
  var95: number;
  expectedShortfall95: number;
  highVolProbability: number;
  sigmas: number[];
}

export interface RiskMetrics {
  asOf: string;
  baseCurrency: string;
  cashAtRisk: CashAtRisk[];
  liquidityBase: number;
  liquidityCoverageRatio: number;
  fxVar: FxVar[];
  fxVarTotal: number;
}

export interface StressResult {
  id: string;
  name: string;
  description: string;
  liquidityImpactBase: number;
  liquidityAfterBase: number;
  coverageRatioAfter: number;
}

export interface LimitResult {
  id: string;
  name: string;
  actual: number;
  limit: number;
  unit: string;
  status: 'Ok' | 'Warning' | 'Breach';
  detail: string;
}

export interface IntercompanyBalance {
  id: string;
  lenderEntityId: string;
  lenderName: string;
  borrowerEntityId: string;
  borrowerName: string;
  currency: string;
  amount: number;
}

export interface NettingTransfer {
  fromEntityId: string;
  toEntityId: string;
  amount: number;
}

export interface NettingResult {
  settlementCurrency: string;
  grossVolume: number;
  netVolume: number;
  reductionPct: number;
  positions: { entityId: string; net: number }[];
  transfers: NettingTransfer[];
}

export interface NettingCycle {
  id: string;
  runAt: string;
  runBy: string;
  settlementCurrency: string;
  grossVolume: number;
  netVolume: number;
  transfers: NettingTransfer[];
}

export interface Facility {
  id: string;
  entityId: string;
  name: string;
  currency: string;
  commitment: number;
  drawn: number;
  undrawn: number;
  utilization: number;
  annualRate: number;
  floating: boolean;
  maturity: string;
  drawnBase: number;
  undrawnBase: number;
}

export interface CovenantResult {
  facilityId: string;
  facilityName: string;
  covenantId: string;
  name: string;
  kind: string;
  actual: number;
  threshold: number;
  headroom: number;
  status: 'Pass' | 'Warning' | 'Breach';
}

export interface DebtSummary {
  baseCurrency: string;
  totalDebtBase: number;
  netDebtBase: number;
  liquidityBase: number;
  ebitdaProxyBase: number;
  annualInterestBase: number;
  facilities: Facility[];
  covenants: CovenantResult[];
}

export interface PaymentApproval {
  userId: string;
  userName: string;
  at: string;
}

export interface Payment {
  id: string;
  accountId: string;
  beneficiary: string;
  amount: number;
  currency: string;
  reference: string;
  valueDate: string;
  status: PaymentStatus;
  createdBy: string;
  createdAt: string;
  approvals: PaymentApproval[];
  riskFlags: string[];
  requiredApprovals: number;
  rejectionReason: string | null;
  releasedAt?: string | null;
}

export interface CreatePaymentRequest {
  accountId: string;
  beneficiary: string;
  amount: number;
  reference: string;
  valueDate: string;
}

export interface AiAssessment {
  score: number;
  threshold: number;
  robustZ: number;
  newCounterparty: boolean;
  flagged: boolean;
  reasons: string[];
}

export interface AnomalyItem {
  source: string;
  reference: string;
  accountId: string;
  counterparty: string;
  amountBase: number;
  date: string;
  score: number;
  reasons: string[];
}

export interface ModelInfo {
  name: string;
  kind: string;
  version: string;
  description: string;
}

export interface AiOverview {
  models: ModelInfo[];
  anomalyThreshold: number;
  hedgingAgentEpisodes: number;
  regimes: HmmStatus[];
  forecastBacktest: BacktestMetrics;
}

export interface AuditEntry {
  sequence: number;
  timestamp: string;
  actor: string;
  action: string;
  subject: string;
  detail: string;
  prevHash: string;
  hash: string;
}

export interface AuditVerification {
  valid: boolean;
  entries: number;
  brokenAtSequence: number | null;
}
