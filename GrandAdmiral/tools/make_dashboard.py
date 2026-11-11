"""Writes k8s/grafana-dashboard.yaml: the Grand Admiral dashboard as a ConfigMap the homelab's Grafana
picks up by itself (label grafana_dashboard: "1"). Edit here and re-run, not the YAML.

    python GrandAdmiral/tools/make_dashboard.py

Prometheus panels read the exporter (server/app/metrics.py) and kube-state-metrics; Loki panels read
the self-play farm's per-battle GASTAT log lines (Assets/Scripts/Dev/DifficultySim.cs).
"""

import json
import pathlib

NS = "oawga-machine-learning"
PROM = {"type": "prometheus", "uid": "prometheus"}
LOKI = {"type": "loki", "uid": "loki"}
GASTAT = f'{{namespace="{NS}", container="selfplay"}} |= "GASTAT " | regexp `GASTAT (?P<stat>\\{{.*\\}})` | line_format "{{{{.stat}}}}" | json'

panels = []
_id = [0]


def _next():
    _id[0] += 1
    return _id[0]


def row(title, y):
    panels.append({"type": "row", "title": title, "id": _next(), "collapsed": False,
                   "gridPos": {"h": 1, "w": 24, "x": 0, "y": y}})


def stat(title, expr, x, y, w=4, h=4, unit="short", source=PROM, decimals=None, description="", alert_above=None):
    """A single number. Plain counts and versions are blue (no good or bad value); alert_above makes it
    green up to that value and red above it."""
    if alert_above is None:
        color = {"color": {"mode": "fixed", "fixedColor": "blue"}}
    else:
        color = {"color": {"mode": "thresholds"},
                 "thresholds": {"mode": "absolute", "steps": [{"color": "green", "value": None},
                                                              {"color": "red", "value": alert_above}]}}
    p = {"type": "stat", "title": title, "id": _next(), "datasource": source, "description": description,
         "gridPos": {"h": h, "w": w, "x": x, "y": y},
         "fieldConfig": {"defaults": {"unit": unit, **color, **({"decimals": decimals} if decimals is not None else {})},
                         "overrides": []},
         "options": {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
                     "colorMode": "value", "graphMode": "area", "textMode": "auto"},
         "targets": [{"refId": "A", "expr": expr, "datasource": source, **({"queryType": "range"} if source is LOKI else {})}]}
    panels.append(p)


def series(title, targets, x, y, w=12, h=8, unit="short", source=PROM, stack=False, description="", bars=False):
    p = {"type": "timeseries", "title": title, "id": _next(), "datasource": source, "description": description,
         "gridPos": {"h": h, "w": w, "x": x, "y": y},
         "fieldConfig": {"defaults": {"unit": unit, "custom": {
             "drawStyle": "bars" if bars else "line", "fillOpacity": 25 if stack or bars else 8, "lineWidth": 2,
             "stacking": {"mode": "normal" if stack else "none", "group": "A"}, "showPoints": "never"}}, "overrides": []},
         "options": {"legend": {"displayMode": "list", "placement": "bottom"}, "tooltip": {"mode": "multi"}},
         "targets": [{"refId": chr(65 + i), "expr": expr, "legendFormat": legend, "datasource": source,
                      **({"queryType": "range"} if source is LOKI else {})}
                     for i, (expr, legend) in enumerate(targets)]}
    panels.append(p)


