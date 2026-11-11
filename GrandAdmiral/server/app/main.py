"""Grand Admiral ingest + model API.

    GET  /healthz
    GET  /v1/challenge                  start anonymous registration
    POST /v1/register                   finish it (proof of work) -> install id + token
    POST /v1/battles                    upload one battle record (signed)
    POST /v1/arena                      self-play results (self-play installs only)
    GET  /v1/model/latest?have=N        newest released model bundle, 204 if N is current

The API never holds the model signing key and never writes models, so taking it over does not
let anyone push a model to players. The worst it allows is junk uploads, which the trainer is
built to throw away.
"""

import base64
import datetime as dt
import json
import logging
import os
import time
from collections import OrderedDict
from dataclasses import dataclass, field

from fastapi import FastAPI, Request, Response
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field, ValidationError
from sqlalchemy import and_, func, select
from sqlalchemy.exc import IntegrityError

from osiris_ml import storage
from osiris_ml.records import BattleRecord

from . import security

log = logging.getLogger("grand-admiral.api")


@dataclass
class Settings:
    server_key: bytes
    db_url: str
    pow_bits: int = 20
    max_body: int = 512 * 1024
    uploads_per_hour: int = 30
    uploads_per_day_per_network: int = 400
    registrations_per_day_per_network: int = 10
    requests_per_minute_per_ip: int = 120
    trusted_proxy_hops: int = 1
    selfplay_installs: tuple = ()
    create_schema: bool = False

    @classmethod
    def from_env(cls):
        path = os.environ.get("GA_SERVER_KEY_FILE")
        raw = open(path, "rb").read().strip() if path else os.environ.get("GA_SERVER_KEY", "").encode()
        key = base64.b64decode(raw) if raw else b""
        if len(key) < 32:
            raise RuntimeError("GA_SERVER_KEY(_FILE) must hold at least 32 random bytes, base64")
        return cls(
            server_key=key,
            db_url=os.environ["GA_DB_URL"],
            pow_bits=int(os.environ.get("GA_POW_BITS", "20")),
            trusted_proxy_hops=int(os.environ.get("GA_TRUSTED_PROXY_HOPS", "1")),
            selfplay_installs=tuple(i for i in os.environ.get("GA_SELFPLAY_INSTALLS", "").split(",") if i),
        )


class RegisterBody(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)
    challenge: str = Field(max_length=128)
    nonce: str = Field(max_length=64)


class ArenaBody(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)
    version: int = Field(ge=1, le=2**31)
    opponent: str = Field(pattern=r"^(admiral|captain|cadet|grand_admiral)$")
    games: int = Field(ge=1, le=1000)
    wins: int = Field(ge=0, le=1000)
    draws: int = Field(ge=0, le=1000)


@dataclass
class RateWindow:
    """In-memory sliding counter per client IP: sheds floods before they cost a DB query."""
    limit: int
    window: float = 60.0
    hits: OrderedDict = field(default_factory=OrderedDict)
    max_keys: int = 100_000

    def allow(self, key, now=None):
        now = now if now is not None else time.monotonic()
        start, count = self.hits.get(key, (now, 0))
        if now - start >= self.window:
            start, count = now, 0
        count += 1
        self.hits[key] = (start, count)
        self.hits.move_to_end(key)
        while len(self.hits) > self.max_keys:
            self.hits.popitem(last=False)
        return count <= self.limit


def _error(status, message):
    return JSONResponse({"error": message}, status_code=status)


