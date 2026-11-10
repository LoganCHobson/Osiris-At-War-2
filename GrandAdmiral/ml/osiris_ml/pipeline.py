"""One training round, as the nightly trainer CronJob runs it.

    1. Load every released model, checking each bundle's signature (the DB is not trusted either).
    2. Deep-check new uploads against the model version they claim; accept or reject each one.
       Installs whose uploads keep failing are blocked, and the API stops taking their data.
    3. Build the training pool (recent accepted battles), split train / held-out by battle id.
    4. Weight records so no install or network dominates; self-play installs are trusted.
    5. Train a candidate one conservative step on from the current release.
    6. Gate it (gate.py). Pass: sign and release. Fail: keep it, with the report, for a human to read.
    7. Delete battle data older than the retention window.
"""

import datetime as dt
import hashlib
import json
import logging
from dataclasses import dataclass, field

import numpy as np
from sqlalchemy import and_, case, delete, func, select, update

from . import bundle, dataset, gate, spec, storage, train
from .model import Model
from .records import BattleRecord

log = logging.getLogger("grand-admiral.trainer")


@dataclass
class RoundConfig:
    window_days: int = 30
    max_pool: int = 60000
    holdout_share: float = 0.2
    retention_days: int = 90
    max_install_share: float = 0.02
    max_per_install: int = 40
    block_after: int = 10              # judge an install once it has this many checked uploads...
    block_reject_share: float = 0.5    # ...and block it if at least this share were rejected
    selfplay_installs: tuple = ()
    selfplay_exploration: float = 0.2
    min_arena_games: int = 40
    training: train.TrainConfig = field(default_factory=train.TrainConfig)
    gating: gate.GateConfig = field(default_factory=gate.GateConfig)


def _holdout(battle_id, share):
    digest = hashlib.sha256(battle_id.encode()).digest()
    return digest[0] / 256.0 < share


def load_released(conn, public_keys, statuses=("released", "revoked")):
    """Every model players may have run (revoked ones too: their records are still honest)."""
    models = {}
    rows = conn.execute(select(storage.models.c.version, storage.models.c.bundle)
                        .where(storage.models.c.status.in_(statuses))).all()
    for version, data in rows:
        model_bytes = bundle.verify(bytes(data), public_keys)  # Raises if anyone tampered with the DB.
        model = Model.from_bytes(model_bytes)
        if model.version != version:
            raise bundle.BundleError(f"model row {version} holds version {model.version}")
        models[version] = model
    return models


def check_new(conn, models, cfg: RoundConfig, batch=5000):
    """Deep-check every 'new' upload. Returns (accepted, rejected) counts."""
    accepted = rejected = 0
    while True:
        rows = conn.execute(select(storage.battles.c.id, storage.battles.c.payload, storage.battles.c.install_id)
                            .where(storage.battles.c.status == "new").order_by(storage.battles.c.id).limit(batch)).all()
        if not rows:
            break
        for row_id, payload, install_id in rows:
            reason = None
            try:
                record = BattleRecord.model_validate_json(payload)
            except ValueError as exc:
                reason = f"schema: {str(exc)[:200]}"
            else:
                trusted = install_id in cfg.selfplay_installs
                reason = dataset.deep_check(record, models,
                                            extra_exploration=(cfg.selfplay_exploration,) if trusted else ())
            conn.execute(update(storage.battles).where(storage.battles.c.id == row_id)
                         .values(status="rejected" if reason else "accepted", reason=reason))
            if reason:
                rejected += 1
            else:
                accepted += 1
    block_bad_installs(conn, cfg)
    return accepted, rejected


def block_bad_installs(conn, cfg: RoundConfig):
    b = storage.battles.c
    counts = conn.execute(
        select(b.install_id, func.count(), func.sum(case((b.status == "rejected", 1), else_=0)))
        .where(b.status != "new").group_by(b.install_id)).all()
    blocked = []
    for install_id, total, bad in counts:
        if install_id in cfg.selfplay_installs:
            continue
        if total >= cfg.block_after and (bad or 0) / total >= cfg.block_reject_share:
            conn.execute(update(storage.installs).where(and_(storage.installs.c.id == install_id,
                                                             storage.installs.c.blocked.is_(False)))
                         .values(blocked=True, blocked_reason=f"{bad}/{total} uploads failed checks"))
            blocked.append(install_id)
    if blocked:
        log.warning("blocked %d installs", len(blocked))
    return blocked


def training_pool(conn, cfg: RoundConfig):
    b = storage.battles.c
    since = storage.now() - dt.timedelta(days=cfg.window_days)
    rows = conn.execute(select(b.payload, b.install_id, b.network)
                        .where(and_(b.status == "accepted", b.received_at >= since))
                        .order_by(b.id.desc()).limit(cfg.max_pool)).all()
    rows.reverse()
    records = [BattleRecord.model_validate_json(payload) for payload, _, _ in rows]
    return records, [i for _, i, _ in rows], [n for _, _, n in rows]


