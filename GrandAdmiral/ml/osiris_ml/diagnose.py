"""Why isn't training moving the model? A read-only look at the live pool: nothing is published.

Three questions, each answered on the same split the training round uses (held-out battles are never
trained on):

1. Can the battle situation predict how the battle ends? The critic is fit on the training split and
   scored on the held-out one. If it can't (R^2 near 0), every decision's advantage is mostly noise
   and the policy gradient has nothing to follow.
2. Is there a consistent direction to learn in? The policy gradient at the released model, computed
   separately on two disjoint halves of the battles. Pointing the same way (cosine well above 0) means
   a real signal; near 0 means the two halves disagree, i.e. noise.

   Reference, from the toy battles in toyenv.py (where training does learn): outcome R^2 ~0.83, and
   gradient agreement ~0.4 at 1,000 battles, ~0.5 at 4,000 and ~0.8 at 13,000 (agreement grows with
   the data, as the noise averages out).
3. Is the training maths holding the model in place? The released model trained with no KL stop,
   then with the anchor off, then also without the PPO clip, then with a bigger learning rate. For
   each: how far it moved (KL on held-out data) and the gate's own estimate of the gain.
"""

import dataclasses
import json
import logging
import time

import numpy as np

from . import dataset, gate, pipeline, spec, train

log = logging.getLogger("grand-admiral.diagnose")


def _weighted_r2(pred, target, w):
    mean = np.average(target, weights=w)
    total = np.average((target - mean) ** 2, weights=w)
    resid = np.average((target - pred) ** 2, weights=w)
    return float(1 - resid / total) if total > 0 else 0.0


def predictability(model, train_ds, hold_ds):
    critic = train.fit_critic(model, train_ds)
    out, _ = critic.forward(hold_ds.x)
    v = out[:, spec.OUT_VALUE]
    r = hold_ds.returns
    w = hold_ds.weights
    # Where in each battle a decision falls: 0 at its first decision, 1 at its last.
    phase = np.zeros(len(r))
    for b in np.unique(hold_ds.battle):
        rows = np.flatnonzero(hold_ds.battle == b)
        phase[rows] = np.linspace(0, 1, len(rows)) if len(rows) > 1 else 1.0
    by_phase = {}
    for lo, hi, name in ((0.0, 0.25, "first quarter"), (0.25, 0.75, "middle"), (0.75, 1.01, "last quarter")):
        m = (phase >= lo) & (phase < hi)
        by_phase[name] = round(_weighted_r2(v[m], r[m], w[m]), 4) if m.any() else None
    first = np.array([np.flatnonzero(hold_ds.battle == b)[0] for b in np.unique(hold_ds.battle)])
    return {
        "r2_all_decisions": round(_weighted_r2(v, r, w), 4),
        "r2_by_phase": by_phase,
        "outcome_spread_std": round(float(np.sqrt(np.average((r - np.average(r, weights=w)) ** 2, weights=w))), 4),
        "first_decision_corr": round(float(np.corrcoef(v[first], r[first])[0, 1]), 4) if len(first) > 2 else None,
    }


def _flat(grads):
    return np.concatenate([g.ravel() for g in grads])


