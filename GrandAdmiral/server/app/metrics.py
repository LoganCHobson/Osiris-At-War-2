"""Prometheus exporter for Grand Admiral: everything the dashboard shows, read from the database.

    python -m app.metrics            (GA_METRICS_DB_URL, GA_SELFPLAY_INSTALLS, GA_METRICS_PORT=9100)

Runs as its own Deployment with a read-only database role (ga_metrics), separate from the
internet-facing API, so the API keeps its narrow grants and the numbers are the same however many
API replicas there are. Values are computed per scrape (cached briefly) from the battles, arena
results and models tables; recent-battle statistics come from the newest uploads' payloads.
"""

import datetime as dt
import json
import logging
import os
import threading
import time
from collections import Counter

from prometheus_client import start_http_server
from prometheus_client.core import GaugeMetricFamily, REGISTRY
from sqlalchemy import func, select

from osiris_ml import spec, storage

log = logging.getLogger("grand-admiral.metrics")

RECENT = 500           # newest battles whose payloads feed the "recent" statistics (~120 kB each)
PAYLOAD_BATCH = 50     # payloads held in memory at once while reading them
CACHE_SECONDS = 15
ORDER_KEYS = {name: key for name, (_, key) in spec.CATEGORICAL.items()}
ACTIONS = {"stance": spec.STANCES, "commit": spec.COMMITS, "formation": spec.FORMATIONS,
           "escort": spec.ESCORTS, "maneuver": spec.MANEUVERS, "tempo": spec.TEMPOS}


def _source(install, selfplay):
    return "selfplay" if install in selfplay else "player"


def _quantile(values, q):
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(q * (len(ordered) - 1) + 0.5))]


