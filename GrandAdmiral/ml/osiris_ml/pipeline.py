"""One training round, as the nightly trainer CronJob runs it.

    1. Load every released model, checking each bundle's signature (the DB is not trusted either).
    2. Deep-check new uploads against the model version they claim; accept or reject each one.
       Installs whose uploads keep failing are blocked, and the API stops taking their data.
    3. Build the training pool (recent accepted battles from games at or above the balance floor),
       split train / held-out by battle id.
    4. Weight records so no install or network dominates; self-play installs are trusted.
    5. Train a candidate one conservative step on from the current release.
    6. Gate it (gate.py). Fail: keep it, with the report, for a human to read. Pass: sign and release
       it, or (require_arena) sign it as a *trial*: self-play plays trials, and a later round promotes
       the trial once it wins at least as often against Admiral as the release does, or fails it.
    7. Delete battle data older than the retention window.
"""

import dataclasses
import datetime as dt
import hashlib
import json
import logging
import re
from dataclasses import dataclass, field

import numpy as np
from sqlalchemy import and_, case, delete, func, select, update

from . import bundle, dataset, gate, spec, storage, train
from .model import Model
from .records import BattleRecord

log = logging.getLogger("grand-admiral.trainer")

POOL_BATCH = 50  # training-pool payloads held at once while reading them


@dataclass
class RoundConfig:
    window_days: int = 30
    max_pool: int = 60000
    holdout_share: float = 0.2
    retention_days: int = 90
    # Rebalance guard: battles from game versions below this are never trained on (or gated against).
    # Raise it to the new game version whenever a release changes ship stats or combat rules.
    min_client_version: str = ""
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


# Every signed model anyone may have played: players run releases (and once ran revoked ones), self-play
# also runs trials and the trials that failed. Their battle records are all honest data.
PLAYED = ("released", "revoked", "trial", "failed")


def load_released(conn, public_keys, statuses=PLAYED):
    """Every signed model some game may have played, by version (the DB is not trusted: each is verified)."""
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
    # Streamed, and each record turned into arrays as it arrives: holding every payload and parsed
    # record at once ran the trainer out of its 3Gi at 3,500 battles.
    # Options on the statement, not the connection: Connection.execution_options() would make every
    # later statement on it (retention's DELETE) a server-side cursor too, which Postgres rejects.
    rows = conn.execute(
        select(b.payload, b.install_id, b.network)
        .where(and_(b.status == "accepted", b.received_at >= since))
        .order_by(b.id.desc()).limit(cfg.max_pool)
        .execution_options(stream_results=True, yield_per=POOL_BATCH))
    battles, installs_, networks = [], [], []
    floor = version_key(cfg.min_client_version) if cfg.min_client_version else None
    stale = 0
    for payload, install, network in rows:
        record = BattleRecord.model_validate_json(payload)
        if floor is not None and version_key(record.client_version) < floor:
            stale += 1  # Fought under an older balance: what worked then may not work now.
            continue
        battles.append(dataset.compact(record))
        installs_.append(install)
        networks.append(network)
    if stale:
        log.info("skipped %d battles from game versions below %s", stale, cfg.min_client_version)
    battles.reverse(); installs_.reverse(); networks.reverse()  # Oldest first, as before.
    return battles, installs_, networks


def version_key(version):
    """Orders game versions numerically: 0.10.0 > 0.9.2 > 0.9. A non-numeric piece (a suffix like
    '-beta') counts as 0, and a version that doesn't start with a number ('toy-1') sorts below every
    real version, so odd versions never sneak above the floor."""
    if not version[:1].isdigit():
        return (-1,)
    parts = []
    for piece in re.split(r"[.\-+]", version):
        parts.append(int(piece) if piece.isdigit() else 0)
    while len(parts) > 1 and parts[-1] == 0:
        parts.pop()
    return tuple(parts)


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


def arena_games(conn, version):
    a = storage.arena.c
    return conn.execute(select(func.sum(a.games)).where(and_(a.version == version, a.opponent == "admiral"))).scalar() or 0


