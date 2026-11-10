import copy
import math

import numpy as np
import pytest
from pydantic import ValidationError

from osiris_ml import dataset, spec
from osiris_ml.records import BattleRecord
from tests.conftest import parse


def test_toy_record_is_valid(toy_record):
    record = parse(toy_record())
    assert record.decisions


def _first_with(record, key):
    for decision in record["decisions"]:
        if key in decision:
            return decision
    pytest.skip(f"no decision with {key}")


BAD = {
    "extra field": lambda r: r.update(player_name="bob"),
    "nan feature": lambda r: r["decisions"][0]["x"].__setitem__(0, math.nan),
    "inf feature": lambda r: r["decisions"][0]["x"].__setitem__(0, math.inf),
    "huge feature": lambda r: r["decisions"][0]["x"].__setitem__(0, 1e6),
    "short features": lambda r: r["decisions"][0]["x"].pop(),
    "wrong schema": lambda r: r.update(schema=2),
    "wrong feature schema": lambda r: r.update(feature_schema=7),
    "bad uuid": lambda r: r.update(battle_id="not-a-uuid"),
    "uppercase uuid": lambda r: r.update(battle_id=r["battle_id"].upper()),
    "script in version": lambda r: r.update(client_version="<script>"),
    "negative power": lambda r: r.update(end_power=-1.0),
    "ends stronger": lambda r: r.update(end_power=r["start_power"] * 2),
    "won by retreating": lambda r: r.update(won=True, retreated=True),
    "bool as int": lambda r: r.update(model_version=True),
    "string number": lambda r: r.update(duration="12"),
    "zero probability": lambda r: r["decisions"][0]["s"].update(p=0.0),
    "probability above one": lambda r: r["decisions"][0]["s"].update(p=1.5),
    "masked action chosen": lambda r: r["decisions"][0]["s"].update(a=0, m=[False, True, True]),
    "single option": lambda r: r["decisions"][0]["s"].update(a=0, m=[True, False, False]),
    "decisions out of order": lambda r: r["decisions"].__setitem__(0, dict(r["decisions"][0], t=1e3)),
    "decision after battle": lambda r: r["decisions"][-1].update(t=r["duration"] + 100),
    "too many decisions": lambda r: r.update(decisions=r["decisions"] * 200),
    "empty decision": lambda r: r["decisions"].__setitem__(0, {"t": 0.0, "x": r["decisions"][0]["x"]}),
    "no decisions": lambda r: r.update(decisions=[]),
    "unknown opponent": lambda r: r.update(opponent="god"),
    "exploration too high": lambda r: r.update(exploration=0.9),
}


@pytest.mark.parametrize("name", sorted(BAD))
def test_rejects_bad_records(toy_record, name):
    record = copy.deepcopy(toy_record(seed=3))
    record["decisions"][0].setdefault("s", {"a": 0, "p": 0.5, "m": [True, True, True]})
    BAD[name](record)
    with pytest.raises((ValidationError, ValueError)):
        BattleRecord.model_validate(record)


def test_rejects_bad_target(toy_record):
    record = copy.deepcopy(toy_record(seed=4))
    decision = _first_with(record, "g")
    decision["g"]["a"] = len(decision["g"]["c"])
    with pytest.raises(ValidationError):
        BattleRecord.model_validate(record)
    decision["g"]["a"] = 0
    decision["g"]["c"][0] = decision["g"]["c"][0][:-1]
    with pytest.raises(ValidationError):
        BattleRecord.model_validate(record)


def test_json_round_trip(toy_record):
    record = parse(toy_record(seed=5))
    again = BattleRecord.model_validate_json(record.model_dump_json(by_alias=True))
    assert again == record


# ---- the trainer's deep check ----

def test_deep_check_accepts_honest(toy_record, baseline):
    for seed in range(5):
        assert dataset.deep_check(parse(toy_record(seed=seed)), {1: baseline}) is None


def test_deep_check_rejects_fabricated_probabilities(toy_record, baseline):
    record = toy_record(seed=6)
    for decision in record["decisions"]:
        for head in ("s", "c", "g"):
            if head in decision:
                decision[head]["p"] = 0.999  # "I was certain" - but the model wasn't.
        if "r" in decision:
            decision["r"]["p"] = 0.999
    assert dataset.deep_check(parse(record), {1: baseline}) is not None


def test_deep_check_rejects_flipped_actions(toy_record, baseline):
    """Claiming a different action than was taken, with the original probability, is caught."""
    record = toy_record(seed=6)
    changed = 0
    for decision in record["decisions"]:
        s = decision.get("s")
        if s and s["p"] > 0.5:
            other = next(i for i in range(3) if i != s["a"] and s["m"][i])
            s["a"] = other
            changed += 1
    assert changed >= 3
    assert dataset.deep_check(parse(record), {1: baseline}) is not None


def test_deep_check_rejects_unknown_version(toy_record, baseline):
    assert dataset.deep_check(parse(toy_record(model_version=5)), {1: baseline}) == "unknown model version"


def test_deep_check_rejects_knob_mismatch(toy_record, baseline):
    assert "temperature" in dataset.deep_check(parse(toy_record(temperature=1.5)), {1: baseline})
    assert "exploration" in dataset.deep_check(parse(toy_record(exploration=0.15)), {1: baseline})


def test_deep_check_allows_selfplay_exploration(baseline):
    from osiris_ml import toyenv
    explore = baseline.copy()
    explore.knobs[spec.KNOB_IDS["exploration"]] = 0.2
    record = parse(toyenv.play(explore, np.random.default_rng(1)))
    assert dataset.deep_check(record, {1: baseline}) is not None
    assert dataset.deep_check(record, {1: baseline}, extra_exploration=(0.2,)) is None


def test_contribution_weights_cap_a_flooder():
    sources = [f"honest{i}" for i in range(50) for _ in range(20)] + ["bot"] * 10000
    w = dataset.contribution_weights(sources)
    total = w.sum()
    bot_share = w[-10000:].sum() / total
    honest_share = w[:20].sum() / total
    assert bot_share <= 0.021
    assert bot_share <= honest_share * 1.05


def test_contribution_weights_few_sources():
    w = dataset.contribution_weights(["a"] * 3 + ["b"])
    np.testing.assert_allclose(w, [1, 1, 1, 1])


def test_battle_return_bounds(toy_record):
    for seed in range(20):
        r = dataset.battle_return(parse(toy_record(seed=seed)))
        assert -1.5 <= r <= 1.5
