import struct

import numpy as np
import pytest

from osiris_ml import bundle, spec
from osiris_ml.model import Model, ModelFormatError


def test_round_trip_preserves_outputs(baseline):
    again = Model.from_bytes(baseline.to_bytes())
    x = np.random.default_rng(0).normal(size=(50, spec.N_FEATURES))
    np.testing.assert_allclose(baseline.forward(x)[0], again.forward(x)[0], atol=1e-5)
    assert again.version == baseline.version
    assert again.knobs == baseline.knobs


def test_size_is_small(baseline):
    assert len(baseline.to_bytes()) < 32 * 1024  # tens of KB: trivial to download on any connection


@pytest.mark.parametrize("mutate,message", [
    (lambda b: b"XXXX" + b[4:], "magic"),
    (lambda b: b[:4] + struct.pack("<H", 9) + b[6:], "format"),
    (lambda b: b[:6] + struct.pack("<H", 99) + b[8:], "schema"),
    (lambda b: b[:-1], "length"),
    (lambda b: b + b"\x00", "length"),
    (lambda b: b[:10], "header"),
])
def test_rejects_malformed(baseline, mutate, message):
    with pytest.raises(ModelFormatError):
        Model.from_bytes(mutate(baseline.to_bytes()))


def test_rejects_nan_weights(baseline):
    data = bytearray(baseline.to_bytes())
    from osiris_ml.model import _HEADER
    struct.pack_into("<f", data, _HEADER.size + spec.N_FEATURES * 8 + 4 * 5, float("nan"))  # inside w1
    with pytest.raises(ModelFormatError):
        Model.from_bytes(bytes(data))


def test_rejects_zero_input_scale(baseline):
    baseline.in_std[3] = 0.0
    with pytest.raises(ModelFormatError):
        Model.from_bytes(baseline.to_bytes())


def test_knobs_are_clamped_and_unknown_ignored(baseline):
    baseline.knobs[spec.KNOB_IDS["temperature"]] = 50.0
    baseline.knobs[999] = 1.0
    loaded = Model.from_bytes(baseline.to_bytes())
    assert loaded.knob("temperature") == 2.0
    assert 999 not in loaded.knobs


def test_bundle_round_trip(baseline, signing_key):
    signed = bundle.sign(baseline.to_bytes(), signing_key, 1)
    assert bundle.verify(signed, {1: signing_key.public_key()}) == baseline.to_bytes()


def test_bundle_rejects_any_flipped_bit(baseline, signing_key):
    signed = bytearray(bundle.sign(baseline.to_bytes(), signing_key, 1))
    keys = {1: signing_key.public_key()}
    for position in (12, 100, len(signed) // 2, len(signed) - 1):
        tampered = bytearray(signed)
        tampered[position] ^= 0x01
        with pytest.raises(bundle.BundleError):
            bundle.verify(bytes(tampered), keys)


def test_bundle_rejects_other_key(baseline, signing_key):
    other = bundle.generate_key()
    signed = bundle.sign(baseline.to_bytes(), other, 1)
    with pytest.raises(bundle.BundleError):
        bundle.verify(signed, {1: signing_key.public_key()})


def test_bundle_rejects_unknown_key_id(baseline, signing_key):
    signed = bundle.sign(baseline.to_bytes(), signing_key, 7)
    with pytest.raises(bundle.BundleError, match="unknown key"):
        bundle.verify(signed, {1: signing_key.public_key()})


def test_bundle_rejects_trailing_bytes(baseline, signing_key):
    signed = bundle.sign(baseline.to_bytes(), signing_key, 1)
    with pytest.raises(bundle.BundleError):
        bundle.verify(signed + b"\x00", {1: signing_key.public_key()})


def test_signature_is_domain_separated(baseline, signing_key):
    """A signature made over the bare model bytes (no context prefix) must not verify."""
    from cryptography.hazmat.primitives import hashes
    from cryptography.hazmat.primitives.asymmetric import padding

    data = baseline.to_bytes()
    raw = signing_key.sign(data, padding.PKCS1v15(), hashes.SHA256())
    forged = bundle._HEAD.pack(bundle.MAGIC, spec.BUNDLE_FORMAT, 1, len(data)) + data + struct.pack("<H", len(raw)) + raw
    with pytest.raises(bundle.BundleError):
        bundle.verify(forged, {1: signing_key.public_key()})


def test_client_key_export(signing_key):
    import base64
    modulus, exponent = bundle.public_key_for_client(signing_key)
    assert len(base64.b64decode(modulus)) == 384  # RSA-3072
    assert base64.b64decode(exponent) == b"\x01\x00\x01"