def bars(title, expr, legend, x, y, w=6, h=8, unit="percentunit", description=""):
    panels.append({"type": "bargauge", "title": title, "id": _next(), "datasource": PROM, "description": description,
                   "gridPos": {"h": h, "w": w, "x": x, "y": y},
                   "fieldConfig": {"defaults": {"unit": unit, "min": 0, "max": 1}, "overrides": []},
                   "options": {"orientation": "horizontal", "displayMode": "gradient", "showUnfilled": True,
                               "reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False}},
                   "targets": [{"refId": "A", "expr": expr, "legendFormat": legend, "datasource": PROM, "instant": True}]})


# ---- overview ----
row("Overview", 0)
stat("Battles, last hour", 'sum(ga_battles_received{window="1h"})', 0, 1)
stat("Battles, last 24h", 'sum(ga_battles_received{window="24h"})', 4, 1)
stat("Battles stored", "sum(ga_battles)", 8, 1)
stat("Released model", 'ga_model_latest_version{status="released"}', 12, 1, decimals=0)
stat("Trial in self-play", 'ga_model_latest_version{status="trial"}', 16, 1, decimals=0,
     description="A candidate that passed the offline checks and is being played by self-play before release.")
stat("Self-play pods", f'kube_deployment_status_replicas_available{{namespace="{NS}", deployment="grand-admiral-selfplay"}}', 20, 1,
     decimals=0)

# ---- self-play farm (Loki) ----
row("Self-play farm", 5)
series("Battles finished per hour", [(f"sum(count_over_time({GASTAT} [1h]))", "battles / h")], 0, 6, w=8, source=LOKI)
series("Real time per battle", [(f"avg(avg_over_time({GASTAT} | unwrap wall_seconds [30m]))", "mean"),
                                (f"max(quantile_over_time(0.9, {GASTAT} | unwrap wall_seconds [30m]))", "p90")],
       8, 6, w=8, unit="s", source=LOKI, description="Wall-clock seconds per self-play battle (at -simSpeed 6).")
series("Battle length in game time", [(f"avg(avg_over_time({GASTAT} | unwrap game_seconds [30m]))", "mean")],
       16, 6, w=8, unit="s", source=LOKI)
series("Results (Grand Admiral as Avanti vs Admiral, equal cost)",
       [(f"sum by (result) (count_over_time({GASTAT} [1h]))", "{{result}}")], 0, 14, w=8, source=LOKI, stack=True, bars=True)
series("Fleet left at the end", [(f"avg(avg_over_time({GASTAT} | unwrap ga_left [1h]))", "Grand Admiral"),
                                 (f"avg(avg_over_time({GASTAT} | unwrap admiral_left [1h]))", "Admiral")],
       8, 14, w=8, unit="percentunit", source=LOKI)
series("Farm CPU and memory", [
    (f'sum(rate(container_cpu_usage_seconds_total{{namespace="{NS}", container="selfplay"}}[5m]))', "CPU cores"),
    (f'sum(container_memory_working_set_bytes{{namespace="{NS}", container="selfplay"}}) / 1e9', "memory GB")],
    16, 14, w=8)

# ---- learning ----
row("Learning", 22)
series("Self-play win rate per model", [
    ('(ga_arena{result="wins"} + 0.5 * ga_arena{result="draws"}) / ignoring(result) ga_arena{result="games"}', "v{{version}}")],
    0, 23, w=8, unit="percentunit", description="What the trainer compares: wins + half the draws, per model version.")
series("Self-play games per model", [('ga_arena{result="games"}', "v{{version}}")], 8, 23, w=8)
series("Gate: estimated improvement (lower bound)", [('ga_gate{metric="improvement_lower"}', "lower bound"),
                                                     ('ga_gate{metric="improvement"}', "estimate")],
       16, 23, w=8, description="The newest candidate's offline estimate. Above 0 = passes that check.")
series("Gate: step size per decision (KL)", [("ga_gate_kl", "{{head}}")], 0, 31, w=8,
       description="How far the newest candidate moved each decision from the release. Limit 0.05.")
series("Models by status", [("ga_models", "{{status}}")], 8, 31, w=8, stack=True)
series("Battles by status", [("sum by (status) (ga_battles)", "{{status}}")], 16, 31, w=8, stack=True)

# ---- behaviour ----
row("How Grand Admiral fights (recent battles)", 39)
for i, head in enumerate(("stance", "formation", "escort", "maneuver")):
    bars(f"{head.capitalize()} orders", f'ga_recent_order_share{{head="{head}"}}', "{{action}}", i * 6, 40)
for i, head in enumerate(("tempo", "commit")):
    bars(f"{head.capitalize()} orders", f'ga_recent_order_share{{head="{head}"}}', "{{action}}", i * 6, 48)
series("Decisions per battle", [("ga_recent_decisions_per_battle", "{{source}}")], 12, 48, w=6)
series("Battle length, recent uploads", [('ga_recent_battle_duration_seconds{stat="mean"}', "{{source}} mean"),
                                         ('ga_recent_battle_duration_seconds{stat="p90"}', "{{source}} p90")],
       18, 48, w=6, unit="s")

# ---- players ----
row("Players", 56)
stat("Installs", 'sum(ga_installs{blocked="false"})', 0, 57)
stat("Blocked installs", 'sum(ga_installs{blocked="true"})', 4, 57, alert_above=1,
     description="Installs cut off for sending forged or broken battle records. Red once any are.")
series("Player battles received", [('ga_battles_received{source="player", window="24h"}', "last 24h")], 8, 57, w=8)
series("Player outcomes (recent)", [('ga_recent_outcomes{source="player"}', "{{outcome}}")], 16, 57, w=8, stack=True)

dashboard = {
    "uid": "grand-admiral", "title": "Grand Admiral", "tags": ["osiris", "grand-admiral"],
    "timezone": "browser", "schemaVersion": 39, "version": 1, "refresh": "1m",
    "time": {"from": "now-24h", "to": "now"}, "panels": panels,
}

out = pathlib.Path(__file__).resolve().parents[1] / "k8s" / "grafana-dashboard.yaml"
body = json.dumps(dashboard, indent=1)
out.write_text(
    "# Generated by GrandAdmiral/tools/make_dashboard.py - edit that and re-run.\n"
    "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: grand-admiral-dashboard\n"
    "  labels: { grafana_dashboard: \"1\" }\n  annotations: { grafana_folder: Osiris }\n"
    "data:\n  grand-admiral.json: |\n" + "\n".join("    " + line for line in body.splitlines()) + "\n",
    encoding="utf-8", newline="\n")
print(f"wrote {out} ({len(panels)} panels)")
