"""Should a candidate model replace the one players have? Every check must pass.

1. Enough evidence: minimum battles and distinct installs in the round.
2. It's better: estimated improvement on *held-out* battles (never trained on), with the lower
   end of a bootstrap confidence interval above zero. Resampled by battle, since decisions within
   a battle share one outcome.
3. It's a small step: mean KL divergence from the released model per head stays under a limit.
   Even data that fooled check 2 can only move behaviour a little per release.
4. It's sane: canary states (press a crushing advantage, escape a rout) still come out right.
5. Self-play, when available: in Unity AI-vs-AI battles it still beats Admiral at least as often as
   the current release does, within a tolerance.
"""

from dataclasses import asdict, dataclass, field

import numpy as np

from . import distill, spec
from .dataset import Dataset
from .train import advantages, head_terms


@dataclass
class GateConfig:
    min_battles: int = 200
    min_installs: int = 20
    bootstrap: int = 400
    confidence: float = 0.95
    min_improvement: float = 0.0
    max_kl: float = 0.05
    min_canary: float = 0.95
    arena_tolerance: float = 0.05
    require_arena: bool = False
    seed: int = 0


@dataclass
class GateReport:
    passed: bool
    reasons: list = field(default_factory=list)
    metrics: dict = field(default_factory=dict)

    def to_dict(self):
        return asdict(self)


def _per_battle_improvement(old, new, ds: Dataset):
    """Each held-out battle's estimated advantage gain: sum over its decisions of
    (pi_new - pi_old)/mu * A, with importance ratios capped to keep the variance sane. Advantages
    use the candidate's critic, which was fit on the training split only."""
    adv = advantages(new, ds)
    out_old, _ = old.forward(ds.x)
    out_new, _ = new.forward(ds.x)
    battles = np.unique(ds.battle)
    index = {b: i for i, b in enumerate(battles)}
    gain = np.zeros(len(battles))
    weight = np.zeros(len(battles))
    for name in spec.HEADS:
        rows, pa_new, _, _ = head_terms(out_new, ds, name)
        if len(rows) == 0:
            continue
        _, pa_old, _, _ = head_terms(out_old, ds, name)
        mu = ds.heads[name]["mu"]
        delta = (np.minimum(pa_new / mu, 3.0) - np.minimum(pa_old / mu, 3.0)) * adv[rows]
        b = np.array([index[x] for x in ds.battle[rows]])
        np.add.at(gain, b, delta * ds.weights[rows])
    for b, i in index.items():
        weight[i] = ds.weights[ds.battle == b].mean()
    return gain, weight


def mean_kl(old, new, ds: Dataset):
    out_old, _ = old.forward(ds.x)
    out_new, _ = new.forward(ds.x)
    result = {}
    for name in spec.HEADS:
        rows, _, _, p = head_terms(out_old, ds, name)
        if len(rows) == 0:
            result[name] = 0.0
            continue
        _, _, _, q = head_terms(out_new, ds, name)
        with np.errstate(divide="ignore", invalid="ignore"):
            terms = np.where(p > 0, p * (np.log(np.maximum(p, 1e-12)) - np.log(np.maximum(q, 1e-12))), 0.0)
        result[name] = float(np.average(terms.sum(axis=1), weights=ds.weights[rows]))
    return result


def evaluate(old, new, holdout: Dataset, installs: int, cfg: GateConfig = GateConfig(), arena=None):
    """arena: optional {"current": win rate vs Admiral, "candidate": win rate vs Admiral}."""
    report = GateReport(passed=True)
    m = report.metrics
    m["battles"] = int(holdout.n_battles)
    m["installs"] = int(installs)

    if not new.is_finite():
        report.reasons.append("candidate has non-finite weights")
    if holdout.n_battles < cfg.min_battles * 0.2 or installs < cfg.min_installs:
        report.reasons.append(f"not enough evidence ({holdout.n_battles} held-out battles, {installs} installs)")

    if len(holdout.x) and new.is_finite():
        gain, weight = _per_battle_improvement(old, new, holdout)
        rng = np.random.default_rng(cfg.seed)
        n = len(gain)
        samples = []
        for _ in range(cfg.bootstrap):
            pick = rng.integers(0, n, n)
            w = weight[pick].sum()
            samples.append(gain[pick].sum() / w if w > 0 else 0.0)
        lower = float(np.quantile(samples, 1 - cfg.confidence))
        m["improvement"] = float(gain.sum() / max(weight.sum(), 1e-9))
        m["improvement_lower"] = lower
        if lower <= cfg.min_improvement:
            report.reasons.append(f"improvement not significant (lower bound {lower:.4f})")

        kl = mean_kl(old, new, holdout)
        m["kl"] = kl
        too_far = {k: v for k, v in kl.items() if v > cfg.max_kl}
        if too_far:
            report.reasons.append(f"moved too far from the released model: {too_far}")

    if new.is_finite():
        canary = distill.canary_scores(new, new.knob("temperature"))
        m["canary"] = canary
        bad = {k: v for k, v in canary.items() if v < cfg.min_canary}
        if bad:
            report.reasons.append(f"failed canary states: {bad}")

    if arena is not None:
        m["arena"] = arena
        if arena["candidate"] < arena["current"] - cfg.arena_tolerance:
            report.reasons.append(f"lost ground in self-play ({arena['candidate']:.2f} vs {arena['current']:.2f})")
    elif cfg.require_arena:
        report.reasons.append("self-play results required but missing")

    report.passed = not report.reasons
    return report
