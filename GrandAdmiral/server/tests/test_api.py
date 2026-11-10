import base64
import hashlib
import itertools
import json
import os
import time

import numpy as np
import pytest
from fastapi.testclient import TestClient
from sqlalchemy import select, update

from app import security
from app.main import RateWindow, Settings, create_app
from osiris_ml import bundle, distill, pipeline, storage, toyenv

KEY = os.urandom(32)


@pytest.fixture(scope="module")
def model():
    m = distill.baseline(samples=6000, epochs=10).rounded()
    m.version = 1
    return m


@pytest.fixture(scope="module")
def signing_key():
    return bundle.generate_key()


def make_client(tmp_path, model, signing_key, **overrides):
    settings = Settings(server_key=KEY, db_url=f"sqlite:///{tmp_path / 'api.db'}", pow_bits=8,
                        create_schema=True, **overrides)
    app = create_app(settings)
    with app.state.engine.begin() as conn:
        pipeline.publish(conn, model, "released", {}, signing_key, 1)
    return TestClient(app), app


@pytest.fixture
def api(tmp_path, model, signing_key):
    return make_client(tmp_path, model, signing_key)


def solve(challenge, bits):
    for n in itertools.count():
        nonce = str(n)
        if security.leading_zero_bits(hashlib.sha256(f"{challenge}:{nonce}".encode()).digest()) >= bits:
            return nonce


def register(client, ip="203.0.113.7"):
    c = client.get("/v1/challenge").json()
    r = client.post("/v1/register", json={"challenge": c["challenge"], "nonce": solve(c["challenge"], c["bits"])},
                    headers={"X-Forwarded-For": ip})
    assert r.status_code == 200, r.text
    return r.json()


def signed_post(client, path, creds, body, ts=None, ip="203.0.113.7", tamper=None):
    data = body if isinstance(body, bytes) else json.dumps(body).encode()
    ts = str(int(ts if ts is not None else time.time()))
    sig = security.request_signature(creds["token"], "POST", path, ts, data)
    if tamper == "body":
        data = data.replace(b'"won": false', b'"won": true') if b'"won": false' in data else data + b" "
    return client.post(path, content=data, headers={
        "Content-Type": "application/json", "X-GA-Install": creds["install_id"], "X-GA-Time": ts,
        "X-GA-Signature": sig, "X-Forwarded-For": ip})


def battle(model, seed=0, **overrides):
    record = toyenv.play(model, np.random.default_rng(seed))
    record.update(overrides)
    return record


# ---- registration ----

def test_register_gives_working_credentials(api, model):
    client, _ = api
    creds = register(client)
    assert signed_post(client, "/v1/battles", creds, battle(model)).status_code == 202


def test_replayed_challenge_gives_the_same_install(api):
    client, _ = api
    c = client.get("/v1/challenge").json()
    body = {"challenge": c["challenge"], "nonce": solve(c["challenge"], c["bits"])}
    first = client.post("/v1/register", json=body).json()
    second = client.post("/v1/register", json=body).json()
    assert first == second


def test_forged_challenge_rejected(api):
    client, _ = api
    c = client.get("/v1/challenge").json()["challenge"]
    ts, rand, mac = c.split(".")
    forged = f"{ts}.{rand}x.{mac}"
    assert client.post("/v1/register", json={"challenge": forged, "nonce": solve(forged, 8)}).status_code == 400


def test_expired_challenge_rejected(api):
    client, _ = api
    old = security.issue_challenge(KEY, now=time.time() - 3600)
    assert client.post("/v1/register", json={"challenge": old, "nonce": solve(old, 8)}).status_code == 400


def test_unsolved_challenge_rejected(api):
    client, _ = api
    c = client.get("/v1/challenge").json()["challenge"]
    for n in itertools.count():
        if security.leading_zero_bits(hashlib.sha256(f"{c}:{n}".encode()).digest()) == 0:
            break
    assert client.post("/v1/register", json={"challenge": c, "nonce": str(n)}).status_code == 400


def test_register_rejects_junk(api):
    client, _ = api
    for body in ({"challenge": 1, "nonce": "1"}, {"challenge": "a.b.c", "nonce": "1", "extra": 1}, {}):
        assert client.post("/v1/register", json=body).status_code == 400


