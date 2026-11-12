"""Runnable examples. Usage: python examples/run_examples.py [forecast|anomaly|regimes|hedging|all]"""

from __future__ import annotations

import sys
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from ballast_ai.agents import QLearningHedger
from ballast_ai.anomaly import OutflowRecord, PaymentAnomalyDetector
from ballast_ai.config import HEDGE_RATIO_LEVELS
from ballast_ai.environments import FxHedgingConfig, FxHedgingEnvironment, encode_state
from ballast_ai.forecasting import forecast
from ballast_ai.generative import GaussianHmm, RegimeSwitchingSimulator
from ballast_ai.research import backtest, synthetic


def forecast_example() -> None:
    print("Cash flow forecast with calibrated intervals")
    start = date(2025, 1, 1)
    y = synthetic.daily_net(synthetic.generate(start, 540, synthetic.FlowProfile(1.0, -0.10, 5)), start, 540)
    f = forecast(y, start, 28, 0.9, 20)
    print(f"  weights: Holt-Winters {f.weight_holt_winters:.0%}, ridge {f.weight_ridge:.0%}")
    print(f"  calibration origins: {f.calibration_origins}")
    for h in range(7):
        print(f"  {f.dates[h]}  point {f.point[h]:12,.0f}  90% [{f.lower[h]:12,.0f}, {f.upper[h]:12,.0f}]")
    m = backtest.run(y, start, 28, 6, 12)
    print(f"  backtest: MASE {m.mase:.2f} vs seasonal naive")
    print(f"  interval coverage {m.interval_coverage:.0%} (nominal {m.confidence:.0%})")


def anomaly_example() -> None:
    print("Payment anomaly screening")
    start = date(2025, 1, 1)
    flows = synthetic.generate(start, 400, synthetic.FlowProfile(1.0, 0.0, 9))
    history = [OutflowRecord("ACC-1", f.counterparty, f.amount, f.date) for f in flows if f.amount < 0]
    detector = PaymentAnomalyDetector()
    detector.fit(history)
    for who, amount in (
        ("Globex Materials", 21_000.0),
        ("Globex Materials", 400_000.0),
        ("Unknown Holdings Ltd", 900_000.0),
    ):
        a = detector.assess("ACC-1", who, amount, date(2026, 3, 10))
        print(f"  {who:<22} {amount:>10,.0f}  score {a.score:.2f}  flagged {a.flagged}")
        for reason in a.reasons:
            print(f"      - {reason}")


def regimes_example() -> None:
    print("Hidden Markov regime detection on FX returns")
    truth = RegimeSwitchingSimulator([0.0, 0.0], [0.005, 0.02], [[0.97, 0.03], [0.08, 0.92]], 4)
    hmm = GaussianHmm.fit(truth.simulate_returns(1, 2500)[0])
    print(f"  fitted sigma: calm {hmm.sigmas[0]:.4f}, stress {hmm.sigmas[1]:.4f} (truth 0.0050, 0.0200)")
    calm_stay, stress_stay = hmm.transition[0, 0], hmm.transition[1, 1]
    print(f"  persistence: calm {calm_stay:.2f}, stress {stress_stay:.2f} (truth 0.97, 0.92)")


def hedging_example() -> None:
    print("Reinforcement learning hedge ratio policy")
    agent = QLearningHedger()
    agent.train(FxHedgingEnvironment(FxHedgingConfig(), 1), 25_000, 17)
    for regime, name in ((0, "calm  "), (1, "stress")):
        picks = [HEDGE_RATIO_LEVELS[agent.greedy(encode_state(b, 0, regime))] for b in range(3)]
        print(f"  {name} regime, hedge ratio by decision month: {', '.join(f'{p:.0%}' for p in picks)}")
    env = FxHedgingEnvironment(FxHedgingConfig(), 99)
    learned = QLearningHedger.evaluate(env, agent.greedy, 600, 5000)
    none = QLearningHedger.evaluate(env, lambda _s: 0, 600, 5000)
    print(
        f"  utility: learned {learned.mean_utility:.4f} vs unhedged {none.mean_utility:.4f}; "
        f"P&L std {learned.pnl_std_dev:.3f} vs {none.pnl_std_dev:.3f}"
    )


EXAMPLES = {
    "forecast": forecast_example,
    "anomaly": anomaly_example,
    "regimes": regimes_example,
    "hedging": hedging_example,
}


def main(argv: list[str]) -> int:
    command = argv[0] if argv else "all"
    if command == "all":
        for run in EXAMPLES.values():
            run()
            print()
        return 0
    if command in EXAMPLES:
        EXAMPLES[command]()
        return 0
    print("Usage: python examples/run_examples.py [forecast|anomaly|regimes|hedging|all]")
    return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
