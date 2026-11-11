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

    shielded = rng.random(n) < 0.6
    shield = np.where(shielded, rng.uniform(0.0, 1.0, n), 0.0)
    x[:, F["our_shield"]] = shield
    x[:, F["shield_trend"]] = np.where(shielded, np.clip(rng.normal(-0.05, 0.15, n), -1, 1), 0.0)
    x[:, F["low_shield_share"]] = np.where(shielded, np.clip((0.5 - shield) * 1.5 + rng.normal(0, 0.1, n), 0, 1), 0.0)
    x[:, F["missile_pressure"]] = np.where(rng.random(n) < 0.5, 0.0, rng.uniform(0.0, 2.0, n))
    x[:, F["pd_idle_share"]] = rng.uniform(0.0, 1.0, n)
    x[:, F["enemy_craft_ratio"]] = np.where(rng.random(n) < 0.3, 0.0, rng.uniform(0.0, 2.5, n))
    x[:, F["bombers_on_capitals"]] = np.where(x[:, F["enemy_craft_ratio"]] > 0, rng.uniform(0.0, 0.8, n), 0.0)
    x[:, F["bow_alignment"]] = rng.uniform(0.0, 1.0, n)
    x[:, F["los_blocked_share"]] = rng.uniform(0.0, 0.7, n)
    x[:, F["cover_between"]] = np.where(rng.random(n) < 0.5, 0.0, rng.uniform(0.0, 0.8, n))
    x[:, F["cover_near"]] = rng.random(n) < 0.4
    x[:, F["enemy_waves"]] = rng.integers(1, 5, n) / 5
    x[:, F["since_enemy_wave"]] = rng.uniform(0.0, 2.0, n)
    has_flagship = rng.random(n) < 0.85
    x[:, F["flagship_health"]] = np.where(has_flagship, rng.uniform(0.1, 1.0, n), 0.0)
    x[:, F["flagship_under_fire"]] = np.where(has_flagship, rng.uniform(0.0, 0.7, n), 0.0)
    x[:, F["kited"]] = rng.random(n) < 0.15
    x[:, F["closing_rate"]] = np.clip(rng.normal(0.0, 0.2, n), -1, 1)
    x[:, F["our_spread"]] = rng.uniform(0.1, 1.5, n)
    x[:, F["enemy_spread"]] = rng.uniform(0.1, 1.8, n)
    x[:, F["log_speed_ratio"]] = np.clip(rng.normal(0.0, 0.4, n), -2, 2)
    x[:, F["damage_exchange"]] = np.clip(x[:, F["log_field_ratio"]] * 0.8 + rng.normal(0.0, 0.6, n), -3, 3)
    for middle, last in (("formation_tight", "formation_loose"), ("escort_screen", "escort_hunt"),
                         ("maneuver_flank", "maneuver_envelop"), ("tempo_rotate", "tempo_pulse")):
        current = rng.choice(3, n, p=[0.6, 0.2, 0.2])
        x[:, F[middle]] = current == 1
        x[:, F[last]] = current == 2
    x[:, F["tempo_rotate"]] *= shielded
    x[:, F["tempo_pulse"]] *= shielded
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


# ---- the playbook for the schema 2 orders ----
# Each rule reads only battle-state features, so v1 can learn it exactly. Each has a little
# hysteresis (the current order is a feature) so the fleet doesn't flip orders every decision.

def _current(x, middle, last):
    """0 / 1 / 2 from a pair of one-hot 'current order' features."""
    return np.where(x[..., F[middle]] > 0.5, 1, np.where(x[..., F[last]] > 0.5, 2, 0))


def formation_mask(x):
    return np.ones(x.shape[:-1] + (3,), dtype=bool)


