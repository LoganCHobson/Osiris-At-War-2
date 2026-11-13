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
    assert p1 == pytest.approx(0.0, abs=1e-12)  # no exploration on retreat: it can't be undone


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


def _score(model):
    """Mean return played the way the game plays: actions sampled from the policy, not its top choice.
    Every model meets the same 4,000 battles, so differences between models aren't battle luck."""
    return toyenv.mean_return(model, battles=4000, seed=5, greedy=False)


def _explore(model, rate=0.2):
    explorer = model.copy()
    explorer.knobs[spec.KNOB_IDS["exploration"]] = rate
    return explorer


@pytest.mark.slow
def test_training_rounds_improve_the_toy_ai(baseline):
    """Five rounds of play -> train -> gate, and the end result clearly better than the Admiral.

    The offline gate is statistical: on thin margins it can let through a model a little worse than the
    one it replaces (it did on GitHub's runners, -0.028, where the floating point differs from a PC's).
    In production self-play catches that: a release must match the old one within the arena tolerance
    (0.05) over 40 games, and a regressed release is reverted. So no release may be clearly worse,
    at most one may slip slightly, and the loop as a whole must improve.

    How many rounds release depends on the run. Since retreat stopped exploring (policy.retreat_probability)
    the starting AI no longer bails out at random, so it starts stronger (0.300, was 0.293) with less to
    gain: the gate holds round 0 (+0.056, not yet significant) and releases round 1 (+0.040); later
    candidates are worse or no clearer. The test asks for what matters: a clear net gain (over 10% of the
    starting score), never a clear regression."""
    rng = np.random.default_rng(1)
    current = baseline
    start = _score(current)
    score = start
    released = slips = 0
    for _ in range(5):
        records = [parse(toyenv.play(_explore(current), rng)) for _ in range(2000)]
        candidate = train.train(current, dataset.build(records[:1600])).rounded()
        candidate.version = current.version + 1
        report = gate.evaluate(current, candidate, dataset.build(records[1600:]), installs=50)
        if report.passed:
            new_score = _score(candidate)
            assert new_score > score - 0.04, report.metrics
            if new_score <= score - 0.015:
                slips += 1
            current, score = candidate, new_score
            released += 1
    assert released >= 1
    assert slips <= 1
    assert score - start > 0.03


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


def test_training_stops_before_moving_too_far(baseline):
    """A long, hot run would carry a head past the step-size limit; the early stop keeps it inside."""
    rng = np.random.default_rng(8)
    ds = dataset.build([parse(toyenv.play(_explore(baseline), rng)) for _ in range(800)])
    hot = dict(epochs=60, learning_rate=0.05, anchor=0.0, kl_check_every=5)
    free = train.train(baseline, ds, train.TrainConfig(max_kl=0.0, **hot))  # 0 turns the stop off
    kept = train.train(baseline, ds, train.TrainConfig(max_kl=0.01, **hot))
    assert max(gate.mean_kl(baseline, free, ds).values()) > 0.01  # the run really would overshoot
    moved = max(gate.mean_kl(baseline, kept, ds).values())
    assert 0.005 < moved <= 0.01  # inside the limit, and up near it: an overshoot still makes progress


def _assert_same_dataset(a, b):
    for field in ("x", "returns", "weights", "battle", "temperature", "epsilon"):
        np.testing.assert_array_equal(getattr(a, field), getattr(b, field))
    assert a.n_battles == b.n_battles and a.heads.keys() == b.heads.keys()
    for name in a.heads:
        assert a.heads[name].keys() == b.heads[name].keys()
        for key in a.heads[name]:
            np.testing.assert_array_equal(a.heads[name][key], b.heads[name][key])


def test_consuming_merge_matches_building_from_records(baseline):
    # The trainer merges the pool battle by battle and drops each one as it goes (so it's held once,
    # not twice); the result has to be exactly what building from the records gives.
    rng = np.random.default_rng(11)
    records = [parse(toyenv.play(_explore(baseline), rng)) for _ in range(12)]
    weights = rng.uniform(0.5, 2.0, len(records))
    battles = [dataset.compact(r) for r in records]
    merged = dataset.merge(battles, weights, consume=True)
    _assert_same_dataset(merged, dataset.build(records, weights))
    assert all(b.data is None for b in battles)  # each battle's own arrays were released


def test_batch_grows_with_the_pool(baseline, caplog):
    # Each step averages over 1/steps_per_epoch of the pool, so a round's few dozen steps learn from most
    # of the farm's games instead of a fixed 1,024-row sample.
    rng = np.random.default_rng(5)
    ds = dataset.build([parse(toyenv.play(_explore(baseline), rng)) for _ in range(40)])
    cfg = train.TrainConfig(epochs=1, batch_size=8, steps_per_epoch=4, max_kl=0.0)
    with caplog.at_level("INFO", logger="grand-admiral.trainer"):
        train.train(baseline, ds, cfg)
    expected = -(-len(ds.x) // 4)
    assert expected > 8
    assert f"{expected} per step" in caplog.text
