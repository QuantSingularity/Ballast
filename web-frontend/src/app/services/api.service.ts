import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Account,
  AiAssessment,
  AiOverview,
  AnomalyItem,
  AuditEntry,
  AuditVerification,
  BacktestMetrics,
  CashSummary,
  CovenantResult,
  CreateHedgeRequest,
  CreatePaymentRequest,
  DebtSummary,
  Exposure,
  Facility,
  Forecast,
  FxRate,
  Hedge,
  HedgeRecommendation,
  HmmStatus,
  IntercompanyBalance,
  Ladder,
  LimitResult,
  NettingCycle,
  NettingResult,
  Payment,
  RiskMetrics,
  StressResult,
  Transaction,
} from '../types/models';

export type PaymentAction = 'submit' | 'approve' | 'reject' | 'release';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1';

  cashSummary(): Observable<CashSummary> {
    return this.http.get<CashSummary>(`${this.base}/cash/summary`);
  }
  accounts(): Observable<Account[]> {
    return this.http.get<Account[]>(`${this.base}/cash/accounts`);
  }
  ladder(): Observable<Ladder> {
    return this.http.get<Ladder>(`${this.base}/cash/ladder`);
  }

  transactions(accountId: string | null, days = 30, limit = 100): Observable<Transaction[]> {
    let params = new HttpParams().set('days', days).set('limit', limit);
    if (accountId) params = params.set('accountId', accountId);
    return this.http.get<Transaction[]>(`${this.base}/cash/transactions`, { params });
  }

  forecast(scope: string, horizonDays: number, confidence: number): Observable<Forecast> {
    const params = new HttpParams()
      .set('scope', scope)
      .set('horizonDays', horizonDays)
      .set('confidence', confidence);
    return this.http.get<Forecast>(`${this.base}/forecast`, { params });
  }

  backtest(scope: string, horizonDays = 28): Observable<BacktestMetrics> {
    const params = new HttpParams().set('scope', scope).set('horizonDays', horizonDays);
    return this.http.get<BacktestMetrics>(`${this.base}/forecast/backtest`, { params });
  }

  fxRates(): Observable<FxRate[]> {
    return this.http.get<FxRate[]>(`${this.base}/fx/rates`);
  }
  exposures(): Observable<Exposure> {
    return this.http.get<Exposure>(`${this.base}/fx/exposures`);
  }
  recommendations(): Observable<HedgeRecommendation[]> {
    return this.http.get<HedgeRecommendation[]>(`${this.base}/fx/recommendations`);
  }
  regimes(): Observable<HmmStatus[]> {
    return this.http.get<HmmStatus[]>(`${this.base}/fx/regimes`);
  }
  hedges(): Observable<Hedge[]> {
    return this.http.get<Hedge[]>(`${this.base}/fx/hedges`);
  }
  bookHedge(req: CreateHedgeRequest): Observable<Hedge> {
    return this.http.post<Hedge>(`${this.base}/fx/hedges`, req);
  }
  cancelHedge(id: string): Observable<Hedge> {
    return this.http.post<Hedge>(`${this.base}/fx/hedges/${id}/cancel`, {});
  }

  riskMetrics(): Observable<RiskMetrics> {
    return this.http.get<RiskMetrics>(`${this.base}/risk/metrics`);
  }
  stress(): Observable<StressResult[]> {
    return this.http.get<StressResult[]>(`${this.base}/risk/stress-scenarios`);
  }
  limits(): Observable<LimitResult[]> {
    return this.http.get<LimitResult[]>(`${this.base}/risk/limits`);
  }

  icBalances(): Observable<IntercompanyBalance[]> {
    return this.http.get<IntercompanyBalance[]>(`${this.base}/inhouse-bank/balances`);
  }

  nettingPreview(currency?: string): Observable<NettingResult> {
    const params = currency
      ? new HttpParams().set('settlementCurrency', currency)
      : new HttpParams();
    return this.http.get<NettingResult>(`${this.base}/inhouse-bank/netting/preview`, { params });
  }

  runNetting(currency?: string): Observable<NettingCycle> {
    return this.http.post<NettingCycle>(`${this.base}/inhouse-bank/netting/run`, {
      settlementCurrency: currency ?? null,
    });
  }

  nettingCycles(): Observable<NettingCycle[]> {
    return this.http.get<NettingCycle[]>(`${this.base}/inhouse-bank/netting/cycles`);
  }

  debtSummary(): Observable<DebtSummary> {
    return this.http.get<DebtSummary>(`${this.base}/debt/summary`);
  }
  facilities(): Observable<Facility[]> {
    return this.http.get<Facility[]>(`${this.base}/debt/facilities`);
  }
  covenants(): Observable<CovenantResult[]> {
    return this.http.get<CovenantResult[]>(`${this.base}/debt/covenants`);
  }

  payments(status?: string): Observable<Payment[]> {
    const params = status ? new HttpParams().set('status', status) : new HttpParams();
    return this.http.get<Payment[]>(`${this.base}/payments`, { params });
  }

  createPayment(req: CreatePaymentRequest): Observable<Payment> {
    return this.http.post<Payment>(`${this.base}/payments`, req);
  }

  paymentAction(id: string, action: PaymentAction, reason?: string): Observable<Payment> {
    const body = action === 'reject' ? { reason: reason ?? '' } : {};
    return this.http.post<Payment>(`${this.base}/payments/${id}/${action}`, body);
  }

  assessPayment(req: {
    accountId: string;
    beneficiary: string;
    amount: number;
    valueDate: string;
  }): Observable<AiAssessment> {
    return this.http.post<AiAssessment>(`${this.base}/anomalies/assess`, req);
  }

  anomalies(): Observable<AnomalyItem[]> {
    return this.http.get<AnomalyItem[]>(`${this.base}/anomalies`);
  }
  audit(limit = 50): Observable<AuditEntry[]> {
    return this.http.get<AuditEntry[]>(`${this.base}/audit`, {
      params: new HttpParams().set('limit', limit),
    });
  }
  verifyAudit(): Observable<AuditVerification> {
    return this.http.get<AuditVerification>(`${this.base}/audit/verify`);
  }
  aiOverview(): Observable<AiOverview> {
    return this.http.get<AiOverview>(`${this.base}/ai/models`);
  }
}
