"""Offline policy improvement from logged battles.

Every logged decision says: in this state, the AI picked action `a` with probability `mu`, and the
battle ended with return R. With a critic V(s) the advantage is A = R - V(s), and the PPO clipped
surrogate
        min(r * A, clip(r, 1 - c, 1 + c) * A),   r = pi_new(a|s) / mu
pushes probability toward actions that did better than expected, but never more than a factor
(1 +- c) away from what was logged. That clip, plus an L2 anchor to the released weights, keeps
each training round a small, conservative step. That's what stops a bad batch of data, honest or
not, from rewriting the AI overnight.

Gradients are written out by hand (the network is tiny), so the trainer needs only numpy.
"""

import logging
import time
from dataclasses import dataclass

import numpy as np

from . import spec
from .dataset import Dataset

log = logging.getLogger("grand-admiral.trainer")


@dataclass
class TrainConfig:
    epochs: int = 60
    batch_size: int = 1024
    learning_rate: float = 5e-3
    clip: float = 0.2
    anchor: float = 5e-4        # L2 pull back toward the previous model's weights
    value_coef: float = 0.0     # the critic is fit by ridge regression instead
    gae_lambda: float = 0.9
    seed: int = 0


def categorical_terms(logits, mask, actions, temperature, epsilon):
    """For each row: mixed probability of the logged action, its gradient wrt the logits, and the
    full mixed distribution."""
    k = len(actions)
    t = temperature[:, None]
    z = np.where(mask, logits / t, -np.inf)
    z = z - z.max(axis=1, keepdims=True)
    e = np.where(mask, np.exp(z), 0.0)
    p = e / e.sum(axis=1, keepdims=True)
    allowed = mask.sum(axis=1)
    mixed = np.where(mask, (1 - epsilon)[:, None] * p + (epsilon / allowed)[:, None], 0.0)
    pa_raw = p[np.arange(k), actions]
    pa = mixed[np.arange(k), actions]
    onehot = np.zeros_like(p)
    onehot[np.arange(k), actions] = 1.0
    grad = (1 - epsilon)[:, None] * pa_raw[:, None] * (onehot - p) / t
    return pa, grad, mixed


def binary_terms(logit, actions, temperature, epsilon):
    p1 = 1.0 / (1.0 + np.exp(-logit / temperature))
    m1 = (1 - epsilon) * p1 + epsilon * 0.5
    pa = np.where(actions == 1, m1, 1.0 - m1)
    grad = (1 - epsilon) * p1 * (1 - p1) / temperature * np.where(actions == 1, 1.0, -1.0)
    return pa, grad, np.stack([1.0 - m1, m1], axis=1)


def head_terms(out, ds: Dataset, name):
    """(rows, pa, d pa / d out[rows], full mixed distribution) for one head."""
    h = ds.heads[name]
    rows = h["rows"]
    if len(rows) == 0:
        return rows, np.zeros(0), np.zeros((0, spec.N_OUT)), np.zeros((0, 2))
    pa, grad_out, mixed = _terms(out[rows], h, slice(None), ds.temperature[rows], ds.epsilon[rows], name)
    return rows, pa, grad_out, mixed


def _terms(out_rows, h, entries, t, eps, name):
    """head_terms for the head entries `entries`, given the network outputs on their rows."""
    grad_out = np.zeros((len(out_rows), spec.N_OUT))
    if name in spec.CATEGORICAL:
        outputs = spec.CATEGORICAL[name][0]
        pa, g, mixed = categorical_terms(out_rows[:, outputs], h["mask"][entries], h["a"][entries], t, eps)
        grad_out[:, outputs] = g
    elif name == "retreat":
        pa, g, mixed = binary_terms(out_rows[:, spec.OUT_RETREAT], h["a"][entries], t, eps)
        grad_out[:, spec.OUT_RETREAT] = g
    else:
        contacts = h["contacts"][entries]
        w = out_rows[:, spec.OUT_TARGET]
        scores = np.einsum("knc,kc->kn", contacts, w)
        pa, g_scores, mixed = categorical_terms(scores, h["mask"][entries], h["a"][entries], t, eps)
        grad_out[:, spec.OUT_TARGET] = np.einsum("kn,knc->kc", g_scores, contacts)
    return pa, grad_out, mixed


def _entry_maps(ds: Dataset):
    """Per head, the head entry each dataset row has (-1 for none). Built once per dataset."""
    maps = getattr(ds, "_entry_maps", None)
    if maps is None:
        maps = {}
        for name, h in ds.heads.items():
            m = np.full(len(ds.x), -1, dtype=np.int64)
            m[h["rows"]] = np.arange(len(h["rows"]))
            maps[name] = m
        ds._entry_maps = maps
    return maps


def fit_critic(model, ds: Dataset, ridge=1e-2):
    """A copy of `model` whose value head is refit to this data by ridge regression on the hidden
    layer. Only the value row changes, so the policy is untouched."""
    fitted = model.copy()
    if len(ds.x) == 0:
        return fitted
    _, (_, h) = model.forward(ds.x)
    design = np.hstack([h, np.ones((len(h), 1))])
    w = ds.weights[:, None]
    gram = design.T @ (design * w) + ridge * np.eye(design.shape[1])
    coef = np.linalg.solve(gram, design.T @ (ds.returns * ds.weights))
    fitted.w2[spec.OUT_VALUE] = coef[:-1]
    fitted.b2[spec.OUT_VALUE] = coef[-1]
    return fitted


