"""Adds up the ga-probe experiment (GrandAdmiral/k8s/experiments/probe.yaml): per test commander, how often
the Grand Admiral beat Admiral and by how much, from the GASTAT lines in the probe pods' logs.

    python GrandAdmiral/tools/probe-report.py [--namespace oawga-machine-learning] [--json out.json]
"""

import argparse
import json
import math
import subprocess
from collections import defaultdict


def kubectl(*args):
    return subprocess.run(["kubectl", *args], capture_output=True, text=True, check=False).stdout


def collect(namespace, label="ga-probe"):
    pods = json.loads(kubectl("-n", namespace, "get", "pods", "-l", f"experiment={label}", "-o", "json") or '{"items":[]}')
    battles = defaultdict(list)
    for pod in pods["items"]:
        if pod["status"].get("phase") not in ("Succeeded", "Failed", "Running"):
            continue
        commander = None
        for line in kubectl("-n", namespace, "logs", pod["metadata"]["name"], "-c", "probe").splitlines():
            if line.startswith("PROBE commander="):
                commander = line.split("=", 1)[1].split()[0]
            elif "GASTAT " in line:
                try:
                    stat = json.loads(line.split("GASTAT ", 1)[1])
                except ValueError:
                    continue
                battles[stat.get("probe") or commander or "?"].append(stat)
    return battles


def summarise(stats):
    n = len(stats)
    wins = sum(s["result"] == "win" for s in stats)
    draws = sum(s["result"] == "draw" for s in stats)
    margins = [s["ga_left"] - s["admiral_left"] for s in stats]
    mean = sum(margins) / n
    sd = math.sqrt(sum((m - mean) ** 2 for m in margins) / max(1, n - 1))
    p = wins / n
    return {"battles": n, "win_pct": round(100 * p, 1), "win_se": round(100 * math.sqrt(p * (1 - p) / n), 1),
            "draws": draws, "margin": round(mean, 3), "margin_se": round(sd / math.sqrt(n), 3),
            "campaign_style": sum(bool(s.get("campaign_style")) for s in stats)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--namespace", default="oawga-machine-learning")
    parser.add_argument("--json")
    parser.add_argument("--label", default="ga-probe", help="experiment label: ga-probe or ga-matchup")
    args = parser.parse_args()
    battles = collect(args.namespace, args.label)
    rows = {name: summarise(stats) for name, stats in battles.items() if stats}
    order = sorted(rows, key=lambda k: (k != "model", -rows[k]["win_pct"]))
    print(f"{'commander':<18}{'battles':>8}{'win %':>8}{'+-':>6}{'margin':>9}{'+-':>7}")
    for name in order:
        r = rows[name]
        print(f"{name:<18}{r['battles']:>8}{r['win_pct']:>8}{r['win_se']:>6}{r['margin']:>9}{r['margin_se']:>7}")
    if args.json:
        with open(args.json, "w") as f:
            json.dump(rows, f, indent=2)


if __name__ == "__main__":
    main()
