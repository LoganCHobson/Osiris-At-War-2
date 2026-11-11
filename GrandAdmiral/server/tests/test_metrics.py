"""The Prometheus exporter, against a SQLite database with a released model, some battles and arena
results in it."""

import json

import numpy as np
from prometheus_client import CollectorRegistry, generate_latest

from app.metrics import Collector
from osiris_ml import distill, pipeline, storage, toyenv


def _db(tmp_path):
    engine = storage.engine_for(f"sqlite:///{tmp_path / 'ga.db'}")
    storage.create_all(engine)
    model = distill.baseline(epochs=5).rounded()
    model.version = 1
    rng = np.random.default_rng(0)
    with engine.begin() as conn:
        pipeline.publish(conn, model, "released", {"metrics": {"improvement_lower": 0.01, "kl": {"stance": 0.002}}})
        for i, install in enumerate(["selfplay-1"] * 6 + ["player-1"] * 3):
            record = toyenv.play(model, rng)
            conn.execute(storage.installs.insert().prefix_with("OR IGNORE").values(
                id=install, created_at=storage.now(), network=install, blocked=False))
            conn.execute(storage.battles.insert().values(
                battle_id=record["battle_id"], install_id=install, network=install, received_at=storage.now(),
                model_version=1, opponent=record["opponent"], status="accepted" if i % 2 else "new",
                payload=json.dumps(record)))
        conn.execute(storage.arena.insert().values(received_at=storage.now(), install_id="selfplay-1", version=1,
                                                   opponent="admiral", games=20, wins=12, draws=2))
    return engine


def _scrape(engine):
    registry = CollectorRegistry()
    registry.register(Collector(engine, ["selfplay-1"]))
    return generate_latest(registry).decode()


def test_exports_battles_arena_models_and_orders(tmp_path):
    text = _scrape(_db(tmp_path))
    assert "ga_metrics_db_up 1.0" in text
    assert 'ga_battles_received{source="selfplay",window="1h"} 6.0' in text
    assert 'ga_battles_received{source="player",window="24h"} 3.0' in text
    assert 'ga_arena{result="wins",version="1"} 12.0' in text
    assert 'ga_arena{result="losses",version="1"} 6.0' in text
    assert 'ga_model_latest_version{status="released"} 1.0' in text
    assert 'ga_gate{metric="improvement_lower"} 0.01' in text
    assert 'ga_gate_kl{head="stance"} 0.002' in text
    assert 'ga_recent_battle_duration_seconds{source="selfplay",stat="count"} 6.0' in text
    shares = [line for line in text.splitlines() if line.startswith('ga_recent_order_share{action=') and 'head="formation"' in line]
    assert shares and abs(sum(float(line.rsplit(" ", 1)[1]) for line in shares) - 1.0) < 1e-6


def test_a_broken_database_reports_down_instead_of_crashing(tmp_path):
    engine = storage.engine_for(f"sqlite:///{tmp_path / 'missing' / 'nope.db'}")
    assert "ga_metrics_db_up 0.0" in _scrape(engine)