def create_app(settings: Settings = None):
    settings = settings or Settings.from_env()
    engine = storage.engine_for(settings.db_url)
    if settings.create_schema:
        storage.create_all(engine)
    limiter = RateWindow(settings.requests_per_minute_per_ip)
    app = FastAPI(title="Grand Admiral", docs_url=None, redoc_url=None, openapi_url=None)
    app.state.settings = settings
    app.state.engine = engine

    def client_ip(request: Request):
        # Behind the ingress controller, the real client is the right-most address it appended.
        forwarded = [p.strip() for p in request.headers.get("x-forwarded-for", "").split(",") if p.strip()]
        if settings.trusted_proxy_hops and len(forwarded) >= settings.trusted_proxy_hops:
            return forwarded[-settings.trusted_proxy_hops]
        return request.client.host if request.client else "unknown"

    @app.middleware("http")
    async def guard(request: Request, call_next):
        if request.url.path != "/healthz" and not limiter.allow(client_ip(request)):
            return _error(429, "slow down")
        response = await call_next(request)
        response.headers["X-Content-Type-Options"] = "nosniff"
        response.headers["Strict-Transport-Security"] = "max-age=31536000"
        response.headers["Cache-Control"] = response.headers.get("Cache-Control", "no-store")
        return response

    async def read_body(request: Request):
        declared = request.headers.get("content-length")
        if declared is None or not declared.isdigit() or int(declared) > settings.max_body:
            return None
        chunks, size = [], 0
        async for chunk in request.stream():
            size += len(chunk)
            if size > settings.max_body:
                return None
            chunks.append(chunk)
        return b"".join(chunks)

    def authenticate(request: Request, body: bytes, conn):
        install_id = request.headers.get("x-ga-install", "")
        ok = security.check_request(settings.server_key, install_id, request.headers.get("x-ga-time"),
                                    request.headers.get("x-ga-signature"), request.method, request.url.path, body)
        if not ok:
            return None, _error(401, "bad signature")
        row = conn.execute(select(storage.installs.c.blocked).where(storage.installs.c.id == install_id)).first()
        if row is None:
            return None, _error(401, "unknown install")
        if row[0]:
            return None, _error(403, "install blocked")
        return install_id, None

    @app.get("/healthz")
    def healthz():
        return {"ok": True}

    @app.get("/v1/challenge")
    def challenge():
        return {"challenge": security.issue_challenge(settings.server_key), "bits": settings.pow_bits}

    @app.post("/v1/register")
    async def register(request: Request):
        body = await read_body(request)
        if body is None:
            return _error(413, "too large")
        try:
            data = RegisterBody.model_validate_json(body)
        except ValidationError:
            return _error(400, "bad request")
        install_id = security.check_challenge(settings.server_key, data.challenge, data.nonce, settings.pow_bits)
        if install_id is None:
            return _error(400, "challenge not solved")

        network = security.network_hash(settings.server_key, client_ip(request))
        with engine.begin() as conn:
            existing = conn.execute(select(storage.installs.c.blocked)
                                    .where(storage.installs.c.id == install_id)).first()
            if existing is None:
                since = storage.now() - dt.timedelta(days=1)
                recent = conn.execute(select(func.count()).select_from(storage.installs).where(
                    and_(storage.installs.c.network == network, storage.installs.c.created_at >= since))).scalar()
                if recent >= settings.registrations_per_day_per_network:
                    return _error(429, "too many registrations from this network")
                conn.execute(storage.installs.insert().values(id=install_id, created_at=storage.now(),
                                                              network=network, blocked=False))
            elif existing[0]:
                return _error(403, "install blocked")
        return {"install_id": install_id, "token": security.token_for(settings.server_key, install_id)}

    @app.post("/v1/battles")
    async def battles(request: Request):
        body = await read_body(request)
        if body is None:
            return _error(413, "too large")
        with engine.begin() as conn:
            install_id, failure = authenticate(request, body, conn)
            if failure:
                return failure
            try:
                record = BattleRecord.model_validate_json(body)
            except ValidationError as exc:
                log.info("rejected upload from %s: %s", install_id, exc.errors()[0].get("msg"))
                return _error(422, "invalid battle record")

            known = conn.execute(select(storage.models.c.version).where(and_(
                storage.models.c.version == record.model_version,
                storage.models.c.status.in_(("released", "revoked"))))).first()
            if known is None:
                return _error(422, "unknown model version")

            b = storage.battles.c
            hour = storage.now() - dt.timedelta(hours=1)
            trusted = install_id in settings.selfplay_installs
            if not trusted and conn.execute(select(func.count()).select_from(storage.battles).where(
                    and_(b.install_id == install_id, b.received_at >= hour))).scalar() >= settings.uploads_per_hour:
                return _error(429, "upload limit reached")
            network = security.network_hash(settings.server_key, client_ip(request))
            day = storage.now() - dt.timedelta(days=1)
            if not trusted and conn.execute(
                    select(func.count()).select_from(storage.battles).where(
                        and_(b.network == network, b.received_at >= day))).scalar() >= settings.uploads_per_day_per_network:
                return _error(429, "upload limit reached")

            try:
                with conn.begin_nested():
                    conn.execute(storage.battles.insert().values(
                        battle_id=record.battle_id, install_id=install_id, network=network,
                        received_at=storage.now(), model_version=record.model_version,
                        opponent=record.opponent, status="new",
                        payload=json.dumps(record.model_dump(by_alias=True), separators=(",", ":"))))
            except IntegrityError:
                return JSONResponse({"status": "duplicate"}, status_code=200)
        return JSONResponse({"status": "queued"}, status_code=202)

    @app.post("/v1/arena")
    async def arena(request: Request):
        body = await read_body(request)
        if body is None:
            return _error(413, "too large")
        with engine.begin() as conn:
            install_id, failure = authenticate(request, body, conn)
            if failure:
                return failure
            if install_id not in settings.selfplay_installs:
                return _error(403, "self-play only")
            try:
                data = ArenaBody.model_validate_json(body)
            except ValidationError:
                return _error(422, "bad arena result")
            if data.wins + data.draws > data.games:
                return _error(422, "bad arena result")
            conn.execute(storage.arena.insert().values(received_at=storage.now(), install_id=install_id,
                                                       **data.model_dump()))
        return JSONResponse({"status": "recorded"}, status_code=202)

    @app.post("/v1/selfplay/model")
    async def selfplay_model(request: Request):
        """The model self-play should play: the newest trial (a candidate that passed the offline checks
        and is waiting for self-play to prove it), else the newest release. Players never get trials."""
        body = await read_body(request)
        if body is None:
            return _error(413, "too large")
        with engine.connect() as conn:
            install_id, failure = authenticate(request, body, conn)
            if failure:
                return failure
            if install_id not in settings.selfplay_installs:
                return _error(403, "self-play only")
            m = storage.models.c
            row = None
            for status in ("trial", "released"):
                row = conn.execute(select(m.version, m.bundle, m.sha256).where(m.status == status)
                                   .order_by(m.version.desc()).limit(1)).first()
                if row is not None:
                    break
        if row is None:
            return Response(status_code=204)
        return Response(content=bytes(row.bundle), media_type="application/octet-stream",
                        headers={"Cache-Control": "no-store", "X-GA-Model-Version": str(row.version)})

    @app.get("/v1/model/latest")
    def latest_model(have: int = 0):
        with engine.connect() as conn:
            row = conn.execute(select(storage.models.c.version, storage.models.c.bundle, storage.models.c.sha256)
                               .where(storage.models.c.status == "released")
                               .order_by(storage.models.c.version.desc()).limit(1)).first()
        if row is None or row.version <= have:
            return Response(status_code=204, headers={"Cache-Control": "public, max-age=300"})
        return Response(content=bytes(row.bundle), media_type="application/octet-stream", headers={
            "Cache-Control": "public, max-age=300", "ETag": f'"{row.sha256}"', "X-GA-Model-Version": str(row.version)})

    return app


def asgi():
    """Entry point for uvicorn (--factory)."""
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
    return create_app()