def test_registration_limit_per_network(tmp_path, model, signing_key):
    client, _ = make_client(tmp_path, model, signing_key, registrations_per_day_per_network=3)
    for _ in range(3):
        register(client, ip="198.51.100.1")
    c = client.get("/v1/challenge").json()
    r = client.post("/v1/register", json={"challenge": c["challenge"], "nonce": solve(c["challenge"], 8)},
                    headers={"X-Forwarded-For": "198.51.100.99"})  # same /24
    assert r.status_code == 429
    register(client, ip="192.0.2.1")  # a different network is fine


# ---- uploads ----

def test_duplicate_upload_is_idempotent(api, model):
    client, _ = api
    creds = register(client)
    record = battle(model, 1)
    assert signed_post(client, "/v1/battles", creds, record).status_code == 202
    r = signed_post(client, "/v1/battles", creds, record)
    assert r.status_code == 200 and r.json()["status"] == "duplicate"


def test_bad_signature(api, model):
    client, _ = api
    creds = register(client)
    assert signed_post(client, "/v1/battles", creds, battle(model, 2), tamper="body").status_code == 401
    wrong = dict(creds, token=base64.b64encode(os.urandom(32)).decode())
    assert signed_post(client, "/v1/battles", wrong, battle(model, 2)).status_code == 401


def test_stale_and_future_timestamps(api, model):
    client, _ = api
    creds = register(client)
    assert signed_post(client, "/v1/battles", creds, battle(model, 3), ts=time.time() - 900).status_code == 401
    assert signed_post(client, "/v1/battles", creds, battle(model, 3), ts=time.time() + 900).status_code == 401


def test_unknown_install(api, model):
    client, _ = api
    creds = {"install_id": "6f1c1d6e-1111-4222-8333-444455556666",
             "token": security.token_for(KEY, "6f1c1d6e-1111-4222-8333-444455556666")}
    assert signed_post(client, "/v1/battles", creds, battle(model, 4)).status_code == 401


def test_missing_headers(api, model):
    client, _ = api
    r = client.post("/v1/battles", json=battle(model, 4))
    assert r.status_code == 401


def test_blocked_install(api, model):
    client, app = api
    creds = register(client)
    with app.state.engine.begin() as conn:
        conn.execute(update(storage.installs).values(blocked=True))
    assert signed_post(client, "/v1/battles", creds, battle(model, 5)).status_code == 403


def test_oversized_body(api, model):
    client, _ = api
    creds = register(client)
    assert signed_post(client, "/v1/battles", creds, b"{" + b" " * (600 * 1024) + b"}").status_code == 413


def test_body_without_length(api):
    client, _ = api

    def stream():
        yield b"{}"

    r = client.post("/v1/battles", content=stream())
    assert r.status_code == 413


def test_invalid_record(api, model):
    client, _ = api
    creds = register(client)
    assert signed_post(client, "/v1/battles", creds, battle(model, 6, end_power=-5.0)).status_code == 422
    assert signed_post(client, "/v1/battles", creds, b"not json").status_code == 422


def test_unknown_model_version(api, model):
    client, _ = api
    creds = register(client)
    assert signed_post(client, "/v1/battles", creds, battle(model, 7, model_version=42)).status_code == 422


def test_stored_payload_is_normalised(api, model):
    """What's stored is re-serialised from the validated model, never the client's raw bytes."""
    client, app = api
    creds = register(client)
    raw = json.dumps(battle(model, 8), indent=4).encode()
    assert signed_post(client, "/v1/battles", creds, raw).status_code == 202
    with app.state.engine.connect() as conn:
        stored = conn.execute(select(storage.battles.c.payload)).scalar()
    assert "\n" not in stored and "  " not in stored


def test_no_ip_addresses_stored(api, model):
    client, app = api
    creds = register(client, ip="203.0.113.77")
    signed_post(client, "/v1/battles", creds, battle(model, 9), ip="203.0.113.77")
    with app.state.engine.connect() as conn:
        dump = json.dumps([list(map(str, r)) for t in (storage.installs, storage.battles)
                           for r in conn.execute(select(t))])
    assert "203.0.113" not in dump


def test_hourly_upload_limit(tmp_path, model, signing_key):
    client, _ = make_client(tmp_path, model, signing_key, uploads_per_hour=3)
    creds = register(client)
    codes = [signed_post(client, "/v1/battles", creds, battle(model, 100 + i)).status_code for i in range(5)]
    assert codes == [202, 202, 202, 429, 429]


def test_selfplay_install_is_exempt(tmp_path, model, signing_key):
    client, app = make_client(tmp_path, model, signing_key, uploads_per_hour=2)
    creds = register(client)
    app.state.settings.selfplay_installs = (creds["install_id"],)
    codes = [signed_post(client, "/v1/battles", creds, battle(model, 200 + i)).status_code for i in range(4)]
    assert codes == [202] * 4


