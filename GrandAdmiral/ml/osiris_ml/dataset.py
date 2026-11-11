"""Turning validated battle records into training arrays, and the trainer's deep checks.

The API can only check that a record is well formed. The trainer can do much better: it has every
model ever released, so it recomputes the probability of each logged action under the model the
record claims to come from. Hand-made or replayed-from-another-version records don't match and
are thrown out, and an install that keeps sending them is cut off entirely.
"""

import math
from collections import defaultdict
from dataclasses import dataclass, field

import numpy as np

from . import policy, spec
from .records import BattleRecord

# How far a logged probability may sit from the recomputed one. JSON rounding and float32 maths
# account for a few percent; locally adapted clients are allowed more, but adaptation is clamped
# on the client (GAAdaptation.cs) so it can never get near this.
UNADAPTED_LOG_TOLERANCE = 0.05
ADAPTED_LOG_TOLERANCE = math.log(4.0)
MIN_MATCHING_SHARE = 0.9


def battle_return(record):
    """Reward for every decision in a battle, from the AI's point of view, in about [-1.5, 1.5].

    Power still standing (escaped ships count, so a good retreat beats a bad last stand) minus the
    enemy's, plus a bonus for actually winning. Computed here from raw numbers rather than trusted
    from the client.
    """
    ours = record.end_power / record.start_power
    theirs = record.enemy_end_power / record.enemy_start_power
    value = (ours - theirs) + (0.5 if record.won else -0.5)
    return float(np.clip(value, -1.5, 1.5))


def _head_probabilities(model, out, decision, temperature, epsilon):
    """(head, logged p, recomputed p) for every head this decision used."""
    found = []
    for name, (outputs, key) in spec.CATEGORICAL.items():
        choice = getattr(decision, key)
        if choice is not None:
            mixed, _ = policy.masked_softmax(out[outputs], choice.m, temperature, epsilon)
            found.append((name, choice.p, mixed[choice.a]))
    if decision.r is not None:
        p1, _ = policy.retreat_probability(out[spec.OUT_RETREAT], temperature, epsilon)
        found.append(("retreat", decision.r.p, p1 if decision.r.a == 1 else 1.0 - p1))
    if decision.g is not None:
        scores = policy.target_scores(out, decision.g.c)
        mixed, _ = policy.masked_softmax(scores, np.ones(len(decision.g.c), bool), temperature, epsilon)
        found.append(("target", decision.g.p, mixed[decision.g.a]))
    return found


def deep_check(record: BattleRecord, models, extra_exploration=()):
    """models: {version: Model}. Returns None if the record is believable, else the reason.
    extra_exploration: other exploration rates this source may use (trusted self-play)."""
    model = models.get(record.model_version)
    if model is None:
        return "unknown model version"
    if abs(record.temperature - model.knob("temperature")) > 1e-3:
        return "temperature doesn't match the model"
    allowed = (model.knob("exploration"),) + tuple(extra_exploration)
    if all(abs(record.exploration - e) > 1e-3 for e in allowed):
        return "exploration doesn't match the model"

    tolerance = ADAPTED_LOG_TOLERANCE if record.adapted else UNADAPTED_LOG_TOLERANCE
    out, _ = model.forward([d.x for d in record.decisions])
    checked = matching = 0
    for decision, row in zip(record.decisions, out):
        for _, logged, recomputed in _head_probabilities(model, row, decision, record.temperature, record.exploration):
            checked += 1
            if recomputed > 0 and abs(math.log(logged) - math.log(recomputed)) <= tolerance:
                matching += 1
    if checked == 0:
        return "no decisions"
    if matching / checked < MIN_MATCHING_SHARE:
        return f"only {matching}/{checked} logged probabilities match model v{record.model_version}"
    return None


@dataclass
class Head:
    rows: list = field(default_factory=list)
    actions: list = field(default_factory=list)
    mu: list = field(default_factory=list)
    masks: list = field(default_factory=list)
    contacts: list = field(default_factory=list)

    def arrays(self):
        result = {"rows": np.asarray(self.rows, dtype=np.int64),
                  "a": np.asarray(self.actions, dtype=np.int64),
                  "mu": np.asarray(self.mu, dtype=np.float64)}
        if self.masks:
            result["mask"] = np.asarray(self.masks, dtype=bool)
        if self.contacts:
            n = len(self.contacts)
            padded = np.zeros((n, spec.MAX_CONTACTS, spec.N_CONTACT))
            valid = np.zeros((n, spec.MAX_CONTACTS), dtype=bool)
            for i, rows in enumerate(self.contacts):
                padded[i, :len(rows)] = rows
                valid[i, :len(rows)] = True
            result["contacts"] = padded
            result["mask"] = valid
        return result


