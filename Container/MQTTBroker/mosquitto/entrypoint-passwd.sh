#!/bin/sh
# Stages config into place and builds the password file, then hands over to the
# image's normal entrypoint.
#
# The repo's files are bind-mounted read-only at /config-src rather than straight
# onto /mosquitto/config, because Mosquitto refuses to load an ACL or password
# file that is world readable or owned by another user - and nothing can chmod a
# file sitting on a read-only mount. So the live copies are made here instead,
# with the ownership Mosquitto wants. Mounting outside /mosquitto also stops the
# image's own entrypoint tripping over `chown -R /mosquitto`.
#
# Credentials live in .env only; the hashed password file is regenerated on every
# boot into a named volume, so changing a password is just
# `docker compose up -d --force-recreate mosquitto`. Every account is mandatory:
# a missing or empty password aborts the boot rather than starting a broker with
# an account that cannot log in.
set -eu

SRC=/config-src
CONF=/mosquitto/config
PASSWD=/mosquitto/secrets/passwd

require() {
	# $1 = env var name, $2 = its value
	if [ -z "$2" ]; then
		echo "entrypoint-passwd: $1 is empty - set it in .env" >&2
		exit 1
	fi
}

require ADMIN_USER "${ADMIN_USER:-}"
require ADMIN_PASSWORD "${ADMIN_PASSWORD:-}"
require DEVICE_USER "${DEVICE_USER:-}"
require DEVICE_PASSWORD "${DEVICE_PASSWORD:-}"
require DASHBOARD_USER "${DASHBOARD_USER:-}"
require DASHBOARD_PASSWORD "${DASHBOARD_PASSWORD:-}"
require HEALTHCHECK_USER "${HEALTHCHECK_USER:-}"
require HEALTHCHECK_PASSWORD "${HEALTHCHECK_PASSWORD:-}"

mkdir -p "$CONF" "$(dirname "$PASSWD")"

# Fresh copies each boot, so editing the repo file and recreating is enough.
cp "$SRC/mosquitto.conf" "$CONF/mosquitto.conf"
cp "$SRC/acl" "$CONF/acl"

# Removing the password file first means mosquitto_passwd creates it as root and
# stays quiet; appending to a file owned by mosquitto makes it warn on every line.
rm -f "$PASSWD"
# -c creates/truncates, so the first user resets the file and the rest append.
mosquitto_passwd -c -b "$PASSWD" "$ADMIN_USER" "$ADMIN_PASSWORD"
mosquitto_passwd -b "$PASSWD" "$DEVICE_USER" "$DEVICE_PASSWORD"
mosquitto_passwd -b "$PASSWD" "$DASHBOARD_USER" "$DASHBOARD_PASSWORD"
mosquitto_passwd -b "$PASSWD" "$HEALTHCHECK_USER" "$HEALTHCHECK_PASSWORD"

# Mosquitto wants its secrets non-world-readable and owned by the user it runs as.
chmod 0700 "$(dirname "$PASSWD")"
chmod 0600 "$PASSWD" "$CONF/acl"
chmod 0644 "$CONF/mosquitto.conf"
chown mosquitto:mosquitto "$PASSWD" "$CONF/acl" "$CONF/mosquitto.conf"
chown -R mosquitto:mosquitto /mosquitto/secrets /mosquitto/data 2>/dev/null || true

exec /docker-entrypoint.sh "$@"