# ---- arena ----

def test_arena_only_for_selfplay(tmp_path, model, signing_key):
    client, app = make_client(tmp_path, model, signing_key)
    creds = register(client)
    result = {"version": 1, "opponent": "admiral", "games": 10, "wins": 6, "draws": 1}
    assert signed_post(client, "/v1/arena", creds, result).status_code == 403
    app.state.settings.selfplay_installs = (creds["install_id"],)
    assert signed_post(client, "/v1/arena", creds, result).status_code == 202
    assert signed_post(client, "/v1/arena", creds, dict(result, wins=10, draws=5)).status_code == 422


# ---- model distribution ----

def test_model_download_verifies(api, model, signing_key):
    client, _ = api
    r = client.get("/v1/model/latest")
    assert r.status_code == 200
    assert r.headers["x-ga-model-version"] == "1"
    assert bundle.verify(r.content, {1: signing_key.public_key()}) == model.to_bytes()
    assert client.get("/v1/model/latest?have=1").status_code == 204


def test_unreleased_models_never_served(api, model, signing_key):
    client, app = api
    candidate = model.copy()
    candidate.version = 2
    with app.state.engine.begin() as conn:
        pipeline.publish(conn, candidate, "rejected", {})
    assert client.get("/v1/model/latest?have=1").status_code == 204


def test_bad_have_parameter(api):
    client, _ = api
    assert client.get("/v1/model/latest?have=abc").status_code == 422


# ---- hygiene ----

def test_no_docs_or_schema(api):
    client, _ = api
    for path in ("/docs", "/redoc", "/openapi.json"):
        assert client.get(path).status_code == 404


def test_security_headers(api):
    client, _ = api
    r = client.get("/v1/challenge")
    assert r.headers["x-content-type-options"] == "nosniff"
    assert r.headers["cache-control"] == "no-store"


def test_flood_is_shed(tmp_path, model, signing_key):
    client, _ = make_client(tmp_path, model, signing_key, requests_per_minute_per_ip=5)
    codes = [client.get("/v1/challenge", headers={"X-Forwarded-For": "192.0.2.50"}).status_code for _ in range(7)]
    assert codes[:5] == [200] * 5 and codes[5:] == [429, 429]
    assert client.get("/healthz").status_code == 200


def test_spoofed_forwarded_for_is_ignored(tmp_path, model, signing_key):
    """Only the address the ingress appended counts; whatever the client put in front is ignored."""
    client, _ = make_client(tmp_path, model, signing_key, requests_per_minute_per_ip=2)
    codes = [client.get("/v1/challenge", headers={"X-Forwarded-For": f"10.0.0.{i}, 192.0.2.60"}).status_code
             for i in range(4)]
    assert codes == [200, 200, 429, 429]


def test_rate_window_bounded_memory():
    window = RateWindow(limit=1, max_keys=100)
    for i in range(1000):
        window.allow(f"ip{i}", now=0.0)
    assert len(window.hits) == 100


def test_network_hash_groups_subnets():
    assert security.network_hash(KEY, "203.0.113.1") == security.network_hash(KEY, "203.0.113.250")
    assert security.network_hash(KEY, "203.0.113.1") != security.network_hash(KEY, "203.0.114.1")
    assert security.network_hash(KEY, "2001:db8:1:2::1") == security.network_hash(KEY, "2001:db8:1:ffff::9")
    assert security.network_hash(KEY, "203.0.113.1") != security.network_hash(os.urandom(32), "203.0.113.1")


def test_settings_refuse_weak_key(monkeypatch):
    monkeypatch.setenv("GA_SERVER_KEY", base64.b64encode(b"short").decode())
    monkeypatch.setenv("GA_DB_URL", "sqlite://")
    with pytest.raises(RuntimeError):
        Settings.from_env()


def test_selfplay_credentials_are_stable_and_work(tmp_path, model, signing_key):
    from app.admin import selfplay_credentials
    client, app = make_client(tmp_path, model, signing_key)
    first = selfplay_credentials(app.state.settings)
    assert selfplay_credentials(app.state.settings) == first
    app.state.settings.selfplay_installs = (first["install_id"],)
    result = {"version": 1, "opponent": "admiral", "games": 4, "wins": 3, "draws": 0}
    assert signed_post(client, "/v1/arena", first, result).status_code == 202