def resolve_trial(conn, current, cfg: RoundConfig):
    """The pending trial's fate. Returns (still pending: bool, what happened) once self-play has played
    it enough: promoted to released if it wins at least as often against Admiral as the current
    release (within the arena tolerance), failed otherwise."""
    m = storage.models.c
    trial = conn.execute(select(m.version).where(m.status == "trial").order_by(m.version.desc()).limit(1)).scalar()
    if trial is None:
        return False, None
    games = arena_games(conn, trial)
    if games < cfg.min_arena_games:
        return True, f"trial v{trial} awaiting self-play ({games}/{cfg.min_arena_games} games)"
    trial_score, current_score = arena_for(conn, trial), arena_for(conn, current.version)
    if current_score is None or trial_score >= current_score - cfg.gating.arena_tolerance:
        conn.execute(update(storage.models).where(m.version == trial).values(status="released"))
        return False, f"promoted trial v{trial} (self-play {trial_score:.2f} vs v{current.version} {current_score if current_score is not None else 'n/a'})"
    conn.execute(update(storage.models).where(m.version == trial).values(status="failed"))
    return False, f"failed trial v{trial} (self-play {trial_score:.2f} vs v{current.version} {current_score:.2f})"


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
        summary["accepted"], summary["rejected"] = check_new(conn, models, cfg)
        pending, trial = resolve_trial(conn, current, cfg)
        if trial:
            summary["trial"] = trial
        current = models[max(current_versions(conn))]  # A promoted trial is now the release to train from.
        summary["current"] = current.version

    if pending:
        summary["result"] = "waiting for self-play"
        log.info("round: %s", summary)
        with engine.begin() as conn:
            _retention(conn, cfg)
        return summary
    if cfg.gating.require_arena and arena_games_for(engine, current.version) < cfg.min_arena_games:
        # A trial is judged against the release's own self-play record, so that has to exist first.
        summary["result"] = f"waiting for self-play on v{current.version}"
        log.info("round: %s", summary)
        with engine.begin() as conn:
            _retention(conn, cfg)
        return summary

    with engine.begin() as conn:
        records, install_ids, networks = training_pool(conn, cfg)
        weights = weigh(install_ids, networks, cfg)
        held = np.array([_holdout(r.battle_id, cfg.holdout_share) for r in records], dtype=bool)
        train_records = [r for r, h in zip(records, held) if not h]
        hold_records = [r for r, h in zip(records, held) if h]
        summary["pool"] = len(records)
        summary["installs"] = len(set(i for i in install_ids if i not in cfg.selfplay_installs))  # players
        summary["selfplay_battles"] = sum(1 for i in install_ids if i in cfg.selfplay_installs)

        if len(train_records) < cfg.gating.min_battles * (1 - cfg.holdout_share):
            summary["result"] = "waiting for data"
            log.info("round: %s", summary)
            _retention(conn, cfg)
            return summary

        candidate = train.train(current, dataset.merge(train_records, weights[~held]), cfg.training).rounded()
        candidate.version = max(models) + 1
        latest = conn.execute(select(func.max(storage.models.c.version))).scalar() or 0
        candidate.version = max(candidate.version, latest + 1)

        offline = dataclasses.replace(cfg.gating, require_arena=False)  # Self-play judges trials later.
        report = gate.evaluate(current, candidate, dataset.merge(hold_records, weights[held]),
                               installs=summary["installs"], cfg=offline,
                               trusted_battles=summary["selfplay_battles"])
        summary["gate"] = report.to_dict()
        if report.passed and cfg.gating.require_arena:
            publish(conn, candidate, "trial", report.to_dict(), private_key, key_id)
            summary["result"] = f"trial v{candidate.version} sent to self-play"
        elif report.passed:
            publish(conn, candidate, "released", report.to_dict(), private_key, key_id)
            summary["result"] = f"released v{candidate.version}"
        else:
            publish(conn, candidate, "rejected", report.to_dict())  # Unsigned: can never be served.
            summary["result"] = "candidate rejected: " + "; ".join(report.reasons)
        _retention(conn, cfg)
    log.info("round: %s", summary)
    return summary


def arena_games_for(engine, version):
    with engine.connect() as conn:
        return arena_games(conn, version)


def _retention(conn, cfg: RoundConfig):
    cutoff = storage.now() - dt.timedelta(days=cfg.retention_days)
    conn.execute(delete(storage.battles).where(storage.battles.c.received_at < cutoff))
    conn.execute(delete(storage.arena).where(storage.arena.c.received_at < cutoff))
