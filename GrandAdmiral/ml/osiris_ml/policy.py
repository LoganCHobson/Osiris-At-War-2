"""Action distributions, exactly as the client samples them (GAPolicy.cs).

Every head is: logits / temperature -> softmax over the *allowed* actions -> mixed with epsilon
uniform exploration. The client logs the probability of the action it actually took; the trainer
recomputes it under the same rules, which is what makes off-policy learning (and cheating
detection) possible.
"""

import numpy as np

from . import spec


def masked_softmax(logits, mask, temperature, epsilon):
    """logits, mask: (..., k). Returns mixed probabilities and the raw softmax."""
    logits = np.asarray(logits, dtype=np.float64)
    mask = np.asarray(mask, dtype=bool)
    z = np.where(mask, logits / temperature, -np.inf)
    z = z - np.max(z, axis=-1, keepdims=True)
    e = np.where(mask, np.exp(z), 0.0)
    p = e / e.sum(axis=-1, keepdims=True)
    allowed = mask.sum(axis=-1, keepdims=True)
    mixed = np.where(mask, (1.0 - epsilon) * p + epsilon / allowed, 0.0)
    return mixed, p


def retreat_probability(logit, temperature, epsilon):
    """Probability of retreating (action 1)."""
    p = 1.0 / (1.0 + np.exp(-np.asarray(logit, dtype=np.float64) / temperature))
    return (1.0 - epsilon) * p + epsilon * 0.5, p


def target_scores(out, contacts):
    """contacts: (..., n, N_CONTACT) -> scores (..., n)."""
    w = np.asarray(out)[..., spec.OUT_TARGET]
    return np.einsum("...nk,...k->...n", np.asarray(contacts, dtype=np.float64), w)


def decide(out, *, masks=None, retreat_open=False, contacts=None, temperature=1.0, epsilon=0.05,
           stance_mask=None, commit_mask=None):
    """Every head's distribution for one decision (used by parity tests and the toy env).
    masks: {categorical head name: allowed actions}; heads left out weren't decided."""
    out = np.asarray(out, dtype=np.float64)
    masks = dict(masks or {})
    if stance_mask is not None:
        masks["stance"] = stance_mask
    if commit_mask is not None:
        masks["commit"] = commit_mask
    result = {}
    for name, (outputs, _) in spec.CATEGORICAL.items():
        if masks.get(name) is not None:
            result[name] = masked_softmax(out[outputs], masks[name], temperature, epsilon)[0]
    if retreat_open:
        p1 = retreat_probability(out[spec.OUT_RETREAT], temperature, epsilon)[0]
        result["retreat"] = np.array([1.0 - p1, p1])
    if contacts is not None and len(contacts) > 0:
        scores = target_scores(out, contacts)
        result["target"] = masked_softmax(scores, np.ones(len(contacts), bool), temperature, epsilon)[0]
    result["value"] = float(out[spec.OUT_VALUE])
    return result
