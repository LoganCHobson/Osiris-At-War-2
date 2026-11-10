"""Authentication pieces for the ingest API. None of it needs server-side session state.

Registration (anonymous, no accounts):
    GET  /v1/challenge -> "ts.rand.mac" + difficulty bits. The MAC proves we issued it.
    POST /v1/register  {challenge, nonce} where sha256(challenge ":" nonce) starts with `bits`
         zero bits (about a second of CPU for a real client; costly at scale for a bot farm).
    The install id is derived from the challenge, so replaying a solved challenge only ever
    yields the same install. The upload token is HMAC(server key, install id): nothing to store,
    nothing to leak from the database.

Uploads:
    X-GA-Install: install id, X-GA-Time: unix seconds,
    X-GA-Signature: hex HMAC-SHA256(token, "POST\\n/v1/battles\\n<time>\\n<sha256 hex of body>")
    Requests more than five minutes off are refused; battle ids are unique, so replaying a
    captured request inside that window changes nothing.

Be clear about what this does and doesn't prove: a determined player can pull their own token out
of their own game and sign anything. What it gives us is a stable, costly-to-multiply identity per
install, so rate limits and per-install training caps actually bite, plus integrity in transit.
Whether the *content* is honest is the trainer's job (osiris_ml.dataset.deep_check).
"""

import base64
import hashlib
import hmac
import ipaddress
import secrets
import time
import uuid

CHALLENGE_TTL = 600
CLOCK_SKEW = 300


def _mac(key, *parts):
    return hmac.new(key, "|".join(parts).encode(), hashlib.sha256).digest()


def issue_challenge(key, now=None):
    ts = str(int(now if now is not None else time.time()))
    rand = secrets.token_urlsafe(12)
    mac = base64.urlsafe_b64encode(_mac(key, "chal", ts, rand)[:18]).decode().rstrip("=")
    return f"{ts}.{rand}.{mac}"


def leading_zero_bits(digest):
    bits = 0
    for byte in digest:
        if byte == 0:
            bits += 8
            continue
        return bits + 8 - byte.bit_length()
    return bits


def check_challenge(key, challenge, nonce, bits, now=None):
    """Returns the install id this solved challenge maps to, or None."""
    if not isinstance(challenge, str) or not isinstance(nonce, str) or len(challenge) > 128 or len(nonce) > 64:
        return None
    parts = challenge.split(".")
    if len(parts) != 3 or not parts[0].isdigit():
        return None
    ts, rand, mac = parts
    expected = base64.urlsafe_b64encode(_mac(key, "chal", ts, rand)[:18]).decode().rstrip("=")
    if not hmac.compare_digest(expected, mac):
        return None
    age = (now if now is not None else time.time()) - int(ts)
    if age < -CLOCK_SKEW or age > CHALLENGE_TTL:
        return None
    if leading_zero_bits(hashlib.sha256(f"{challenge}:{nonce}".encode()).digest()) < bits:
        return None
    return str(uuid.UUID(bytes=_mac(key, "id", challenge)[:16], version=4))


def token_for(key, install_id):
    return base64.b64encode(_mac(key, "tok", install_id)).decode()


def request_signature(token, method, path, timestamp, body):
    message = f"{method}\n{path}\n{timestamp}\n{hashlib.sha256(body).hexdigest()}".encode()
    return hmac.new(base64.b64decode(token), message, hashlib.sha256).hexdigest()


def check_request(key, install_id, timestamp, signature, method, path, body, now=None):
    try:
        uuid.UUID(install_id, version=4)
        ts = int(timestamp)
    except (TypeError, ValueError):
        return False
    if abs((now if now is not None else time.time()) - ts) > CLOCK_SKEW:
        return False
    if not isinstance(signature, str) or len(signature) != 64:
        return False
    expected = request_signature(token_for(key, install_id), method, path, str(ts), body)
    return hmac.compare_digest(expected, signature.lower())


def network_of(ip):
    """The /24 (IPv4) or /48 (IPv6) an address is in: what rate limits and caps apply to."""
    try:
        address = ipaddress.ip_address(ip)
    except ValueError:
        return "unknown"
    prefix = 24 if address.version == 4 else 48
    return str(ipaddress.ip_network(f"{address}/{prefix}", strict=False))


def network_hash(key, ip):
    """Stored instead of any IP address. Keyed, so it can't be reversed by brute force."""
    return _mac(key, "net", network_of(ip)).hex()[:32]