def gradient_agreement(model, train_ds, cfg: train.TrainConfig, splits=3, seed=0):
    critic = train.fit_critic(model, train_ds)
    adv = train.advantages(critic, train_ds, cfg.gae_lambda)
    anchor = [p.copy() for p in model.params()]
    rng = np.random.default_rng(seed)
    battles = np.unique(train_ds.battle)
    cosines = []
    for _ in range(splits):
        half = set(rng.permutation(battles)[: len(battles) // 2].tolist())
        in_a = np.isin(train_ds.battle, list(half))
        g = []
        for rows in (np.flatnonzero(in_a), np.flatnonzero(~in_a)):
            # Only the policy: the value row is refit by regression, not by gradient.
            _, grads = train.loss_and_grads(model, anchor, train_ds, adv, rows, dataclasses.replace(cfg, anchor=0.0))
            grads[2][spec.OUT_VALUE] = 0.0
            grads[3][spec.OUT_VALUE] = 0.0
            g.append(_flat(grads))
        cos = float(g[0] @ g[1] / (np.linalg.norm(g[0]) * np.linalg.norm(g[1]) + 1e-12))
        cosines.append(round(cos, 4))
    return {"cosine_between_halves": cosines, "mean": round(float(np.mean(cosines)), 4)}


# Second run (2026-11-19): the first showed the per-decision clip freezes training (KL ~0.003 with
# both limits on, ~1.7 with no clip at all). So: widen the clip, keep the real KL stop (0.04) and the
# anchor, and see which width lets training reach the KL cap with a held-out gain that holds up.
VARIANTS = [
    # Third run (2026-11-21), after hiding the AI's own orders from the scoring critic and judging
    # gains by battle outcome (lam 1): does training still find gains the stricter gate accepts?
    ("as configured (lam 0.9), KL stop 0.04", {"max_kl": 0.04}),
    ("trainer lam 1.0, KL stop 0.04", {"max_kl": 0.04, "gae_lambda": 1.0}),
]


def training_variants(model, train_ds, hold_ds, cfg: pipeline.RoundConfig, epochs, seconds, trusted):
    offline = dataclasses.replace(cfg.gating, require_arena=False, max_kl=1e9)
    results = []
    for name, changes in VARIANTS:
        tc = dataclasses.replace(cfg.training, **{"max_kl": 0.0, "epochs": epochs, "max_seconds": seconds, **changes})
        started = time.monotonic()
        candidate = train.train(model, train_ds, tc)
        report = gate.evaluate(model, candidate, hold_ds, installs=0, cfg=offline, trusted_battles=trusted)
        on_train = gate.evaluate(model, candidate, train_ds.subset(np.unique(train_ds.battle)[:4000]), installs=0,
                                 cfg=dataclasses.replace(offline, bootstrap=50), trusted_battles=trusted)
        kl = report.metrics.get("kl", {})
        row = {
            "variant": name,
            "minutes": round((time.monotonic() - started) / 60, 1),
            "largest_kl": round(max(kl.values(), default=0.0), 4),
            "kl": {k: round(v, 4) for k, v in kl.items()},
            "held_out_gain": round(report.metrics.get("improvement", 0.0), 4),
            "held_out_gain_lower_bound": round(report.metrics.get("improvement_lower", 0.0), 4),
            "training_split_gain": round(on_train.metrics.get("improvement", 0.0), 4),
        }
        log.info("diagnose: %s", json.dumps(row))
        results.append(row)
    return results


def run(engine, public_keys, cfg: pipeline.RoundConfig, epochs=40, seconds=600):
    with engine.begin() as conn:
        models = pipeline.load_released(conn, public_keys)
        current = models[max(pipeline.current_versions(conn))]
        records, install_ids, networks = pipeline.training_pool(conn, cfg)
    weights = pipeline.weigh(install_ids, networks, cfg)
    held = np.array([pipeline._holdout(r.battle_id, cfg.holdout_share) for r in records], dtype=bool)
    train_records = [r for r, h in zip(records, held) if not h]
    hold_records = [r for r, h in zip(records, held) if h]
    trusted = sum(1 for i in install_ids if i in cfg.selfplay_installs)
    summary = {"model": current.version, "battles": len(records)}
    del records
    train_ds = dataset.merge(train_records, weights[~held], consume=True)
    hold_ds = dataset.merge(hold_records, weights[held], consume=True)
    del train_records, hold_records
    summary["decisions"] = int(len(train_ds.x) + len(hold_ds.x))
    log.info("diagnose: v%d, %d battles, %d decisions", current.version, summary["battles"], summary["decisions"])

    summary["1_predictability"] = predictability(current, train_ds, hold_ds)
    log.info("diagnose: predictability %s", json.dumps(summary["1_predictability"]))
    summary["2_gradient_agreement"] = gradient_agreement(current, train_ds, cfg.training)
    log.info("diagnose: gradient agreement %s", json.dumps(summary["2_gradient_agreement"]))
    summary["3_training_variants"] = training_variants(current, train_ds, hold_ds, cfg, epochs, seconds, trusted)
    return summary


# ---- does the gate's estimate of a gain match what actually happened? ----
# The features include the AI's own standing orders (stance, formation, escort, manoeuvre, tempo). A
# critic that sees them can learn "pressing goes with winning" only because the AI presses when it is
# already winning; GAE then credits each order with the jump in V it causes by showing up in the next
# state, and the trainer and the gate both chase that. lam=1 (R - V(s_t)) has no such bootstrap.
ORDER_FEATURES = spec.OWN_ORDER_FEATURES


def _values(model, ds, fit_ds, drop_orders):
    """V(s) for ds from a ridge critic on `model`'s hidden layer, fit on fit_ds."""
    cols = [spec.FEATURES.index(f) for f in ORDER_FEATURES] if drop_orders else []

    def hidden(x):
        x = x.copy()
        x[:, cols] = 0.0
        _, (_, h) = model.forward(x)
        return np.hstack([h, np.ones((len(h), 1))])

    d = hidden(fit_ds.x)
    w = fit_ds.weights[:, None]
    coef = np.linalg.solve(d.T @ (d * w) + 1e-2 * np.eye(d.shape[1]), d.T @ (fit_ds.returns * fit_ds.weights))
    pred = hidden(ds.x) @ coef
    resid = ds.returns - pred
    r2 = 1 - np.average(resid ** 2, weights=ds.weights) / np.average(
        (ds.returns - np.average(ds.returns, weights=ds.weights)) ** 2, weights=ds.weights)
    return pred, float(r2)


def _gae(v, ds, lam, normalise=True):
    a = np.zeros_like(v)
    running = 0.0
    for i in range(len(v) - 1, -1, -1):
        last = i == len(v) - 1 or ds.battle[i + 1] != ds.battle[i]
        delta = (ds.returns[i] - v[i]) if last else (v[i + 1] - v[i])
        running = delta + (0.0 if last else lam * running)
        a[i] = running
    if not normalise:
        return a
    mean = np.average(a, weights=ds.weights)
    return (a - mean) / (np.sqrt(np.average((a - mean) ** 2, weights=ds.weights)) + 1e-6)


def _estimate(old, new, ds, adv, seed=0, boot=400):
    gain, weight = gate._per_battle_improvement(old, new, ds, adv)
    rng = np.random.default_rng(seed)
    n = len(gain)
    samples = []
    for _ in range(boot):
        pick = rng.integers(0, n, n)
        samples.append(gain[pick].sum() / max(weight[pick].sum(), 1e-9))
    return {"estimate": round(float(gain.sum() / max(weight.sum(), 1e-9)), 4),
            "lower": round(float(np.quantile(samples, 0.05)), 4),
            "upper": round(float(np.quantile(samples, 0.95)), 4)}


def estimator_check(models, train_ds, hold_ds, pairs):
    rows = []
    for old_v, new_v in pairs:
        if old_v not in models or new_v not in models:
            continue
        old, new = models[old_v], models[new_v]
        row = {"pair": f"v{old_v} -> v{new_v}"}
        row["as the gate does it (stored critic, lam 1.0)"] = _estimate(old, new, hold_ds, train.advantages(new, hold_ds, 1.0))
        for drop in (False, True):
            v, r2 = _values(new, hold_ds, train_ds, drop)
            tag = "critic without own orders" if drop else "critic with own orders"
            row[f"{tag}: held-out R2"] = round(r2, 4)
            for lam in (0.9, 0.5, 1.0):
                row[f"{tag}, lam {lam}"] = _estimate(old, new, hold_ds, _gae(v, hold_ds, lam))
            # In return units (no normalising), comparable with the actual change in mean return.
            row[f"{tag}, lam 1.0, return units"] = _estimate(old, new, hold_ds, _gae(v, hold_ds, 1.0, False))
        log.info("estimator: %s", json.dumps(row))
        rows.append(row)
    return rows


def run_estimator(engine, public_keys, cfg: pipeline.RoundConfig, pairs=((12, 33), (33, 34), (12, 34))):
    with engine.begin() as conn:
        models = pipeline.load_released(conn, public_keys)
        records, install_ids, networks = pipeline.training_pool(conn, cfg)
    weights = pipeline.weigh(install_ids, networks, cfg)
    held = np.array([pipeline._holdout(r.battle_id, cfg.holdout_share) for r in records], dtype=bool)
    train_records = [r for r, h in zip(records, held) if not h]
    hold_records = [r for r, h in zip(records, held) if h]
    del records
    train_ds = dataset.merge(train_records, weights[~held], consume=True)
    hold_ds = dataset.merge(hold_records, weights[held], consume=True)
    del train_records, hold_records
    log.info("estimator: %d held-out battles", hold_ds.n_battles)
    return {"held_out_battles": int(hold_ds.n_battles), "pairs": estimator_check(models, train_ds, hold_ds, pairs)}
