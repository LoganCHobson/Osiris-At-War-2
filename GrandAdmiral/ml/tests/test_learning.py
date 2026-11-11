"""Does the learning loop actually learn, and does the gate only let improvements through?"""

import numpy as np
import pytest

from osiris_ml import dataset, distill, gate, policy, spec, toyenv, train
from tests.conftest import parse


def test_policy_distributions():
    mixed, raw = policy.masked_softmax([1.0, 2.0, 3.0], [True, False, True], 1.0, 0.1)
    assert mixed[1] == 0.0 and raw[1] == 0.0
    assert mixed.sum() == pytest.approx(1.0)
    assert mixed[[0, 2]].min() >= 0.05 - 1e-12  # epsilon spread over the two allowed actions
    p1, _ = policy.retreat_probability(-50.0, 1.0, 0.1)
    assert p1 == pytest.approx(0.05)


def test_baseline_matches_admiral_rules(baseline):
    agreement = distill.heuristic_agreement(baseline)
    assert agreement["stance"] > 0.98
    assert agreement["retreat"] > 0.98
    assert distill.canary_scores(baseline) == {"stance": 1.0, "retreat": 1.0}


def test_baseline_plays_like_admiral_in_toy(baseline):
    heuristic = toyenv.mean_return(baseline, battles=800, heuristic=True)
    learned = toyenv.mean_return(baseline, battles=800)
    assert abs(heuristic - learned) < 0.03


def test_gradients_match_finite_differences(baseline):
    rng = np.random.default_rng(3)
    model = baseline.copy()
    model.w2 += rng.normal(0, 0.3, model.w2.shape)
    ds = dataset.build([parse(toyenv.play(model, rng)) for _ in range(8)])
    adv = train.advantages(train.fit_critic(model, ds), ds)
    cfg = train.TrainConfig(clip=10.0, value_coef=0.5, anchor=0.01)
    anchor = [p.copy() + 0.01 for p in model.params()]
    rows = np.arange(len(ds.x))
    _, grads = train.loss_and_grads(model, anchor, ds, adv, rows, cfg)
    for p, g in zip(model.params(), grads):
        for _ in range(10):
            idx = tuple(rng.integers(0, s) for s in p.shape)
            old = p[idx]
            p[idx] = old + 1e-5
            plus, _ = train.loss_and_grads(model, anchor, ds, adv, rows, cfg)
            p[idx] = old - 1e-5
            minus, _ = train.loss_and_grads(model, anchor, ds, adv, rows, cfg)
            p[idx] = old
            numeric = (plus - minus) / 2e-5
            assert abs(numeric - g[idx]) <= 1e-5 + 1e-4 * abs(numeric)


def _explore(model, rate=0.2):
    explorer = model.copy()
    explorer.knobs[spec.KNOB_IDS["exploration"]] = rate
    return explorer


@pytest.mark.slow
@pytest.mark.xfail(strict=False, reason="known issue (2026-11-10): with the schema 2 orders the toy trainer gains about as much per round as evaluation noise (~+-0.01; +0.02 per round with the orders taken out), so the offline gate can pass a worse model or reject a better one. Releases now need self-play to confirm them (require_arena). Being worked on: the toy simulator and the trainer's credit assignment.")
def test_training_rounds_improve_the_toy_ai(baseline):
    """Five rounds of play -> train -> gate. Every release must be at least as good as the one it
    replaced (within evaluation noise), and the end result clearly better than the Admiral."""
    rng = np.random.default_rng(1)
    current = baseline
    start = toyenv.mean_return(current, battles=2000)
    score = start
    released = 0
    for _ in range(5):
        records = [parse(toyenv.play(_explore(current), rng)) for _ in range(2000)]
        candidate = train.train(current, dataset.build(records[:1600])).rounded()
        candidate.version = current.version + 1
        report = gate.evaluate(current, candidate, dataset.build(records[1600:]), installs=50)
        if report.passed:
            new_score = toyenv.mean_return(candidate, battles=2000)
            assert new_score > score - 0.015, report.metrics
            current, score = candidate, new_score
            released += 1
    assert released >= 2
    assert score - start > 0.04


def test_gate_rejects_an_unchanged_model(baseline):
    rng = np.random.default_rng(2)
    records = [parse(toyenv.play(baseline, rng)) for _ in range(300)]
    report = gate.evaluate(baseline, baseline.copy(), dataset.build(records), installs=50)
    assert not report.passed
    assert any("not significant" in r for r in report.reasons)


def test_gate_rejects_a_coward(baseline):
    """A model that retreats from fights it is winning fails the canaries, whatever the data says."""
    coward = baseline.copy()
    coward.w2[spec.OUT_RETREAT] = 0.0
    coward.b2[spec.OUT_RETREAT] = 5.0  # Retreats whatever the odds.
    rng = np.random.default_rng(3)
    records = [parse(toyenv.play(baseline, rng)) for _ in range(100)]
    report = gate.evaluate(baseline, coward, dataset.build(records), installs=50)
    assert not report.passed
    assert any("canary" in r for r in report.reasons)


def test_gate_rejects_a_big_jump(baseline):
    jumped = baseline.copy()
    jumped.b2[spec.OUT_COMMIT] = [0.0, 0.0, 6.0]
    rng = np.random.default_rng(4)
    records = [parse(toyenv.play(baseline, rng)) for _ in range(100)]
    report = gate.evaluate(baseline, jumped, dataset.build(records), installs=50)
    assert any("moved too far" in r for r in report.reasons)


def test_gate_needs_enough_installs(baseline):
    rng = np.random.default_rng(5)
    records = [parse(toyenv.play(baseline, rng)) for _ in range(60)]
    report = gate.evaluate(baseline, baseline.copy(), dataset.build(records), installs=3)
    assert any("not enough evidence" in r for r in report.reasons)


def test_gate_rejects_non_finite(baseline):
    broken = baseline.copy()
    broken.w1[0, 0] = np.nan
    rng = np.random.default_rng(6)
    report = gate.evaluate(baseline, broken, dataset.build([parse(toyenv.play(baseline, rng))]), installs=50)
    assert not report.passed


def test_gate_arena_regression(baseline):
    rng = np.random.default_rng(7)
    records = [parse(toyenv.play(baseline, rng)) for _ in range(50)]
    report = gate.evaluate(baseline, baseline.copy(), dataset.build(records), installs=50,
                           arena={"candidate": 0.40, "current": 0.55})
    assert any("self-play" in r for r in report.reasons)


def test_training_is_deterministic(baseline):
    rng = np.random.default_rng(8)
    ds = dataset.build([parse(toyenv.play(baseline, rng)) for _ in range(40)])
    cfg = train.TrainConfig(epochs=3)
    a = train.train(baseline, ds, cfg)
    b = train.train(baseline, ds, cfg)
    assert a.to_bytes() == b.to_bytes()
