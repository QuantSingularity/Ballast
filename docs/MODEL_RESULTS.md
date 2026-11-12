# Model results

Everything here was measured by running the code in this repository, with fixed seeds, so the numbers reproduce. They
describe behavior on **synthetic** data produced by the backend demo seeder, the Python `research.synthetic` generator and
the synthetic rate provider. They say how well the models recover structure that was put there on purpose, not how they
would perform on real cash flows or markets.

Reproduce:

```bash
cd code/ai_models
.venv/bin/python examples/run_examples.py all        # standalone examples (anomaly, regimes, hedging, forecast)
.venv/bin/python -m pytest                            # behavior tests
./scripts/start_dev.sh --api-only                     # then GET /api/v1/forecast/backtest?horizonDays=28
```

## Port parity with the original C# models

The models were first written in C# and then ported to Python. On the same seeded data, the forecasting path reproduces the
C# numbers to the last digit (forecasting uses no random numbers): the group-level backtest below, the 30-day cash at risk of
1,456,139 (computed from 20 calibration origins at the time), and the hedge recommendation notionals all matched exactly. Components that draw random numbers (the isolation
forest, the RL agent, the regime simulator) use a different random stream, so their exact values differ slightly and they are
checked by behavior instead.

## Cash forecast ensemble

Damped-trend Holt-Winters (weekly season) blended with a ridge regression on calendar and lag features. The blend weights
come from inverse mean squared error on a 28-day holdout. Intervals come from rolling-origin calibration: the model is refit
at earlier origins, and the quantiles of its out-of-sample errors set the interval widths. Cumulative intervals use the
signed error quantiles, so they can be asymmetric.

**Group-level history through the API** (all operating accounts in USD, 28-day horizon, 8 outer origins, 20 calibration
origins each):

| Measure                         | Ensemble | Holt-Winters alone | Ridge alone | Seasonal naive |
| ------------------------------- | -------- | ------------------ | ----------- | -------------- |
| Daily mean absolute error       | 137,069  | 136,929            | 148,762     | 195,417        |
| Cumulative error at horizon end | 597,411  | n/a                | n/a         | 1,986,728      |

- **MASE against seasonal naive: 0.70**, and the cumulative 28-day error is about 3.3x lower than the naive baseline.
- **The ensemble does not beat Holt-Winters alone on daily error here** (137,069 against 136,929).
- **90% intervals covered 79.5% of outcomes.** They are too narrow.

**Spread across 8 independent synthetic series** (single-entity profile, 28-day horizon, 6 outer origins, 12 calibration
origins), to show how much of that is noise:

| Series seed           | 1    | 2    | 3    | 4    | 5    | 6    | 7    | 8    |
| --------------------- | ---- | ---- | ---- | ---- | ---- | ---- | ---- | ---- |
| MASE                  | 0.63 | 0.69 | 0.66 | 0.59 | 0.76 | 0.69 | 0.65 | 0.78 |
| 90% interval coverage | 79%  | 86%  | 88%  | 90%  | 64%  | 82%  | 93%  | 88%  |

- MASE ranges 0.59 to 0.78 (mean 0.68), always better than the naive baseline.
- Coverage ranges 64% to 93% (mean 84%). On average the intervals are about six points short of nominal, and on one series
  they were 26 points short. Calibration origins overlap and their errors are correlated, so the effective sample is smaller
  than the origin count suggests. Treat the bands as optimistic.

## Cash at risk sample quality

Cash at risk is read from the cumulative forecast errors of the calibration origins. Origins are seven days apart, so for a
30-day horizon each window overlaps most of its neighbors, and for 91 days almost entirely. The risk metrics therefore use the
largest calibration set the service accepts (40 origins, about 0.4 s) and report `effectiveSamples` (origins x 7 days divided
by the horizon), a `reliability` label (low below 10, moderate below 30) and a `method`. When fewer than five observations
fall in the tail (always the case at 40 samples), the figure is the larger of the empirical quantile and a normal fit to the
same errors, so the 99% level is no longer just the sample minimum. On the demo data this gives, in USD:

| Horizon | Level | Cash at risk | Expected shortfall | Effective samples | Reliability |
| ------- | ----- | ------------ | ------------------ | ----------------- | ----------- |
| 30 days | 95%   | 1,649,843    | 1,930,056          | 9.3               | low         |
| 30 days | 99%   | 2,090,276    | 2,404,249          | 9.3               | low         |
| 91 days | 95%   | 2,689,164    | 3,404,089          | 3.1               | low         |
| 91 days | 99%   | 3,851,780    | 4,429,879          | 3.1               | low         |

