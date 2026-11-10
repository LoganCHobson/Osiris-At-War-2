"""Database tables shared by the ingest API and the trainer (SQLAlchemy Core; Postgres in the
cluster, SQLite in tests).

In Postgres the two services log in as different roles (k8s/postgres-init.sql): the internet-facing
API can add installs, battles and arena results and read released models, but it can't change or
delete anything it didn't just write, and it can't write models at all. Only the trainer can.
"""

import datetime as dt

from sqlalchemy import (Boolean, Column, DateTime, Integer, LargeBinary, MetaData, String, Table, Text,
                        create_engine, event)

metadata = MetaData()

installs = Table(
    "ga_installs", metadata,
    Column("id", String(36), primary_key=True),
    Column("created_at", DateTime(timezone=True), nullable=False),
    Column("network", String(32), nullable=False),        # keyed hash of the /24 (or /48); never the IP
    Column("blocked", Boolean, nullable=False, default=False),
    Column("blocked_reason", Text),
)

battles = Table(
    "ga_battles", metadata,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("battle_id", String(36), nullable=False, unique=True),
    Column("install_id", String(36), nullable=False, index=True),
    Column("network", String(32), nullable=False, index=True),
    Column("received_at", DateTime(timezone=True), nullable=False, index=True),
    Column("model_version", Integer, nullable=False),
    Column("opponent", String(8), nullable=False),
    Column("status", String(10), nullable=False, default="new", index=True),  # new | accepted | rejected
    Column("reason", Text),
    Column("payload", Text, nullable=False),
)

models = Table(
    "ga_models", metadata,
    Column("version", Integer, primary_key=True, autoincrement=False),
    Column("created_at", DateTime(timezone=True), nullable=False),
    Column("status", String(10), nullable=False),          # candidate | released | rejected | revoked
    Column("bundle", LargeBinary, nullable=False),
    Column("sha256", String(64), nullable=False),
    Column("report", Text),
)

arena = Table(
    "ga_arena", metadata,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("received_at", DateTime(timezone=True), nullable=False),
    Column("install_id", String(36), nullable=False),
    Column("version", Integer, nullable=False, index=True),
    Column("opponent", String(16), nullable=False),        # e.g. "admiral"
    Column("games", Integer, nullable=False),
    Column("wins", Integer, nullable=False),
    Column("draws", Integer, nullable=False),
)


def now():
    return dt.datetime.now(dt.timezone.utc)


def engine_for(url):
    engine = create_engine(url, pool_pre_ping=True, future=True)
    if url.startswith("sqlite"):
        @event.listens_for(engine, "connect")
        def _pragma(conn, _):
            conn.execute("PRAGMA foreign_keys=ON")
    return engine


def create_all(engine):
    """Tests and local dev only. In the cluster the schema comes from postgres-init.sql."""
    metadata.create_all(engine)