def weigh(installs_, networks, cfg: RoundConfig):
    """Per-record weights: the stricter of the per-install and per-network caps. Self-play data
    is trusted ground truth and gets full weight."""
    by_install = dataset.contribution_weights(installs_, cfg.max_install_share, cfg.max_per_install)
    by_network = dataset.contribution_weights(networks, cfg.max_install_share * 2, cfg.max_per_install * 3)
    weights = np.minimum(by_install, by_network)
    trusted = np.array([i in cfg.selfplay_installs for i in installs_], dtype=bool)
    weights[trusted] = 1.0
    return weights


def current_versions(conn):
    return [v for (v,) in conn.execute(select(storage.models.c.version).where(storage.models.c.status == "released"))]


def arena_for(conn, version):
    a = storage.arena.c
    row = conn.execute(select(func.sum(a.games), func.sum(a.wins), func.sum(a.draws))
                       .where(and_(a.version == version, a.opponent == "admiral"))).one()
    games, wins, draws = row
    if not games:
        return None
    return (wins + 0.5 * (draws or 0)) / games


def publish(conn, model, status, report, private_key=None, key_id=None):
    data = model.to_bytes()
    signed = bundle.sign(data, private_key, key_id) if private_key is not None else data
    conn.execute(storage.models.insert().values(
        version=model.version, created_at=storage.now(), status=status, bundle=signed,
        sha256=hashlib.sha256(signed).hexdigest(), report=json.dumps(report)))


def revert_if_regressed(conn, models, private_key, key_id, cfg: RoundConfig):
    """Self-play safety net. If the newest release wins fewer Unity AI-vs-AI battles against
    Admiral than the release before it, re-release the previous weights under a new version
    number (clients never accept an older version, so a rollback has to move forward)."""
    released = sorted(current_versions(conn))
    if len(released) < 2:
        return None
    latest, previous = released[-1], released[-2]
    a = storage.arena.c
    games = conn.execute(select(func.sum(a.games)).where(a.version == latest)).scalar() or 0
    if games < cfg.min_arena_games:
        return None
    now_score, before = arena_for(conn, latest), arena_for(conn, previous)
    if before is None or now_score >= before - cfg.gating.arena_tolerance:
        return None
    restored = models[previous].copy()
    restored.version = (conn.execute(select(func.max(storage.models.c.version))).scalar() or latest) + 1
    conn.execute(update(storage.models).where(storage.models.c.version == latest).values(status="revoked"))
    publish(conn, restored, "released", {"reverted_from": latest, "restores": previous,
                                         "arena": {"latest": now_score, "previous": before}}, private_key, key_id)
    models[restored.version] = restored
    log.warning("v%d lost ground in self-play (%.2f vs %.2f): released v%d = v%d", latest, now_score, before,
                restored.version, previous)
    return restored.version


def run_round(engine, private_key, key_id, public_keys, cfg: RoundConfig = RoundConfig()):
    """Returns a summary dict (also logged)."""
    summary = {}
    with engine.begin() as conn:
        models = load_released(conn, public_keys)
        if not models:
            raise RuntimeError("no released model: run `osiris-ml bootstrap` first")
        reverted = revert_if_regressed(conn, models, private_key, key_id, cfg)
        if reverted:
            summary["reverted"] = reverted
        current = models[max(current_versions(conn))]
        summary["current"] = current.version
        summary["accepted"], summary["rejected"] = check_new(conn, models, cfg)

    with engine.begin() as conn:
        records, install_ids, networks = training_pool(conn, cfg)
        weights = weigh(install_ids, networks, cfg)
        held = np.array([_holdout(r.battle_id, cfg.holdout_share) for r in records], dtype=bool)
        train_records = [r for r, h in zip(records, held) if not h]
        hold_records = [r for r, h in zip(records, held) if h]
        summary["pool"] = len(records)
        summary["installs"] = len(set(install_ids))

        if len(train_records) < cfg.gating.min_battles * (1 - cfg.holdout_share):
            summary["result"] = "waiting for data"
            log.info("round: %s", summary)
            _retention(conn, cfg)
            return summary

        candidate = train.train(current, dataset.build(train_records, weights[~held]), cfg.training).rounded()
        candidate.version = max(models) + 1
        latest = conn.execute(select(func.max(storage.models.c.version))).scalar() or 0
        candidate.version = max(candidate.version, latest + 1)

        report = gate.evaluate(current, candidate, dataset.build(hold_records, weights[held]),
                               installs=summary["installs"], cfg=cfg.gating)
        summary["gate"] = report.to_dict()
        if report.passed:
            publish(conn, candidate, "released", report.to_dict(), private_key, key_id)
            summary["result"] = f"released v{candidate.version}"
        else:
            publish(conn, candidate, "rejected", report.to_dict())  # Unsigned: can never be served.
            summary["result"] = "candidate rejected: " + "; ".join(report.reasons)
        _retention(conn, cfg)
    log.info("round: %s", summary)
    return summary


def _retention(conn, cfg: RoundConfig):
    cutoff = storage.now() - dt.timedelta(days=cfg.retention_days)
    conn.execute(delete(storage.battles).where(storage.battles.c.received_at < cutoff))
    conn.execute(delete(storage.arena).where(storage.arena.c.received_at < cutoff))
