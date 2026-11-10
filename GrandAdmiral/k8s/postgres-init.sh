#!/bin/sh
# Runs once, when the database volume is first initialised (docker-entrypoint-initdb.d).
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v api_password="$GA_API_DB_PASSWORD" -v trainer_password="$GA_TRAINER_DB_PASSWORD" \
  -f /init/postgres-init.sql
