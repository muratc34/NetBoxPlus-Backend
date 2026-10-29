#!/bin/sh
# Runs once, when the Postgres volume is empty. Creates a database per service, owned by a login
# role of the same name. Other roles (including the other services) cannot connect to it.
# Only "set -e": the image may source this file into its own entrypoint, where "set -u" would leak.
set -e

for name in $(echo "$NETBOXPLUS_DATABASES" | tr ',' ' '); do
  echo "Creating database and role '$name'"
  psql --set ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
       --set name="$name" --set password="$NETBOXPLUS_SERVICE_PASSWORD" <<'SQL'
CREATE ROLE :"name" LOGIN PASSWORD :'password';
CREATE DATABASE :"name" OWNER :"name";
REVOKE ALL ON DATABASE :"name" FROM PUBLIC;
SQL
done
