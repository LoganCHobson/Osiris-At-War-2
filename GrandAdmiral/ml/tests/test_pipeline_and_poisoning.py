"""The nightly round end to end on SQLite, including what happens when people send garbage or
try to teach the AI bad habits."""

import datetime as dt
import json
import uuid

import numpy as np
import pytest
from sqlalchemy import select, update

from osiris_ml import bundle, pipeline, spec, storage, toyenv
from osiris_ml.model import Model
from tests.conftest import parse


@pytest.fixture
def db(tmp_path, baseline, signing_key):
    engine = storage.engine_for(f"sqlite:///{tmp_path / 'ga.db'}")
    storage.create_all(engine)
    with engine.begin() as conn:
        pipeline.publish(conn, baseline, "released", {"bootstrap": True}, signing_key, 1)
    return engine


def _insert(engine, record, install, network=None, when=None, status="new"):
    with engine.begin() as conn:
        if conn.execute(select(storage.installs.c.id).where(storage.installs.c.id == install)).first() is None:
            conn.execute(storage.installs.insert().values(id=install, created_at=storage.now(),
                                                          network=network or install, blocked=False))
        conn.execute(storage.battles.insert().values(
            battle_id=record["battle_id"], install_id=install, network=network or install,
            received_at=when or storage.now(), model_version=record["model_version"],
            opponent=record["opponent"], status=status, payload=json.dumps(record)))


def _cfg(**kw):
    cfg = pipeline.RoundConfig(**kw)
    cfg.gating.min_battles = 200
    cfg.gating.min_installs = 20
    return cfg


def _keys(signing_key):
    return {1: signing_key.public_key()}


def test_waits_for_data(db, signing_key):
    summary = pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg())
    assert summary["result"] == "waiting for data"


def test_tampered_model_row_stops_the_round(db, signing_key, baseline):
    evil = baseline.copy()
    evil.b2[spec.OUT_RETREAT] = 50.0
    with db.begin() as conn:
        row = conn.execute(select(storage.models.c.bundle)).scalar()
        key_id, _, signature = bundle.parse(bytes(row))
        forged = bundle._HEAD.pack(bundle.MAGIC, 1, key_id, len(evil.to_bytes())) + evil.to_bytes() + \
            len(signature).to_bytes(2, "little") + signature
        conn.execute(update(storage.models).values(bundle=forged))
    with pytest.raises(bundle.BundleError):
        pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg())


def _honest_population(engine, model, installs=40, per_install=40, seed=10, exploration=0.2):
    rng = np.random.default_rng(seed)
    explorer = model.copy()
    explorer.knobs[spec.KNOB_IDS["exploration"]] = exploration
    selfplay = []
    for i in range(installs):
        install = str(uuid.UUID(int=i + 1, version=4))
        selfplay.append(install)
        for _ in range(per_install):
            _insert(engine, toyenv.play(explorer, rng), install, network=f"net{i}")
    return tuple(selfplay)


@pytest.mark.slow
def test_full_round_releases_a_verified_better_model(db, signing_key, baseline):
    installs = _honest_population(db, baseline)
    summary = pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg(selfplay_installs=installs))
    assert summary["accepted"] == 1600 and summary["rejected"] == 0
    assert summary["result"] == "released v2", summary
    with db.connect() as conn:
        data = conn.execute(select(storage.models.c.bundle).where(storage.models.c.version == 2)).scalar()
    released = Model.from_bytes(bundle.verify(bytes(data), _keys(signing_key)))
    assert toyenv.mean_return(released, battles=1500) > toyenv.mean_return(baseline, battles=1500)


def test_forger_is_rejected_and_blocked(db, signing_key, baseline):
    rng = np.random.default_rng(20)
    for _ in range(15):
        record = toyenv.play(baseline, rng)
        for decision in record["decisions"]:  # Made-up choices, made-up confidence.
            for head in ("s", "c"):
                if head in decision:
                    allowed = [i for i, ok in enumerate(decision[head]["m"]) if ok]
                    decision[head].update(a=int(rng.choice(allowed)), p=0.9)
            if "r" in decision:
                decision["r"].update(a=1, p=0.9)
            if "g" in decision:
                decision["g"].update(a=int(rng.integers(len(decision["g"]["c"]))), p=0.9)
        record["won"], record["retreated"] = True, False
        record["enemy_end_power"] = 0.0
        _insert(db, record, "bot")
    summary = pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg())
    assert summary["rejected"] == 15
    with db.connect() as conn:
        blocked, reason = conn.execute(select(storage.installs.c.blocked, storage.installs.c.blocked_reason)
                                       .where(storage.installs.c.id == "bot")).one()
    assert blocked and "15/15" in reason


def test_honest_player_is_never_blocked(db, signing_key, baseline):
    rng = np.random.default_rng(21)
    for _ in range(30):
        _insert(db, toyenv.play(baseline, rng), "honest")
    pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg())
    with db.connect() as conn:
        assert not conn.execute(select(storage.installs.c.blocked).where(storage.installs.c.id == "honest")).scalar()


