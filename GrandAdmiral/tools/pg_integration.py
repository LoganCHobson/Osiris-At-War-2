"""Postgres integration test: the real schema (k8s/postgres-init.sql), the real roles, the real API
and trainer code, and proof that each role can do its job and nothing more.

    GA_PG_ADMIN_URL=postgresql+psycopg://postgres:pw@localhost:5432/grand_admiral \
    python tools/pg_integration.py

Run by CI against a throwaway Postgres service container. Never point it at production: it
creates and drops tables.
"""

import base64
import hashlib
import itertools
import json
import os
import pathlib
import subprocess
import sys
import time
import urllib.parse

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT / "ml"), str(ROOT / "server")]

import numpy as np  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402
from sqlalchemy import create_engine, delete, text, update  # noqa: E402
from sqlalchemy.exc import ProgrammingError  # noqa: E402

from app import security  # noqa: E402
from app.main import Settings, create_app  # noqa: E402
from osiris_ml import bundle, distill, pipeline, storage, toyenv  # noqa: E402


def role_url(admin_url, user, password):
    parts = urllib.parse.urlsplit(admin_url)
    netloc = f"{user}:{password}@{parts.hostname}:{parts.port or 5432}"
    return urllib.parse.urlunsplit((parts.scheme, netloc, parts.path, "", ""))


def must_fail(engine, statement, what):
    try:
        with engine.begin() as conn:
            conn.execute(statement)
    except ProgrammingError as exc:
        assert "permission denied" in str(exc) or "must be owner" in str(exc), exc
        print(f"  ok: {what} -> permission denied")
        return
    raise AssertionError(f"{what} should have been refused")


def main():
    admin_url = os.environ["GA_PG_ADMIN_URL"]
    admin = create_engine(admin_url)
    with admin.begin() as conn:
        for table in ("ga_arena", "ga_models", "ga_battles", "ga_installs"):
            conn.execute(text(f"DROP TABLE IF EXISTS {table} CASCADE"))

    parts = urllib.parse.urlsplit(admin_url)
    env = dict(os.environ, PGPASSWORD=parts.password or "")
    subprocess.run(["psql", "-h", parts.hostname, "-p", str(parts.port or 5432), "-U", parts.username,
                    "-d", parts.path.lstrip("/"), "-v", "api_password=api-pw", "-v", "trainer_password=trainer-pw",
                    "-q", "-f", str(ROOT / "k8s" / "postgres-init.sql")], check=True, env=env)
    print("schema applied")

    api_engine = create_engine(role_url(admin_url, "ga_api", "api-pw"))
    trainer_engine = create_engine(role_url(admin_url, "ga_trainer", "trainer-pw"))

    # Bootstrap (as the trainer, like the bootstrap Job).
    key = bundle.generate_key()
    model = distill.baseline(samples=8000, epochs=15).rounded()
    model.version = 1
    with trainer_engine.begin() as conn:
        pipeline.publish(conn, model, "released", {"bootstrap": True}, key, 1)
    print("bootstrapped v1 as ga_trainer")

    # The API, logged in as ga_api.
    server_key = os.urandom(32)
    settings = Settings(server_key=server_key, db_url=role_url(admin_url, "ga_api", "api-pw"), pow_bits=8)
    client = TestClient(create_app(settings))

    def register(ip):
        c = client.get("/v1/challenge").json()
        nonce = next(str(n) for n in itertools.count()
                     if security.leading_zero_bits(hashlib.sha256(f"{c['challenge']}:{n}".encode()).digest()) >= c["bits"])
        r = client.post("/v1/register", json={"challenge": c["challenge"], "nonce": nonce}, headers={"X-Forwarded-For": ip})
        assert r.status_code == 200, r.text
        return r.json()

    def upload(creds, record, ip):
        body = json.dumps(record).encode()
        ts = str(int(time.time()))
        return client.post("/v1/battles", content=body, headers={
            "Content-Type": "application/json", "X-GA-Install": creds["install_id"], "X-GA-Time": ts,
            "X-GA-Signature": security.request_signature(creds["token"], "POST", "/v1/battles", ts, body),
            "X-Forwarded-For": ip})

    rng = np.random.default_rng(0)
    for i in range(6):
        ip = f"198.51.{100 + i}.1"
        creds = register(ip)
        for _ in range(3):
            assert upload(creds, toyenv.play(model, rng), ip).status_code == 202
    dup = toyenv.play(model, rng)
    assert upload(creds, dup, ip).status_code == 202
    assert upload(creds, dup, ip).status_code == 200
    r = client.get("/v1/model/latest")
    assert r.status_code == 200 and bundle.verify(r.content, {1: key.public_key()}) == model.to_bytes()
    print("API flow ok as ga_api (register, upload, duplicate, model download)")

    print("ga_api privileges:")
    must_fail(api_engine, update(storage.battles).values(status="accepted"), "api updates battles")
    must_fail(api_engine, delete(storage.battles), "api deletes battles")
    must_fail(api_engine, update(storage.installs).values(blocked=False), "api unblocks installs")
    must_fail(api_engine, storage.models.insert().values(version=99, created_at=storage.now(), status="released",
                                                         bundle=b"x", sha256="x"), "api writes a model")
    must_fail(api_engine, update(storage.models).values(status="released"), "api changes model status")
    must_fail(api_engine, text("SELECT * FROM ga_arena"), "api reads arena")
    must_fail(api_engine, text("CREATE TABLE evil (x int)"), "api creates a table")

    print("ga_trainer privileges:")
    must_fail(trainer_engine, update(storage.models).values(bundle=b"tampered"), "trainer rewrites a model bundle")
    must_fail(trainer_engine, update(storage.battles).values(payload="{}"), "trainer rewrites a payload")
    must_fail(trainer_engine, storage.installs.insert().values(id="x", created_at=storage.now(), network="n",
                                                               blocked=False), "trainer creates installs")
    must_fail(trainer_engine, text("DROP TABLE ga_battles"), "trainer drops a table")

    cfg = pipeline.RoundConfig()
    summary = pipeline.run_round(trainer_engine, key, 1, {1: key.public_key()}, cfg)
    assert summary["accepted"] == 19 and summary["rejected"] == 0, summary
    assert summary["result"] == "waiting for data", summary
    print("trainer round ok as ga_trainer:", {k: summary[k] for k in ("accepted", "rejected", "result")})
    print("POSTGRES INTEGRATION PASSED")


if __name__ == "__main__":
    main()
