"""The starting point: the Admiral's hand-written battle rules, distilled into the network.

Grand Admiral v1 plays like Admiral (the same thresholds AIBattleCommander uses), so on day one
it's never worse than the best existing AI; learning only ever moves it on from there. The same
rules also define the canary states the promotion gate uses: positions where any sane commander
knows the answer (press a 3:1 advantage, get out of a 1:6 rout) that no amount of training data
may talk the model out of.
"""

import numpy as np

from . import spec
from .model import Model
from .train import Adam

F = {name: i for i, name in enumerate(spec.FEATURES)}

PRESS_AT = 1.25
FALL_BACK_AT = 0.7
RETREAT_RATIO = 0.35
DEFENDED_RETREAT_FACTOR = 0.6
TARGET_SHARPNESS = 4.0


def sample_states(n, rng):
    x = np.zeros((n, spec.N_FEATURES))
    total = np.clip(rng.normal(0.0, 1.2, n), -3, 3)
    x[:, F["log_total_ratio"]] = total
    x[:, F["log_field_ratio"]] = np.clip(total + rng.normal(0.0, 0.5, n), -3, 3)
    x[:, F["ratio_trend"]] = np.clip(rng.normal(0.0, 0.2, n), -1, 1)
    x[:, F["our_health"]] = rng.uniform(0.2, 1.0, n)
    x[:, F["enemy_health"]] = rng.uniform(0.2, 1.0, n)
    x[:, F["our_reserve_share"]] = np.where(rng.random(n) < 0.5, 0.0, rng.uniform(0.0, 0.8, n))
    x[:, F["enemy_unseen_share"]] = np.where(rng.random(n) < 0.6, 0.0, rng.uniform(0.0, 0.7, n))
    x[:, F["our_losses"]] = rng.uniform(0.0, 0.9, n)
    x[:, F["enemy_losses"]] = rng.uniform(0.0, 0.9, n)
    x[:, F["log_dps_ratio"]] = np.clip(x[:, F["log_field_ratio"]] + rng.normal(0.0, 0.4, n), -3, 3)
    x[:, F["log_range_ratio"]] = np.clip(rng.normal(0.0, 0.4, n), -2, 2)
    x[:, F["center_distance"]] = rng.uniform(0.1, 2.0, n)
    x[:, F["our_strike_share"]] = rng.uniform(0.0, 0.5, n)
    x[:, F["enemy_strike_share"]] = rng.uniform(0.0, 0.5, n)
    x[:, F["enemy_capital_share"]] = rng.uniform(0.3, 1.0, n)
    x[:, F["pop_headroom"]] = rng.uniform(0.0, 1.0, n)
    x[:, F["time"]] = rng.uniform(0.0, 1.0, n)
    attacker = rng.random(n) < 0.5
    x[:, F["is_attacker"]] = attacker
    x[:, F["friendly_defenses"]] = (~attacker) & (rng.random(n) < 0.5)
    x[:, F["enemy_defenses"]] = attacker & (rng.random(n) < 0.5)
    x[:, F["enemy_retreating"]] = rng.random(n) < 0.05
    stance = rng.choice(3, n, p=[0.6, 0.2, 0.2])
    x[:, F["stance_press"]] = stance == 1
    x[:, F["stance_fallback"]] = stance == 2
    x[:, F["vs_player"]] = rng.random(n) < 0.5
    return x


def can_regroup(x):
    has_backup = (x[..., F["our_reserve_share"]] > 0) | (x[..., F["friendly_defenses"]] > 0.5)
    return has_backup & (x[..., F["is_attacker"]] < 0.5)


def stance_mask(x):
    mask = np.ones(x.shape[:-1] + (3,), dtype=bool)
    mask[..., 2] = can_regroup(x)
    return mask


def heuristic_stance(x):
    """AIBattleCommander.EvaluateStance for an Admiral with an aggression-1 personality."""
    field = np.exp(x[:, F["log_field_ratio"]])
    press = x[:, F["stance_press"]] > 0.5
    fallback = x[:, F["stance_fallback"]] > 0.5
    regroup = can_regroup(x)
    label = np.zeros(len(x), dtype=np.int64)
    label[press & (field >= PRESS_AT * 0.85)] = 1
    label[fallback & regroup & (field <= FALL_BACK_AT * 1.3)] = 2
    fresh = ~press & ~fallback
    label[fresh & (field >= PRESS_AT)] = 1
    label[fresh & (field <= FALL_BACK_AT) & regroup] = 2
    return label


def outlook(x):
    return np.exp(x[..., F["log_total_ratio"]])


def retreat_open(x):
    o = outlook(x)
    return (o >= spec.RETREAT_FORCED_BELOW) & (o <= spec.RETREAT_NEVER_ABOVE)


def heuristic_retreat(x):
    threshold = RETREAT_RATIO * np.where(x[:, F["friendly_defenses"]] > 0.5, DEFENDED_RETREAT_FACTOR, 1.0)
    return (outlook(x) < threshold).astype(np.int64)


def value_prior(x):
    return 0.9 * np.tanh(x[:, F["log_total_ratio"]])


