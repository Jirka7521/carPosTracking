#!/bin/bash
# Runs automatically the first time the database is initialized (after the
# .sql scripts in this directory, thanks to the "02" prefix ordering).
#
# The image already bootstrapped the admin superuser (POSTGRES_USER) and the
# database. This script adds the one remaining role:
#   BE - read/write (SELECT, INSERT, UPDATE, DELETE) on all current and future
#        tables. No DDL: it can't DROP or ALTER anything (owner-only).
#
# Its username/password come from the BE_USER / BE_PASSWORD env vars, which
# docker-compose injects from .env. This is a .sh (not .sql) script precisely
# so it can read those env vars; plain .sql files can't.
set -euo pipefail

: "${BE_USER:?BE_USER must be set in .env}"
: "${BE_PASSWORD:?BE_PASSWORD must be set in .env}"

# Connect as the admin superuser against the app database. The username is
# interpolated as a quoted identifier (:"...") and the password via format(%L)
# + \gexec, so both are quoted safely and can't break out of the SQL.
psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v db="$POSTGRES_DB" \
     -v owner="$POSTGRES_USER" \
     -v be_user="$BE_USER" \
     -v be_pw="$BE_PASSWORD" <<'EOSQL'
-- BE: read/write login for the backend.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'be_user', :'be_pw') \gexec

GRANT CONNECT ON DATABASE :"db"     TO :"be_user";
GRANT USAGE   ON SCHEMA   public    TO :"be_user";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"be_user";
-- Sequence access so inserts into serial/identity columns work.
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"be_user";

-- Future tables/sequences created by the admin owner become accessible
-- automatically, so the schema can grow without re-granting every time.
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"be_user";
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO :"be_user";
EOSQL

echo "Created read/write role: $BE_USER."