@dataclass
class Dataset:
    x: np.ndarray
    returns: np.ndarray
    weights: np.ndarray
    battle: np.ndarray            # which battle each row came from (for bootstrap by battle)
    heads: dict                   # head -> arrays
    temperature: np.ndarray
    epsilon: np.ndarray
    n_battles: int

    def subset(self, battles):
        keep = np.isin(self.battle, np.asarray(sorted(battles)))
        index = np.flatnonzero(keep)
        remap = -np.ones(len(self.x), dtype=np.int64)
        remap[index] = np.arange(len(index))
        heads = {}
        for name, arrays in self.heads.items():
            selected = keep[arrays["rows"]] if len(arrays["rows"]) else np.zeros(0, bool)
            heads[name] = {key: value[selected] for key, value in arrays.items()}
            heads[name]["rows"] = remap[heads[name]["rows"]]
        return Dataset(self.x[index], self.returns[index], self.weights[index], self.battle[index], heads,
                       self.temperature[index], self.epsilon[index], len(set(battles)))


def build(records, record_weights=None):
    """records: list of BattleRecord. record_weights: optional per-record sample weight."""
    xs, returns, weights, battle, temps, eps = [], [], [], [], [], []
    heads = {name: Head() for name in spec.HEADS}
    for b, record in enumerate(records):
        reward = battle_return(record)
        weight = 1.0 if record_weights is None else float(record_weights[b])
        for decision in record.decisions:
            row = len(xs)
            xs.append(decision.x)
            returns.append(reward)
            weights.append(weight)
            battle.append(b)
            temps.append(record.temperature)
            eps.append(record.exploration)
            for name, (_, key) in spec.CATEGORICAL.items():
                choice = getattr(decision, key)
                if choice is not None:
                    h = heads[name]
                    h.rows.append(row); h.actions.append(choice.a); h.mu.append(choice.p); h.masks.append(choice.m)
            if decision.r is not None:
                h = heads["retreat"]
                h.rows.append(row); h.actions.append(decision.r.a); h.mu.append(decision.r.p)
            if decision.g is not None:
                h = heads["target"]
                h.rows.append(row); h.actions.append(decision.g.a); h.mu.append(decision.g.p); h.contacts.append(decision.g.c)

    head_arrays = {}
    for name, head in heads.items():
        arrays = head.arrays()
        if name == "target" and "contacts" not in arrays:
            arrays["contacts"] = np.zeros((0, spec.MAX_CONTACTS, spec.N_CONTACT))
            arrays["mask"] = np.zeros((0, spec.MAX_CONTACTS), bool)
        if name in spec.CATEGORICAL and "mask" not in arrays:
            arrays["mask"] = np.zeros((0, 3), bool)
        head_arrays[name] = arrays

    return Dataset(np.asarray(xs, dtype=np.float64).reshape(-1, spec.N_FEATURES), np.asarray(returns),
                   np.asarray(weights), np.asarray(battle, dtype=np.int64), head_arrays,
                   np.asarray(temps), np.asarray(eps), len(records))


def contribution_weights(sources, max_share=0.02, max_per_source=40):
    """Per-record weights that stop any one install (or network) from dominating a training round.

    sources: list of source keys (install id, or install id + network) per record. Each source's
    records share a total weight of min(count, max_per_source), then every source is capped at
    max_share of the round. With 50 honest players and one bot uploading 10,000 fake battles, the
    bot gets the same say as any one of the players.
    """
    counts = defaultdict(int)
    for source in sources:
        counts[source] += 1
    budget = {s: min(c, max_per_source) for s, c in counts.items()}
    total = sum(budget.values())
    if total == 0:
        return np.zeros(0)
    cap = max(max_share * total, 1.0)
    # A single source can't hold more than its share, unless there are too few sources to share.
    if len(counts) * cap >= total:
        budget = {s: min(b, cap) for s, b in budget.items()}
    return np.asarray([budget[s] / counts[s] for s in sources])