With 540 days of history only about four non-overlapping 91-day windows exist after the minimum training period, so the long
horizon figures are indicative. More history is the only real fix.

- The ensemble beat Holt-Winters alone on daily error in 5 of the 8 series, by 0.2% to 3%. The gain is small.

## Payment anomaly detector

Isolation forest (120 trees) over log amount, amount relative to the account median, weekend value date, new-beneficiary
flag and day of month, plus a per-beneficiary robust z-score (median and MAD, with a floor of 2% of the median so constant
payments do not produce infinite z-scores). A payment from a beneficiary with at least 5 stable, prior payments is treated
as routine and is not flagged on isolation score alone.

| Case (standalone example)    | Score | Flagged | Why                                                |
| ---------------------------- | ----- | ------- | -------------------------------------------------- |
| Routine supplier, 21,000     | 0.43  | no      |                                                    |
| Same supplier, 400,000       | 0.71  | yes     | Robust z = 36.2 against that beneficiary's history |
| Unknown beneficiary, 900,000 | 0.71  | yes     | Large payment to a beneficiary with no history     |

Through the API on the demo history: Globex Materials 20,000 scores 0.46 and is not flagged; a brand-new vendor at 900,000
scores 0.68 and is flagged. The last 30 days of historical transactions contain nothing flagged.

An early version flagged routine month-end bank charges because small fixed amounts are rare in amount space; the routine
rule above came from that finding. There is no labeled fraud data, so precision and recall are not measured.

## Regime detection (hidden Markov model)

Two-state Gaussian HMM fit by Baum-Welch on 2,500 simulated returns (converges in about 15 iterations).

| Parameter          | Fitted | True   |
| ------------------ | ------ | ------ |
| Calm volatility    | 0.0050 | 0.0050 |
| Stress volatility  | 0.0191 | 0.0200 |
| Calm persistence   | 0.97   | 0.97   |
| Stress persistence | 0.92   | 0.92   |

The filter separates a stressed 25-day window from a calm one (posterior of the high-volatility state above 0.5 versus below
0.2 in the tests). After a short stressed window the posterior can be well short of 1 if the last observation happens to be
small, which is the correct behavior of a filter, not a defect.

## Hedging policy (tabular Q-learning)

Environment: 4 monthly decision steps, two volatility regimes (2% and 5% per month), a transaction cost of 0.4% of the
notional moved, and a mean-variance reward. Actions are hedge ratios 0, 25, 50, 75, 100%.

Greedy hedge ratio by decision month, starting unhedged (3 buckets are used by the exposure horizons):

| Regime | Month 1 | Month 2 | Month 3 |
| ------ | ------- | ------- | ------- |
| Calm   | 75%     | 25%     | 25%     |
| Stress | 100%    | 100%    | 75%     |

Evaluation over 600 simulated episodes: mean utility -0.0037 for the learned policy against -0.0156 unhedged, and P&L
standard deviation 0.008 against 0.075.

Notes:

- The policy has the right shape: it hedges more in stress than in calm, and in calm it hedges less as fewer months of risk
  remain. A rough closed-form estimate for the calm regime (ignoring the option to re-hedge later) is about 58%, 44% and 17%
  for the three months. The learned 75/25/25 is within one or two grid steps of that, and utility is flat near the optimum, so
  adjacent levels are hard to tell apart. Different random seeds pick different adjacent levels (an earlier C# run learned
  50/50/0). Do not read the exact percentages as precise.
- A first version trained on the realized per-step return failed: the zero-mean return noise (standard deviation 0.02) was
  larger than the cost differences (0.001 to 0.004), so the policy was effectively random and hedged fully everywhere. The
  reward now uses the expected variance penalty. That is a variance-reduction choice, and it means the agent optimizes expected
  utility rather than learning from raw returns.
- The environment is a toy and the policy is tabular. The API clamps every recommendation into a treasury policy band, which is
  the control that matters in practice. It is advice for a human to review, not an execution system.

## What is not measured

Calibration on real data, fraud precision and recall, hedging performance against real FX paths, and forecast accuracy
outside the weekly-seasonal, month-end-driven pattern the generators produce.
