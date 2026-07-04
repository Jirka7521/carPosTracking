#!/bin/bash
# Runs automatically the first time the database is initialized (after the
# .sql scripts in this directory, thanks to the "02" prefix ordering).
#
# The image already bootstrapped the admin superuser (POSTGRES_USER) and the
# database. This script adds the one remaining role:
#   dashboard - read-only (SELECT) on all current and future tables.
#
# Its username/password come from the DASHBOARD_USER / DASHBOARD_PASSWORD env
# vars, which docker-compose injects from .env. This is a .sh (not .sql) script
# precisely so it can read those env vars; plain .sql files can't.
set -euo pipefail

: "${DASHBOARD_USER:?DASHBOARD_USER must be set in .env}"
: "${DASHBOARD_PASSWORD:?DASHBOARD_PASSWORD must be set in .env}"

# Connect as the admin superuser against the app database. The username is
# interpolated as a quoted identifier (:"...") and the password via format(%L)
# + \gexec, so both are quoted safely and can't break out of the SQL.
psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v db="$POSTGRES_DB" \
     -v owner="$POSTGRES_USER" \
     -v dash_user="$DASHBOARD_USER" \
     -v dash_pw="$DASHBOARD_PASSWORD" <<'EOSQL'
-- dashboard: read-only login.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'dash_user', :'dash_pw') \gexec

GRANT CONNECT ON DATABASE :"db"     TO :"dash_user";
GRANT USAGE   ON SCHEMA   public    TO :"dash_user";
GRANT SELECT  ON ALL TABLES IN SCHEMA public TO :"dash_user";

-- Future tables created by the admin owner become readable automatically,
-- so the schema can grow without re-granting every time.
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
  GRANT SELECT ON TABLES TO :"dash_user";
EOSQL

echo "Created read-only role: $DASHBOARD_USER."