def test_junk_payload_is_rejected_not_fatal(db, signing_key):
    with db.begin() as conn:
        conn.execute(storage.installs.insert().values(id="junk", created_at=storage.now(), network="n", blocked=False))
        conn.execute(storage.battles.insert().values(
            battle_id="x", install_id="junk", network="n", received_at=storage.now(), model_version=1,
            opponent="ai", status="new", payload="{not json"))
    summary = pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg())
    assert summary["rejected"] == 1


@pytest.mark.slow
def test_outcome_poisoner_cannot_make_the_ai_worse(db, signing_key, baseline):
    """The hard case: an attacker who knows the model, logs *correct* probabilities, and lies only
    about outcomes - claiming big wins whenever the AI retreated or held its reserves back.
    2,500 records from one install, alongside the in-cluster self-play data."""
    installs = _honest_population(db, baseline, seed=30)
    rng = np.random.default_rng(31)
    poisoned = 0
    while poisoned < 2500:
        record = toyenv.play(baseline, rng)
        bad = any(d.get("c", {}).get("a") == 0 or d.get("r", {}).get("a") == 1 for d in record["decisions"])
        if not bad:
            continue
        record.update(won=True, retreated=False, enemy_end_power=0.0, end_power=record["start_power"])
        for copy_index in range(4):
            clone = dict(record, battle_id=str(uuid.uuid4()))
            _insert(db, clone, "attacker", network="attacker-net")
            poisoned += 1

    cfg = _cfg(selfplay_installs=installs)
    summary = pipeline.run_round(db, signing_key, 1, _keys(signing_key), cfg)
    if summary["result"].startswith("released"):
        version = int(summary["result"].split("v")[-1])
        with db.connect() as conn:
            data = conn.execute(select(storage.models.c.bundle).where(storage.models.c.version == version)).scalar()
        released = Model.from_bytes(bundle.verify(bytes(data), _keys(signing_key)))
        assert toyenv.mean_return(released, battles=1500) >= toyenv.mean_return(baseline, battles=1500) - 0.01
        assert summary["gate"]["metrics"]["canary"] == {"stance": 1.0, "retreat": 1.0}
    else:
        assert "rejected" in summary["result"]
    assert summary["rejected"] == 0  # It's the outcomes that lie; every probability checks out.


def test_retention_deletes_old_battles(db, signing_key, baseline):
    rng = np.random.default_rng(40)
    _insert(db, toyenv.play(baseline, rng), "old", when=storage.now() - dt.timedelta(days=200))
    _insert(db, toyenv.play(baseline, rng), "new")
    pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg())
    with db.connect() as conn:
        assert [i for (i,) in conn.execute(select(storage.battles.c.install_id))] == ["new"]


def _arena(engine, version, wins, games=50):
    with engine.begin() as conn:
        conn.execute(storage.arena.insert().values(received_at=storage.now(), install_id="sp", version=version,
                                                   opponent="admiral", games=games, wins=wins, draws=0))


def test_self_play_regression_rolls_back_forward(db, signing_key, baseline):
    worse = baseline.copy()
    worse.version = 2
    worse.b2[spec.OUT_COMMIT] = [2.0, 0.0, 0.0]
    with db.begin() as conn:
        pipeline.publish(conn, worse, "released", {}, signing_key, 1)
    _arena(db, 1, wins=35)
    _arena(db, 2, wins=20)
    summary = pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg())
    assert summary["reverted"] == 3
    assert summary["current"] == 3
    with db.connect() as conn:
        statuses = dict(conn.execute(select(storage.models.c.version, storage.models.c.status)).all())
        data = conn.execute(select(storage.models.c.bundle).where(storage.models.c.version == 3)).scalar()
    assert statuses == {1: "released", 2: "revoked", 3: "released"}
    restored = Model.from_bytes(bundle.verify(bytes(data), _keys(signing_key)))
    np.testing.assert_array_equal(restored.w2, baseline.rounded().w2)


def test_records_from_revoked_model_still_count(db, signing_key, baseline):
    other = baseline.copy()
    other.version = 2
    with db.begin() as conn:
        pipeline.publish(conn, other, "revoked", {}, signing_key, 1)
    rng = np.random.default_rng(41)
    _insert(db, toyenv.play(other, rng), "p")
    summary = pipeline.run_round(db, signing_key, 1, _keys(signing_key), _cfg())
    assert summary["accepted"] == 1


def test_rejected_candidates_are_unsigned(db, signing_key, baseline):
    with db.begin() as conn:
        candidate = baseline.copy()
        candidate.version = 9
        pipeline.publish(conn, candidate, "rejected", {})
        data = conn.execute(select(storage.models.c.bundle).where(storage.models.c.status == "rejected")).scalar()
    with pytest.raises(bundle.BundleError):
        bundle.verify(bytes(data), _keys(signing_key))
