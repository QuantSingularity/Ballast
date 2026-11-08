# Model results

Everything here was measured by running the code in this repository, with fixed seeds, so the numbers reproduce exactly.
They describe behavior on **synthetic** data produced by `SyntheticCashFlowGenerator` and `SyntheticRateProvider`. They say
how well the models recover structure that was put there on purpose, not how they would perform on real cash flows or
markets.

Reproduce:

```bash
dotnet run --project code/ai_models/examples -c Release -- all   # example outputs below
./scripts/smoke_test.sh                                          # starts the API
curl -H "Authorization: Bearer $TOKEN" "http://localhost:5000/api/v1/forecast/backtest?horizonDays=28"
```

## Cash forecast ensemble

Damped-trend Holt-Winters (weekly season) blended with a ridge regression on calendar and lag features. The blend weights
come from inverse mean squared error on a 28-day holdout. Intervals come from rolling-origin calibration: the model is refit
at 20 earlier origins, and the quantiles of its out-of-sample errors set the interval widths. Cumulative intervals use the
signed error quantiles, so they can be asymmetric.

Backtest on the seeded group-level history (all operating accounts in USD), 28-day horizon, 8 outer origins, 20 calibration
origins each:

| Measure                         | Ensemble | Holt-Winters alone | Ridge alone | Seasonal naive |
| ------------------------------- | -------- | ------------------ | ----------- | -------------- |
| Daily mean absolute error       | 137,069  | 136,929            | 148,762     | 195,417        |
| Cumulative error at horizon end | 597,411  | n/a                | n/a         | 1,986,728      |

- **MASE against seasonal naive: 0.70.** The ensemble is clearly better than the naive baseline, and about 3.3x better on the
  cumulative 28-day total.
- **The ensemble does not beat Holt-Winters alone on daily error** here (137,069 against 136,929). Its value is that it is
  not worse and that the ridge component adds calendar effects (month-end, payroll) that help on other series.
- **Interval coverage is 79.5% against a 90% nominal target.** The intervals are too narrow. Calibration origins overlap and
  their errors are correlated, so the effective sample is smaller than it looks. More origins helped on a comparable series
  (82% with 10, 85% with 20, 83% with 30), so this is a limit of the method on short history, not a tuning knob. Treat the
  bands as optimistic.

A single-entity example from `code/ai_models/examples`: MASE 0.71, coverage 85% (nominal 90%).

## Payment anomaly detector

Isolation forest (120 trees) over log amount, amount relative to the account median, weekend value date, new-beneficiary
flag and day of month, plus a per-beneficiary robust z-score (median and MAD, with a floor of 2% of the median so constant
payments do not produce infinite z-scores). A payment from a beneficiary with at least 5 stable, prior payments is treated
as routine and is not flagged on isolation score alone.

| Case                         | Score | Flagged | Why                                                |
| ---------------------------- | ----- | ------- | -------------------------------------------------- |
| Routine supplier, 21,000     | 0.41  | no      |                                                    |
| Same supplier, 400,000       | 0.68  | yes     | Robust z = 42.7 against that beneficiary's history |
| Unknown beneficiary, 900,000 | 0.68  | yes     | Large payment to a beneficiary with no history     |

The first version flagged routine month-end bank charges because small fixed amounts are rare in amount space. The routine
rule above came from that finding. There is no labeled fraud data, so precision and recall are not measured.

## Regime detection (hidden Markov model)

Two-state Gaussian HMM fit by Baum-Welch on 2,500 simulated returns.

| Parameter          | Fitted | True   |
| ------------------ | ------ | ------ |
| Calm volatility    | 0.0049 | 0.0050 |
| Stress volatility  | 0.0201 | 0.0200 |
| Calm persistence   | 0.96   | 0.97   |
| Stress persistence | 0.93   | 0.92   |

The filter separates a stressed 25-day window from a calm one (posterior of the high-volatility state above 0.5 versus below
0.2 in the test). After a short stressed window the posterior can be well under 1 if the last observation happens to be
small, which is the correct behavior of a filter, not a defect.

## Hedging policy (tabular Q-learning)

Environment: 4 monthly decision steps, two volatility regimes (2% and 5% per month), a transaction cost of 0.4% of the
notional moved, and a mean-variance reward. Actions are hedge ratios 0, 25, 50, 75, 100%.

Greedy hedge ratio by decision month, starting unhedged (3 buckets are used by the exposure horizons):

| Regime | Month 1 | Month 2 | Month 3 |
| ------ | ------- | ------- | ------- |
| Calm   | 50%     | 50%     | 0%      |
| Stress | 100%    | 100%    | 75%     |

Evaluation over 600 simulated episodes: mean utility -0.0037 for the learned policy against -0.0158 unhedged, and P&L
standard deviation 0.013 against 0.073.

Two honest notes:

- Calm-regime hedging of about 50% matches the analytic optimum for these parameters (the marginal variance reduction equals
  the marginal cost), so the agent learned something real about this toy problem.
- A first version trained on the realized per-step return failed: the zero-mean return noise (standard deviation 0.02) was
  larger than the cost differences (0.001 to 0.004), so the policy was effectively random and hedged fully everywhere. The
  reward now uses the expected variance penalty. That is a variance-reduction choice, and it means the agent optimizes expected
  utility rather than learning from raw returns.

The environment is a toy and the policy is tabular. The API clamps every recommendation into a treasury policy band, which is
the control that matters in practice. It is advice for a human to review, not an execution system.

## What is not measured

Calibration on real data, fraud precision and recall, hedging performance against real FX paths, and forecast accuracy
outside the weekly-seasonal, month-end-driven pattern the generator produces.
