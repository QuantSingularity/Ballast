import numpy as np
import pytest

from ballast_ai.agents import QLearningHedger, recommend_ratios
from ballast_ai.config import HEDGE_RATIO_LEVELS
from ballast_ai.environments import FxHedgingConfig, FxHedgingEnvironment, encode_state
from ballast_ai.generative import GaussianHmm, RegimeSwitchingSimulator
from ballast_ai.research import risk_metrics

TRUE_TRANSITION = [[0.97, 0.03], [0.08, 0.92]]


@pytest.fixture(scope="module")
def hmm() -> GaussianHmm:
    sim = RegimeSwitchingSimulator([0.0, 0.0], [0.005, 0.02], TRUE_TRANSITION, 4)
    return GaussianHmm.fit(sim.simulate_returns(1, 2500)[0])


@pytest.fixture(scope="module")
def agent() -> QLearningHedger:
    a = QLearningHedger()
    a.train(FxHedgingEnvironment(FxHedgingConfig(), 17), 25_000, 17)
    return a


def test_hmm_recovers_regime_volatilities(hmm):
    assert hmm.sigmas[0] == pytest.approx(0.005, abs=0.002)
    assert hmm.sigmas[1] == pytest.approx(0.02, abs=0.005)
    assert hmm.sigmas[0] < hmm.sigmas[1]
    assert np.allclose(hmm.transition.sum(axis=1), 1.0)


def test_hmm_filter_separates_stressed_from_calm_windows(hmm):
    frozen = [[1.0, 0.0], [0.0, 1.0]]
    stressed = RegimeSwitchingSimulator(
        [0, 0], [0.005, 0.02], frozen, 8
    ).simulate_returns(1, 25, 1)[0]
    quiet = RegimeSwitchingSimulator([0, 0], [0.005, 0.02], frozen, 8).simulate_returns(
        1, 25, 0
    )[0]
    p_stressed, p_quiet = hmm.filter_last(stressed)[1], hmm.filter_last(quiet)[1]
    assert p_stressed > 0.5 and p_quiet < 0.2 and p_stressed > p_quiet + 0.4


def test_hmm_round_trips_through_a_dict_and_rejects_short_input(hmm):
    again = GaussianHmm.from_dict(hmm.to_dict())
    np.testing.assert_allclose(again.sigmas, hmm.sigmas)
    with pytest.raises(ValueError):
        GaussianHmm.fit(np.zeros(10))


def test_simulator_is_seeded_and_vectorized():
    sim = RegimeSwitchingSimulator([0, 0], [0.01, 0.03], TRUE_TRANSITION, 5)
    a = sim.simulate_returns(50, 20)
    np.testing.assert_array_equal(
        a,
        RegimeSwitchingSimulator(
            [0, 0], [0.01, 0.03], TRUE_TRANSITION, 5
        ).simulate_returns(50, 20),
    )
    assert a.shape == (50, 20)


def test_q_learning_outperforms_no_hedge_and_random_policies(agent):
    env = FxHedgingEnvironment(FxHedgingConfig(), 99)
    learned = QLearningHedger.evaluate(env, agent.greedy, 600, 5000)
    none = QLearningHedger.evaluate(env, lambda _s: 0, 600, 5000)
    rng = np.random.default_rng(2)
    random_policy = QLearningHedger.evaluate(
        env, lambda _s: int(rng.integers(len(HEDGE_RATIO_LEVELS))), 600, 5000
    )
    assert learned.mean_utility > none.mean_utility
    assert learned.mean_utility > random_policy.mean_utility
    assert learned.pnl_std_dev < none.pnl_std_dev


def test_q_learning_hedges_more_in_stress_than_in_calm(agent):
    calm = sum(
        HEDGE_RATIO_LEVELS[agent.greedy(encode_state(b, 0, 0))] for b in range(3)
    )
    stress = sum(
        HEDGE_RATIO_LEVELS[agent.greedy(encode_state(b, 0, 1))] for b in range(3)
    )
    assert stress > calm


def test_recommend_ratios_uses_regime_and_current_hedge(agent):
    calm, stress = recommend_ratios(agent, [(0, 0.0, 0.1), (0, 0.0, 0.9)])
    assert stress >= calm and stress == 1.0
    assert recommend_ratios(agent, [(1, 0.49, 0.0)]) == recommend_ratios(
        agent, [(1, 0.5, 0.0)]
    )
    assert agent.episodes == 25_000 and len(agent.policy_table()) == 40


def test_environment_is_deterministic_and_terminates():
    env = FxHedgingEnvironment(FxHedgingConfig(), 1)
    env.reset(10)
    steps = [env.step(2) for _ in range(4)]
    assert steps[-1].done and not any(s.done for s in steps[:-1])
    env.reset(10)
    assert [env.step(2).pnl for _ in range(4)] == [s.pnl for s in steps]


def test_risk_metrics_match_normal_theory():
    pnl = np.random.default_rng(21).normal(0, 100, 50_000)
    assert risk_metrics.historical_var(pnl, 0.95) == pytest.approx(164.5, abs=6)
    assert risk_metrics.parametric_var(pnl, 0.95) == pytest.approx(164.5, abs=4)
    assert risk_metrics.expected_shortfall(pnl, 0.95) > risk_metrics.historical_var(
        pnl, 0.95
    )
    assert risk_metrics.ewma_volatility([0.01, -0.01, 0.01]) == pytest.approx(
        0.01, rel=0.01
    )


def test_fx_var_is_larger_in_stress_and_scales_with_exposure(hmm):
    calm_returns = [0.001] * 20
    stressed = RegimeSwitchingSimulator(
        [0, 0], [0.005, 0.02], [[1.0, 0.0], [0.0, 1.0]], 8
    ).simulate_returns(1, 20, 1)[0]
    calm = risk_metrics.fx_var(hmm, calm_returns, 1_000_000, 21, 0.95, 31)
    stress = risk_metrics.fx_var(hmm, stressed, 1_000_000, 21, 0.95, 31)
    assert stress.high_vol_probability > calm.high_vol_probability
    assert stress.var > calm.var > 0 and stress.expected_shortfall >= stress.var
    double = risk_metrics.fx_var(hmm, calm_returns, 2_000_000, 21, 0.95, 31)
    assert double.var == pytest.approx(2 * calm.var, rel=0.05)