def advantages(critic, ds: Dataset, lam=0.9):
    """GAE(lambda) advantages, normalised. Rewards only arrive at the end of a battle, so
    delta_t = V(s_t+1) - V(s_t), and the last decision's delta is R - V(s_T). lam=1 would be the
    plain (much noisier) Monte Carlo advantage R - V(s_t)."""
    if len(ds.x) == 0:
        return np.zeros(0)
    out, _ = critic.forward(ds.x)
    v = out[:, spec.OUT_VALUE]
    a = np.zeros_like(v)
    running = 0.0
    for i in range(len(v) - 1, -1, -1):
        last = i == len(v) - 1 or ds.battle[i + 1] != ds.battle[i]
        delta = (ds.returns[i] - v[i]) if last else (v[i + 1] - v[i])
        running = delta + (0.0 if last else lam * running)
        a[i] = running
    w = ds.weights
    mean = np.average(a, weights=w)
    std = np.sqrt(np.average((a - mean) ** 2, weights=w)) + 1e-6
    return (a - mean) / std


class Adam:
    def __init__(self, params, lr, beta1=0.9, beta2=0.999, eps=1e-8):
        self.lr, self.b1, self.b2, self.eps = lr, beta1, beta2, eps
        self.m = [np.zeros_like(p) for p in params]
        self.v = [np.zeros_like(p) for p in params]
        self.t = 0

    def step(self, params, grads):
        self.t += 1
        for p, g, m, v in zip(params, grads, self.m, self.v):
            m *= self.b1
            m += (1 - self.b1) * g
            v *= self.b2
            v += (1 - self.b2) * g * g
            m_hat = m / (1 - self.b1 ** self.t)
            v_hat = v / (1 - self.b2 ** self.t)
            p -= self.lr * m_hat / (np.sqrt(v_hat) + self.eps)


def loss_and_grads(model, anchor_params, ds: Dataset, adv, rows_subset, cfg: TrainConfig):
    # Only the batch's rows go through the network. (It used to run the whole dataset forward and back
    # for every batch and mask the rest out: quadratic, so a round over the live pool ran for hours.)
    batch = np.unique(np.asarray(rows_subset, dtype=np.int64))
    out, cache = model.forward(ds.x[batch])
    d_out = np.zeros_like(out)
    maps = _entry_maps(ds)
    total = 0.0

    for name in spec.HEADS:
        h = ds.heads[name]
        if len(h["rows"]) == 0:
            continue
        found = maps[name][batch]
        local = np.flatnonzero(found >= 0)  # positions in the batch; ascending row order, as before
        if len(local) == 0:
            continue
        entries = found[local]
        rows = batch[local]
        pa, grad_out, _ = _terms(out[local], h, entries, ds.temperature[rows], ds.epsilon[rows], name)
        mu = h["mu"][entries]
        a = adv[rows]
        w = ds.weights[rows]
        ratio = pa / mu
        clipped = np.clip(ratio, 1 - cfg.clip, 1 + cfg.clip)
        objective = np.minimum(ratio * a, clipped * a)
        active = ratio * a <= clipped * a + 1e-12
        weight_sum = w.sum()
        if weight_sum <= 0:
            continue
        total -= (w * objective).sum() / weight_sum
        coef = -(w * active * a / mu) / weight_sum
        np.add.at(d_out, local, coef[:, None] * grad_out)

    w = ds.weights[batch]
    if w.sum() > 0:
        err = out[:, spec.OUT_VALUE] - ds.returns[batch]
        total += cfg.value_coef * 0.5 * (w * err * err).sum() / w.sum()
        d_out[:, spec.OUT_VALUE] += cfg.value_coef * w * err / w.sum()

    grads = model.backward(cache, d_out)
    for g, p, p0 in zip(grads, model.params(), anchor_params):
        g += cfg.anchor * (p - p0)
        total += 0.5 * cfg.anchor * float(((p - p0) ** 2).sum())
    return total, grads


def train(previous, ds: Dataset, cfg: TrainConfig = TrainConfig()):
    """A candidate model one conservative step on from `previous`, with its critic fit to `ds`."""
    model = previous.copy()
    anchor_params = [p.copy() for p in previous.params()]
    if len(ds.x) == 0:
        return model
    adv = advantages(fit_critic(previous, ds), ds, cfg.gae_lambda)
    optimiser = Adam(model.params(), cfg.learning_rate)
    rng = np.random.default_rng(cfg.seed)
    n = len(ds.x)
    started = time.monotonic()
    for epoch in range(cfg.epochs):
        if epoch % 10 == 0:
            log.info("training: epoch %d/%d over %d decisions (%.0fs)", epoch, cfg.epochs, n, time.monotonic() - started)
        order = rng.permutation(n)
        for start in range(0, n, cfg.batch_size):
            batch = order[start:start + cfg.batch_size]
            _, grads = loss_and_grads(model, anchor_params, ds, adv, batch, cfg)
            grads[2][spec.OUT_VALUE] = 0.0  # The critic is refit exactly below.
            grads[3][spec.OUT_VALUE] = 0.0
            optimiser.step(model.params(), grads)
    return fit_critic(model, ds)