def baseline(n_hidden=32, samples=40000, epochs=60, seed=7, knobs=None):
    """Grand Admiral model v1: the Admiral rules in network form."""
    rng = np.random.default_rng(seed)
    model = Model(n_hidden=n_hidden, version=1, seed=seed, knobs=knobs)
    model.w2[spec.OUT_TARGET] = 0.0
    model.b2[spec.OUT_TARGET] = 0.0
    model.b2[spec.OUT_TARGET.start] = TARGET_SHARPNESS  # Score = 4 x heuristic log-priority.
    model.b2[spec.OUT_COMMIT] = [0.0, 3.0, 0.0]         # Commit like Admiral ("match").

    x = sample_states(samples, rng)
    stance = heuristic_stance(x)
    smask = stance_mask(x)
    retreat = heuristic_retreat(x)
    open_ = retreat_open(x)
    value = value_prior(x)

    optimiser = Adam(model.params(), 3e-3)
    trainable = np.zeros(spec.N_OUT, dtype=bool)
    trainable[spec.OUT_STANCE] = True
    trainable[spec.OUT_RETREAT] = True
    trainable[spec.OUT_VALUE] = True

    for _ in range(epochs):
        order = rng.permutation(samples)
        for start in range(0, samples, 1024):
            b = order[start:start + 1024]
            out, cache = model.forward(x[b])
            d = np.zeros_like(out)

            logits = np.where(smask[b], out[:, spec.OUT_STANCE], -np.inf)
            logits -= logits.max(axis=1, keepdims=True)
            p = np.where(smask[b], np.exp(logits), 0.0)
            p /= p.sum(axis=1, keepdims=True)
            target = np.zeros_like(p)
            target[np.arange(len(b)), stance[b]] = 1.0
            d[:, spec.OUT_STANCE] = (p - target) / len(b)

            q = 1.0 / (1.0 + np.exp(-out[:, spec.OUT_RETREAT]))
            weight = open_[b] * 3.0 + 0.05  # Mostly matters where retreat is actually on the table.
            d[:, spec.OUT_RETREAT] = weight * (q - retreat[b]) / len(b)

            d[:, spec.OUT_VALUE] = 0.5 * (out[:, spec.OUT_VALUE] - value[b]) / len(b)
            d[:, ~trainable] = 0.0

            grads = model.backward(cache, d)
            grads[2][~trainable] = 0.0
            grads[3][~trainable] = 0.0
            optimiser.step(model.params(), grads)
    return model


def canaries(n=4000, seed=99):
    """States with an obvious right answer: (x, stance mask, expected stance or -1, expected retreat
    or -1). The promotion gate requires a candidate to get these right."""
    rng = np.random.default_rng(seed)
    x = sample_states(n, rng)
    x[:, F["stance_press"]] = 0.0
    x[:, F["stance_fallback"]] = 0.0
    field = np.exp(x[:, F["log_field_ratio"]])
    total = outlook(x)
    regroup = can_regroup(x)

    stance = -np.ones(n, dtype=np.int64)
    stance[(field >= 2.5) & (total >= 2.0)] = 1
    stance[(field <= 0.35) & (total <= 0.5) & regroup] = 2

    retreat = -np.ones(n, dtype=np.int64)
    retreat[(total >= spec.RETREAT_FORCED_BELOW) & (total <= 0.18)] = 1
    retreat[(total >= 0.75) & (total <= spec.RETREAT_NEVER_ABOVE)] = 0
    return x, stance_mask(x), stance, retreat


def canary_scores(model, temperature=1.0):
    x, mask, stance, retreat = canaries()
    out, _ = model.forward(x)
    logits = np.where(mask, out[:, spec.OUT_STANCE], -np.inf)
    chosen = logits.argmax(axis=1)
    s = stance >= 0
    r = retreat >= 0
    p_retreat = 1.0 / (1.0 + np.exp(-out[:, spec.OUT_RETREAT] / temperature))
    return {
        "stance": float((chosen[s] == stance[s]).mean()),
        "retreat": float(((p_retreat[r] > 0.5) == (retreat[r] == 1)).mean()),
    }


def heuristic_agreement(model):
    """How closely a model matches the Admiral rules on random states (1.0 = identical)."""
    rng = np.random.default_rng(1234)
    x = sample_states(5000, rng)
    out, _ = model.forward(x)
    logits = np.where(stance_mask(x), out[:, spec.OUT_STANCE], -np.inf)
    stance = float((logits.argmax(axis=1) == heuristic_stance(x)).mean())
    o = retreat_open(x)
    retreat = float(((out[o, spec.OUT_RETREAT] > 0) == (heuristic_retreat(x)[o] == 1)).mean())
    return {"stance": stance, "retreat": retreat}


def log_priority(priorities):
    p = np.asarray(priorities, dtype=np.float64)
    return np.clip(np.log(np.maximum(p, 1e-9) / max(p.max(), 1e-9)), -5.0, 0.0)


__all__ = ["baseline", "canaries", "canary_scores", "heuristic_agreement", "sample_states", "log_priority",
           "heuristic_stance", "heuristic_retreat", "stance_mask", "retreat_open"]
