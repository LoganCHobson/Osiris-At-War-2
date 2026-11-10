"""The Grand Admiral network: one tanh hidden layer feeding four policy heads and a value head.

Small on purpose. It runs on every AI decision inside the game on a plain C# forward pass, it
must be cheap to download, and a tiny model trained on noisy player data overfits less.

Binary format (little endian), mirrored by GAModel.cs:
    magic "OGAM" | u16 format | u16 feature_schema | u32 version | i64 created_unix
    u16 n_in | u16 n_hidden | u16 n_out | u16 n_contact | u16 n_knobs
    f32 in_mean[n_in] | f32 in_std[n_in]
    f32 w1[n_hidden * n_in] (row major, hidden x input) | f32 b1[n_hidden]
    f32 w2[n_out * n_hidden] | f32 b2[n_out]
    n_knobs x (u16 id, f32 value)
"""

import struct
import time

import numpy as np

from . import spec

MAGIC = b"OGAM"
_HEADER = struct.Struct("<4sHHIqHHHHH")
MAX_HIDDEN = 256
MAX_KNOBS = 64


class ModelFormatError(ValueError):
    pass


class Model:
    def __init__(self, n_hidden=32, version=0, seed=0, knobs=None):
        rng = np.random.default_rng(seed)
        self.version = int(version)
        self.created = int(time.time())
        self.in_mean = np.zeros(spec.N_FEATURES)
        self.in_std = np.ones(spec.N_FEATURES)
        self.w1 = rng.normal(0.0, 1.0 / np.sqrt(spec.N_FEATURES), (n_hidden, spec.N_FEATURES))
        self.b1 = np.zeros(n_hidden)
        self.w2 = rng.normal(0.0, 0.1 / np.sqrt(n_hidden), (spec.N_OUT, n_hidden))
        self.b2 = np.zeros(spec.N_OUT)
        self.knobs = {kid: default for kid, (_, default, _, _) in spec.KNOBS.items()}
        if knobs:
            for name, value in knobs.items():
                kid = spec.KNOB_IDS[name]
                self.knobs[kid] = spec.clamp_knob(kid, float(value))

    @property
    def n_hidden(self):
        return self.w1.shape[0]

    def knob(self, name):
        kid = spec.KNOB_IDS[name]
        return spec.clamp_knob(kid, self.knobs.get(kid, spec.KNOBS[kid][1]))

    # ---- parameters as a flat vector, for the optimiser ----

    PARAM_NAMES = ("w1", "b1", "w2", "b2")

    def params(self):
        return [getattr(self, name) for name in self.PARAM_NAMES]

    def copy(self):
        clone = Model.__new__(Model)
        clone.version = self.version
        clone.created = self.created
        clone.in_mean = self.in_mean.copy()
        clone.in_std = self.in_std.copy()
        for name in self.PARAM_NAMES:
            setattr(clone, name, getattr(self, name).copy())
        clone.knobs = dict(self.knobs)
        return clone

    # ---- forward / backward ----

    def forward(self, x):
        """x: (n, N_FEATURES) -> (outputs (n, N_OUT), cache for backward)."""
        x = np.atleast_2d(np.asarray(x, dtype=np.float64))
        z = (x - self.in_mean) / self.in_std
        h = np.tanh(z @ self.w1.T + self.b1)
        out = h @ self.w2.T + self.b2
        return out, (z, h)

    def backward(self, cache, d_out):
        """Gradients of a loss wrt every parameter, given dLoss/dOutputs."""
        z, h = cache
        g_w2 = d_out.T @ h
        g_b2 = d_out.sum(axis=0)
        d_h = (d_out @ self.w2) * (1.0 - h * h)
        g_w1 = d_h.T @ z
        g_b1 = d_h.sum(axis=0)
        return [g_w1, g_b1, g_w2, g_b2]

    def is_finite(self):
        return all(np.all(np.isfinite(p)) for p in self.params()) and np.all(np.isfinite(self.in_std))

    # ---- serialisation ----

    def to_bytes(self):
        n_hidden = self.n_hidden
        parts = [_HEADER.pack(MAGIC, spec.MODEL_FORMAT, spec.FEATURE_SCHEMA, self.version, self.created,
                              spec.N_FEATURES, n_hidden, spec.N_OUT, spec.N_CONTACT, len(self.knobs))]
        for array in (self.in_mean, self.in_std, self.w1, self.b1, self.w2, self.b2):
            parts.append(np.asarray(array, dtype="<f4").tobytes(order="C"))
        for kid in sorted(self.knobs):
            parts.append(struct.pack("<Hf", kid, self.knobs[kid]))
        return b"".join(parts)

    @classmethod
    def from_bytes(cls, data):
        if len(data) < _HEADER.size:
            raise ModelFormatError("truncated header")
        magic, fmt, schema, version, created, n_in, n_hidden, n_out, n_contact, n_knobs = _HEADER.unpack_from(data)
        if magic != MAGIC:
            raise ModelFormatError("bad magic")
        if fmt != spec.MODEL_FORMAT or schema != spec.FEATURE_SCHEMA:
            raise ModelFormatError(f"unsupported format {fmt}/schema {schema}")
        if n_in != spec.N_FEATURES or n_out != spec.N_OUT or n_contact != spec.N_CONTACT:
            raise ModelFormatError("shape does not match the feature schema")
        if not 1 <= n_hidden <= MAX_HIDDEN or n_knobs > MAX_KNOBS:
            raise ModelFormatError("implausible dimensions")

        floats = 2 * n_in + n_hidden * n_in + n_hidden + n_out * n_hidden + n_out
        expected = _HEADER.size + floats * 4 + n_knobs * 6
        if len(data) != expected:
            raise ModelFormatError(f"length {len(data)} != {expected}")

        values = np.frombuffer(data, dtype="<f4", count=floats, offset=_HEADER.size).astype(np.float64)
        if not np.all(np.isfinite(values)):
            raise ModelFormatError("non-finite weights")

        model = cls.__new__(cls)
        model.version = version
        model.created = created
        cursor = 0

        def take(count, shape=None):
            nonlocal cursor
            chunk = values[cursor:cursor + count]
            cursor += count
            return chunk.reshape(shape) if shape else chunk.copy()

        model.in_mean = take(n_in)
        model.in_std = take(n_in)
        if np.any(model.in_std <= 0):
            raise ModelFormatError("non-positive input scale")
        model.w1 = take(n_hidden * n_in, (n_hidden, n_in)).copy()
        model.b1 = take(n_hidden)
        model.w2 = take(n_out * n_hidden, (n_out, n_hidden)).copy()
        model.b2 = take(n_out)

        model.knobs = {kid: default for kid, (_, default, _, _) in spec.KNOBS.items()}
        offset = _HEADER.size + floats * 4
        for i in range(n_knobs):
            kid, value = struct.unpack_from("<Hf", data, offset + i * 6)
            if kid in spec.KNOBS:  # Unknown knobs are ignored: a newer server may know more.
                model.knobs[kid] = spec.clamp_knob(kid, value)
        return model

    def rounded(self):
        """This model as the client will see it (float32 weights)."""
        return Model.from_bytes(self.to_bytes())