class Collector:
    def __init__(self, engine, selfplay):
        self.engine = engine
        self.selfplay = set(selfplay)
        self.lock = threading.Lock()
        self.cached_at = 0.0
        self.cached = []

    def collect(self):
        with self.lock:
            if time.time() - self.cached_at > CACHE_SECONDS:
                try:
                    self.cached = list(self._families())
                    self.cached_at = time.time()
                except Exception:  # A database blip shouldn't kill the exporter; report it instead.
                    log.exception("metrics query failed")
                    up = GaugeMetricFamily("ga_metrics_db_up", "1 if the last database query worked")
                    up.add_metric([], 0)
                    return [up]
            return self.cached

    def _families(self):
        b, a, m, i = storage.battles.c, storage.arena.c, storage.models.c, storage.installs.c
        with self.engine.connect() as conn:
            up = GaugeMetricFamily("ga_metrics_db_up", "1 if the last database query worked")
            up.add_metric([], 1)
            yield up

            # Battles by status, opponent and source, all time and recently.
            total = GaugeMetricFamily("ga_battles", "Battle records stored", labels=["status", "opponent", "source"])
            counts = Counter()
            for status, opponent, install, n in conn.execute(
                    select(b.status, b.opponent, b.install_id, func.count()).group_by(b.status, b.opponent, b.install_id)):
                counts[(status, opponent, _source(install, self.selfplay))] += n
            for labels, n in counts.items():
                total.add_metric(list(labels), n)
            yield total

            recent = GaugeMetricFamily("ga_battles_received", "Battles received in the last window", labels=["window", "source"])
            for window, seconds in (("1h", 3600), ("24h", 86400)):
                since = storage.now() - dt.timedelta(seconds=seconds)
                per = Counter()
                for install, n in conn.execute(
                        select(b.install_id, func.count()).where(b.received_at >= since).group_by(b.install_id)):
                    per[_source(install, self.selfplay)] += n
                for source in ("selfplay", "player"):
                    recent.add_metric([window, source], per[source])
            yield recent

            installs = GaugeMetricFamily("ga_installs", "Game installs registered", labels=["blocked"])
            for blocked, n in conn.execute(select(i.blocked, func.count()).group_by(i.blocked)):
                installs.add_metric(["true" if blocked else "false"], n)
            yield installs

            # Models and the trainer's last verdict.
            models = GaugeMetricFamily("ga_models", "Models by status", labels=["status"])
            for status, n in conn.execute(select(m.status, func.count()).group_by(m.status)):
                models.add_metric([status], n)
            yield models
            latest = GaugeMetricFamily("ga_model_latest_version", "Newest model version by status", labels=["status"])
            for status, v in conn.execute(select(m.status, func.max(m.version)).group_by(m.status)):
                latest.add_metric([status], v or 0)
            yield latest

            row = conn.execute(select(m.version, m.status, m.report).where(m.status.in_(("released", "rejected", "candidate")))
                               .order_by(m.version.desc()).limit(1)).first()
            gate = GaugeMetricFamily("ga_gate", "The newest candidate's promotion-gate numbers", labels=["metric"])
            kl = GaugeMetricFamily("ga_gate_kl", "The newest candidate's step size per head (KL)", labels=["head"])
            if row is not None and row.report:
                try:
                    report = json.loads(row.report)
                    metrics = report.get("metrics", report.get("gate", {}).get("metrics", {})) or {}
                    gate.add_metric(["version"], row.version)
                    gate.add_metric(["passed"], 1 if row.status == "released" else 0)
                    for key in ("improvement", "improvement_lower", "battles", "installs"):
                        if isinstance(metrics.get(key), (int, float)):
                            gate.add_metric([key], metrics[key])
                    for head, value in (metrics.get("kl") or {}).items():
                        kl.add_metric([head], value)
                except ValueError:
                    pass
            yield gate
            yield kl

            # Self-play arena: Grand Admiral vs Admiral, per model version.
            arena = GaugeMetricFamily("ga_arena", "Self-play results per model version", labels=["version", "result"])
            for version, games, wins, draws in conn.execute(
                    select(a.version, func.sum(a.games), func.sum(a.wins), func.sum(a.draws)).group_by(a.version)):
                arena.add_metric([str(version), "games"], games or 0)
                arena.add_metric([str(version), "wins"], wins or 0)
                arena.add_metric([str(version), "draws"], draws or 0)
                arena.add_metric([str(version), "losses"], (games or 0) - (wins or 0) - (draws or 0))
            yield arena

            # Recent battles in detail, from their payloads. Streamed in small batches: a self-play payload
            # carries every decision's features, and 2000 of them at once (230 MB of JSON) ran the exporter out of memory.
            rows = conn.execution_options(stream_results=True, yield_per=PAYLOAD_BATCH).execute(
                select(b.install_id, b.payload).order_by(b.id.desc()).limit(RECENT))
            yield from self._recent(rows)

    def _recent(self, rows):
        durations = {"selfplay": [], "player": []}
        decisions = {"selfplay": [], "player": []}
        outcomes = Counter()
        orders = Counter()
        order_totals = Counter()
        for install, payload in rows:
            try:
                record = json.loads(payload)
            except ValueError:
                continue
            source = _source(install, self.selfplay)
            durations[source].append(float(record.get("duration", 0)))
            decisions[source].append(len(record.get("decisions", [])))
            outcome = "win" if record.get("won") else "retreat" if record.get("retreated") else "loss"
            outcomes[(source, outcome)] += 1
            for decision in record.get("decisions", []):
                for head, key in ORDER_KEYS.items():
                    choice = decision.get(key)
                    if isinstance(choice, dict) and isinstance(choice.get("a"), int) and 0 <= choice["a"] < 3:
                        orders[(head, ACTIONS[head][choice["a"]])] += 1
                        order_totals[head] += 1

        duration = GaugeMetricFamily("ga_recent_battle_duration_seconds",
                                     "In-game battle length over recent battles", labels=["source", "stat"])
        per_battle = GaugeMetricFamily("ga_recent_decisions_per_battle", "Grand Admiral decisions per recent battle",
                                       labels=["source"])
        for source in ("selfplay", "player"):
            values = durations[source]
            for stat, value in (("mean", sum(values) / len(values) if values else 0.0), ("p50", _quantile(values, 0.5)),
                                ("p90", _quantile(values, 0.9)), ("count", len(values))):
                duration.add_metric([source, stat], value)
            per_battle.add_metric([source], sum(decisions[source]) / len(decisions[source]) if decisions[source] else 0.0)
        yield duration
        yield per_battle

        outcome = GaugeMetricFamily("ga_recent_outcomes", "How recent battles ended for Grand Admiral",
                                    labels=["source", "outcome"])
        for (source, result), n in outcomes.items():
            outcome.add_metric([source, result], n)
        yield outcome

        share = GaugeMetricFamily("ga_recent_order_share", "How often each order is given, over recent battles",
                                  labels=["head", "action"])
        for (head, action), n in orders.items():
            share.add_metric([head, action], n / order_totals[head])
        yield share


def main():
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
    url = os.environ["GA_METRICS_DB_URL"]
    selfplay = tuple(x for x in os.environ.get("GA_SELFPLAY_INSTALLS", "").split(",") if x)
    REGISTRY.register(Collector(storage.engine_for(url), selfplay))
    port = int(os.environ.get("GA_METRICS_PORT", "9100"))
    start_http_server(port)
    log.info("serving metrics on :%d", port)
    while True:
        time.sleep(3600)


if __name__ == "__main__":
    main()
