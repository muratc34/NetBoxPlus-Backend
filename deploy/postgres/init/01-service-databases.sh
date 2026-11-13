#!/bin/sh
# Creates a database per service, owned by a login role of the same name. Other roles (including the
# other services) cannot connect to it. Runs when the Postgres volume is empty, and again on every
# "./deploy/dev.ps1 up": a service added later gets its database without losing the others' data.
# Only "set -e": the image may source this file into its own entrypoint, where "set -u" would leak.
set -e

for name in $(echo "$NETBOXPLUS_DATABASES" | tr ',' ' '); do
  psql --quiet --set ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
       --set name="$name" --set password="$NETBOXPLUS_SERVICE_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'name', :'password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'name') \gexec
SELECT format('CREATE DATABASE %I OWNER %I', :'name', :'name')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'name') \gexec
SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'name') \gexec
SQL
done