def heuristic_formation(x):
    """Tight under missile or bomber pressure (overlapping point defence); loose when our own ships
    block each other's shots; otherwise standard."""
    current = _current(x, "formation_tight", "formation_loose")
    hold = np.where(current == 1, 0.7, 1.0)  # Once tight, stay tight a little longer.
    pressure = ((x[:, F["missile_pressure"]] >= 0.5 * hold) | (x[:, F["bombers_on_capitals"]] >= 0.3 * hold)
                | (x[:, F["enemy_craft_ratio"]] >= 1.2 * hold))
    blocked = x[:, F["los_blocked_share"]] >= np.where(current == 2, 0.25, 0.35)
    return np.where(pressure, 1, np.where(blocked, 2, 0)).astype(np.int64)


def escort_mask(x):
    return np.ones(x.shape[:-1] + (3,), dtype=bool)  # The game masks screen/hunt when it has no escorts.


def heuristic_escort(x):
    """Screen the capitals when bombers are on them or the flagship is being swarmed; hunt craft
    when the sky is full of them and the capitals are safe; otherwise hold the line."""
    current = _current(x, "escort_screen", "escort_hunt")
    bombers = x[:, F["bombers_on_capitals"]]
    craft = x[:, F["enemy_craft_ratio"]]
    screen = ((bombers >= np.where(current == 1, 0.15, 0.25))
              | ((x[:, F["flagship_under_fire"]] >= 0.4) & (craft >= 0.5) & (x[:, F["flagship_health"]] > 0)))
    hunt = (craft >= np.where(current == 2, 0.55, 0.75)) & (bombers < 0.25)
    return np.where(screen, 1, np.where(hunt, 2, 0)).astype(np.int64)


def maneuver_mask(x):
    mask = np.ones(x.shape[:-1] + (3,), dtype=bool)
    falling_back = x[..., F["stance_fallback"]] > 0.5
    mask[..., 1] = ~falling_back
    mask[..., 2] = ~falling_back
    return mask


def heuristic_maneuver(x):
    """Envelop a compact enemy we badly outnumber; flank when we're stronger and at least as fast,
    or when asteroids block the straight approach. Never while kited (close the gap instead),
    falling back, or assaulting defences (the objective decides the approach)."""
    field = np.exp(x[:, F["log_field_ratio"]])
    current = _current(x, "maneuver_flank", "maneuver_envelop")
    lean = np.where(current > 0, 0.85, 1.0)  # A wing already out stays out a little longer.
    free = (x[:, F["kited"]] < 0.5) & (x[:, F["stance_fallback"]] < 0.5) & (x[:, F["enemy_defenses"]] < 0.5)
    envelop = free & (field >= 2.0 * lean) & (x[:, F["enemy_spread"]] < 0.9)
    flank = free & (((field >= 1.4 * lean) & (x[:, F["log_speed_ratio"]] >= -0.2) & (x[:, F["enemy_spread"]] < 1.3))
                    | ((x[:, F["cover_between"]] >= 0.4) & (field >= 1.0 * lean)))
    return np.where(envelop, 2, np.where(flank, 1, 0)).astype(np.int64)


def tempo_mask(x):
    mask = np.ones(x.shape[:-1] + (3,), dtype=bool)
    shielded = x[..., F["our_shield"]] > 0
    mask[..., 1] = shielded
    mask[..., 2] = shielded
    return mask


def heuristic_tempo(x):
    """Shield rhythm: pulse the whole line back while shields are collapsing in an even fight the
    enemy isn't running from; rotate drained ships out when only some are low; otherwise hold.
    Never while kited: stepping back only lets the enemy keep shooting from range."""
    field = np.exp(x[:, F["log_field_ratio"]])
    shield = x[:, F["our_shield"]]
    shielded = shield > 0
    current = _current(x, "tempo_rotate", "tempo_pulse")
    calm = (x[:, F["kited"]] < 0.5) & (x[:, F["enemy_retreating"]] < 0.5)
    pulse = (shielded & calm & (shield < np.where(current == 2, 0.45, 0.3)) & (x[:, F["shield_trend"]] <= 0.02)
             & (field >= 0.7) & (field <= 1.6))
    rotate = shielded & calm & (x[:, F["low_shield_share"]] >= np.where(current == 1, 0.1, 0.15)) & (field >= 0.8)
    return np.where(pulse, 2, np.where(rotate, 1, 0)).astype(np.int64)


