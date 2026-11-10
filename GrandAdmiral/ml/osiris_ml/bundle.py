"""Signed model bundles.

A bundle is the only thing the game ever downloads:
    magic "OGAB" | u16 bundle_format | u16 key_id | u32 model_len | model bytes | u16 sig_len | signature

The signature is RSA-3072 PKCS#1 v1.5 over SHA-256(SIGNATURE_CONTEXT + model bytes). RSA because
Unity's Mono and IL2CPP runtimes verify it natively on every platform with no extra packages.
The game ships with the public keys (GATrustedKeys.cs) and refuses anything they didn't sign, so
neither the API server, the CDN nor a man in the middle can push a model to players. Only the
trainer, which holds the private key and accepts no inbound connections, can.
"""

import base64
import struct

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

from . import spec

MAGIC = b"OGAB"
_HEAD = struct.Struct("<4sHHI")
MAX_MODEL_BYTES = 1 << 20


class BundleError(ValueError):
    pass


def generate_key(bits=3072):
    return rsa.generate_private_key(public_exponent=65537, key_size=bits)


def private_key_pem(key, passphrase=None):
    encryption = (serialization.BestAvailableEncryption(passphrase.encode()) if passphrase
                  else serialization.NoEncryption())
    return key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, encryption)


def load_private_key(pem, passphrase=None):
    return serialization.load_pem_private_key(pem, password=passphrase.encode() if passphrase else None)


def public_key_for_client(key):
    """Modulus and exponent, base64 big-endian, as GATrustedKeys.cs expects them."""
    numbers = key.public_key().public_numbers() if hasattr(key, "public_key") else key.public_numbers()
    n = numbers.n.to_bytes((numbers.n.bit_length() + 7) // 8, "big")
    e = numbers.e.to_bytes((numbers.e.bit_length() + 7) // 8, "big")
    return base64.b64encode(n).decode(), base64.b64encode(e).decode()


def sign(model_bytes, private_key, key_id):
    if len(model_bytes) > MAX_MODEL_BYTES:
        raise BundleError("model too large")
    signature = private_key.sign(spec.SIGNATURE_CONTEXT + model_bytes, padding.PKCS1v15(), hashes.SHA256())
    return (_HEAD.pack(MAGIC, spec.BUNDLE_FORMAT, key_id, len(model_bytes)) + model_bytes
            + struct.pack("<H", len(signature)) + signature)


def parse(bundle):
    """-> (key_id, model_bytes, signature). Structure only; no trust implied."""
    if len(bundle) < _HEAD.size:
        raise BundleError("truncated")
    magic, fmt, key_id, model_len = _HEAD.unpack_from(bundle)
    if magic != MAGIC or fmt != spec.BUNDLE_FORMAT:
        raise BundleError("not a bundle")
    if model_len > MAX_MODEL_BYTES or _HEAD.size + model_len + 2 > len(bundle):
        raise BundleError("bad model length")
    model_bytes = bundle[_HEAD.size:_HEAD.size + model_len]
    (sig_len,) = struct.unpack_from("<H", bundle, _HEAD.size + model_len)
    start = _HEAD.size + model_len + 2
    if start + sig_len != len(bundle):
        raise BundleError("bad signature length")
    return key_id, model_bytes, bundle[start:]


def verify(bundle, public_keys):
    """public_keys: {key_id: public key}. Returns model bytes or raises BundleError."""
    key_id, model_bytes, signature = parse(bundle)
    key = public_keys.get(key_id)
    if key is None:
        raise BundleError(f"unknown key {key_id}")
    try:
        key.verify(signature, spec.SIGNATURE_CONTEXT + model_bytes, padding.PKCS1v15(), hashes.SHA256())
    except InvalidSignature as exc:
        raise BundleError("bad signature") from exc
    return model_bytes
