-- Grand Admiral database: schema and least-privilege roles. Applied once by the postgres init
-- container (k8s/postgres.yaml), with the role passwords passed in as psql variables:
--   psql -v api_password=... -v trainer_password=... -f postgres-init.sql
-- Must match GrandAdmiral/ml/osiris_ml/storage.py (tools/pg_integration.py checks it does).

\set ON_ERROR_STOP on

CREATE TABLE IF NOT EXISTS ga_installs (
    id              varchar(36) PRIMARY KEY,
    created_at      timestamptz NOT NULL,
    network         varchar(32) NOT NULL,
    blocked         boolean     NOT NULL DEFAULT false,
    blocked_reason  text
);
CREATE INDEX IF NOT EXISTS ga_installs_network ON ga_installs (network, created_at);

CREATE TABLE IF NOT EXISTS ga_battles (
    id             serial PRIMARY KEY,
    battle_id      varchar(36) NOT NULL UNIQUE,
    install_id     varchar(36) NOT NULL,
    network        varchar(32) NOT NULL,
    received_at    timestamptz NOT NULL,
    model_version  integer     NOT NULL,
    opponent       varchar(8)  NOT NULL,
    status         varchar(10) NOT NULL DEFAULT 'new',
    reason         text,
    payload        text        NOT NULL CHECK (length(payload) <= 600000)
);
CREATE INDEX IF NOT EXISTS ix_ga_battles_install_id ON ga_battles (install_id, received_at);
CREATE INDEX IF NOT EXISTS ix_ga_battles_network ON ga_battles (network, received_at);
CREATE INDEX IF NOT EXISTS ix_ga_battles_received_at ON ga_battles (received_at);
CREATE INDEX IF NOT EXISTS ix_ga_battles_status ON ga_battles (status);

CREATE TABLE IF NOT EXISTS ga_models (
    version     integer PRIMARY KEY,
    created_at  timestamptz NOT NULL,
    status      varchar(10) NOT NULL CHECK (status IN ('candidate', 'released', 'rejected', 'revoked', 'trial', 'failed')),
    bundle      bytea       NOT NULL,
    sha256      varchar(64) NOT NULL,
    report      text
);

CREATE TABLE IF NOT EXISTS ga_arena (
    id           serial PRIMARY KEY,
    received_at  timestamptz NOT NULL,
    install_id   varchar(36) NOT NULL,
    version      integer     NOT NULL,
    opponent     varchar(16) NOT NULL,
    games        integer     NOT NULL,
    wins         integer     NOT NULL,
    draws        integer     NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ga_arena_version ON ga_arena (version);

-- Nobody gets anything by default.
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

SELECT 'CREATE ROLE ga_api LOGIN' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ga_api') \gexec
SELECT 'CREATE ROLE ga_trainer LOGIN' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ga_trainer') \gexec
SELECT 'CREATE ROLE ga_metrics LOGIN' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ga_metrics') \gexec
ALTER ROLE ga_api WITH LOGIN PASSWORD :'api_password' NOSUPERUSER NOCREATEDB NOCREATEROLE CONNECTION LIMIT 40;
ALTER ROLE ga_trainer WITH LOGIN PASSWORD :'trainer_password' NOSUPERUSER NOCREATEDB NOCREATEROLE CONNECTION LIMIT 5;
ALTER ROLE ga_metrics WITH LOGIN PASSWORD :'metrics_password' NOSUPERUSER NOCREATEDB NOCREATEROLE CONNECTION LIMIT 3;
GRANT USAGE ON SCHEMA public TO ga_api, ga_trainer, ga_metrics;

-- The internet-facing API: add installs, battles and arena results; read released models.
-- It can't change or delete data, and it can't touch models at all.
GRANT SELECT, INSERT ON ga_installs TO ga_api;
GRANT SELECT, INSERT ON ga_battles TO ga_api;
GRANT USAGE ON SEQUENCE ga_battles_id_seq TO ga_api;
GRANT INSERT ON ga_arena TO ga_api;
GRANT USAGE ON SEQUENCE ga_arena_id_seq TO ga_api;
GRANT SELECT ON ga_models TO ga_api;

-- The trainer: judge battles, block installs, publish models, enforce retention.
GRANT SELECT, UPDATE (status, reason), DELETE ON ga_battles TO ga_trainer;
GRANT SELECT, UPDATE (blocked, blocked_reason) ON ga_installs TO ga_trainer;
GRANT SELECT, INSERT, UPDATE (status) ON ga_models TO ga_trainer;
GRANT SELECT, DELETE ON ga_arena TO ga_trainer;

-- The metrics exporter (Prometheus): read-only, and never sees model weights or signatures.
GRANT SELECT (id, created_at, network, blocked) ON ga_installs TO ga_metrics;
GRANT SELECT ON ga_battles TO ga_metrics;
GRANT SELECT ON ga_arena TO ga_metrics;
GRANT SELECT (version, created_at, status, report) ON ga_models TO ga_metrics;

ALTER ROLE ga_api SET statement_timeout = '5s';
ALTER ROLE ga_trainer SET statement_timeout = '10min';
ALTER ROLE ga_metrics SET statement_timeout = '10s';