# head -> (label rule, mask rule). Commit has no rule: it's set straight to Admiral's "match".
PLAYBOOK = {
    "stance": (lambda x: heuristic_stance(x), lambda x: stance_mask(x)),
    "formation": (heuristic_formation, formation_mask),
    "escort": (heuristic_escort, escort_mask),
    "maneuver": (heuristic_maneuver, maneuver_mask),
    "tempo": (heuristic_tempo, tempo_mask),
}


def value_prior(x):
    return 0.9 * np.tanh(x[:, F["log_total_ratio"]])


def baseline(n_hidden=64, samples=40000, epochs=100, seed=7, knobs=None):
    """Grand Admiral model v1: the Admiral rules in network form."""
    rng = np.random.default_rng(seed)
    model = Model(n_hidden=n_hidden, version=1, seed=seed, knobs=knobs)
    model.w2[spec.OUT_TARGET] = 0.0
    model.b2[spec.OUT_TARGET] = 0.0
    model.b2[spec.OUT_TARGET.start] = TARGET_SHARPNESS  # Score = 4 x heuristic log-priority.
    model.b2[spec.OUT_COMMIT] = [0.0, 3.0, 0.0]         # Commit like Admiral ("match").

    x = sample_states(samples, rng)
    labels = {name: (rule(x), mask(x)) for name, (rule, mask) in PLAYBOOK.items()}
    retreat = heuristic_retreat(x)
    open_ = retreat_open(x)
    value = value_prior(x)

    optimiser = Adam(model.params(), 3e-3)
    trainable = np.zeros(spec.N_OUT, dtype=bool)
    for name in PLAYBOOK:
        trainable[spec.CATEGORICAL[name][0]] = True
    trainable[spec.OUT_RETREAT] = True
    trainable[spec.OUT_VALUE] = True

    for _ in range(epochs):
        order = rng.permutation(samples)
        for start in range(0, samples, 1024):
            b = order[start:start + 1024]
            out, cache = model.forward(x[b])
            d = np.zeros_like(out)

            for name, (label, mask) in labels.items():
                outputs = spec.CATEGORICAL[name][0]
                logits = np.where(mask[b], out[:, outputs], -np.inf)
                logits -= logits.max(axis=1, keepdims=True)
                p = np.where(mask[b], np.exp(logits), 0.0)
                p /= p.sum(axis=1, keepdims=True)
                target = np.zeros_like(p)
                target[np.arange(len(b)), label[b]] = 1.0
                d[:, outputs] = (p - target) / len(b)

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
    result = {}
    for name, (rule, mask) in PLAYBOOK.items():
        logits = np.where(mask(x), out[:, spec.CATEGORICAL[name][0]], -np.inf)
        result[name] = float((logits.argmax(axis=1) == rule(x)).mean())
    o = retreat_open(x)
    result["retreat"] = float(((out[o, spec.OUT_RETREAT] > 0) == (heuristic_retreat(x)[o] == 1)).mean())
    return result


def log_priority(priorities):
    p = np.asarray(priorities, dtype=np.float64)
    return np.clip(np.log(np.maximum(p, 1e-9) / max(p.max(), 1e-9)), -5.0, 0.0)


__all__ = ["baseline", "canaries", "canary_scores", "heuristic_agreement", "sample_states", "log_priority",
           "heuristic_stance", "heuristic_retreat", "stance_mask", "retreat_open", "PLAYBOOK",
           "heuristic_formation", "heuristic_escort", "heuristic_maneuver", "heuristic_tempo"]
