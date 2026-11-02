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
# Usernames live in .env, passwords in ./secrets/<NAME> (compose `secrets:`,
# mounted at /run/secrets/<NAME>); an <NAME> environment variable is still read
# when there is no file, so an older deployment keeps booting. The hashed password
# files are regenerated on every boot into a named volume, so changing a password
# is just `docker compose up -d --force-recreate mosquitto`. Every account is mandatory:
# a missing or empty password aborts the boot rather than starting a broker with
# an account that cannot log in.
#
# Two password files, one per listener (see mosquitto.conf):
#   passwd     every account - the internal 1883 listener.
#   passwd-ws  only what may log in from the internet over the 9001 WebSocket
#              listener: the tracker, plus the API's dashboard account while
#              DASHBOARD_ON_WEBSOCKETS is true (the default, so an API still
#              configured for ws://mqtt.local:9001 keeps working). admin and
#              healthcheck are never in it.
#
# Passwords are fed to mosquitto_passwd on stdin, not as arguments: an argument is
# readable by every process on the host (/proc/<pid>/cmdline) for as long as the
# command runs.
set -eu

SRC=/config-src
CONF=/mosquitto/config
PASSWD=/mosquitto/secrets/passwd
PASSWD_WS=/mosquitto/secrets/passwd-ws
# The healthcheck's mosquitto_pub/_sub options file (-o), so its password never
# appears on a command line either. Root-only; the probe runs as root.
HEALTHCHECK_OPTS=/mosquitto/healthcheck.opts

SECRETS=/run/secrets

require() {
	# $1 = env var name, $2 = its value
	if [ -z "$2" ]; then
		echo "entrypoint-passwd: $1 is empty - set it in secrets/$1 (or .env)" >&2
		exit 1
	fi
}

# secret <NAME>: the content of /run/secrets/<NAME> when that file exists, else
# the <NAME> environment variable. $(cat) drops the trailing newline an editor
# may have added; a newline INSIDE a password is refused, because mosquitto_passwd
# reads the password as a single line.
secret() {
	if [ -f "$SECRETS/$1" ]; then
		_value=$(cat "$SECRETS/$1")
	else
		eval "_value=\${$1:-}"
	fi
	case "$_value" in
		*"
"*)
			echo "entrypoint-passwd: $1 contains a line break" >&2
			exit 1
			;;
	esac
	printf '%s' "$_value"
}

ADMIN_PASSWORD=$(secret ADMIN_PASSWORD)
DEVICE_PASSWORD=$(secret DEVICE_PASSWORD)
DASHBOARD_PASSWORD=$(secret DASHBOARD_PASSWORD)
HEALTHCHECK_PASSWORD=$(secret HEALTHCHECK_PASSWORD)

require ADMIN_USER "${ADMIN_USER:-}"
require ADMIN_PASSWORD "$ADMIN_PASSWORD"
require DEVICE_USER "${DEVICE_USER:-}"
require DEVICE_PASSWORD "$DEVICE_PASSWORD"
require DASHBOARD_USER "${DASHBOARD_USER:-}"
require DASHBOARD_PASSWORD "$DASHBOARD_PASSWORD"
require HEALTHCHECK_USER "${HEALTHCHECK_USER:-}"
require HEALTHCHECK_PASSWORD "$HEALTHCHECK_PASSWORD"

case "${DASHBOARD_ON_WEBSOCKETS:-true}" in
	true|false) ;;
	*)
		echo "entrypoint-passwd: DASHBOARD_ON_WEBSOCKETS must be true or false" >&2
		exit 1
		;;
esac

# add_user <file> <user> <password>: appends (or creates) one entry. The password
# goes in twice because mosquitto_passwd asks for it twice.
add_user() {
	printf '%s\n%s\n' "$3" "$3" | mosquitto_passwd "$1" "$2" >/dev/null
}

mkdir -p "$CONF" "$(dirname "$PASSWD")"

# Fresh copies each boot, so editing the repo file and recreating is enough.
cp "$SRC/mosquitto.conf" "$CONF/mosquitto.conf"
cp "$SRC/acl" "$CONF/acl"

# Removing the password files first means mosquitto_passwd creates them as root
# and stays quiet; appending to a file owned by mosquitto makes it warn on every
# line. Created empty, so every account below is a plain append.
rm -f "$PASSWD" "$PASSWD_WS"
touch "$PASSWD" "$PASSWD_WS"
chmod 0600 "$PASSWD" "$PASSWD_WS"

add_user "$PASSWD" "$ADMIN_USER" "$ADMIN_PASSWORD"
add_user "$PASSWD" "$DEVICE_USER" "$DEVICE_PASSWORD"
add_user "$PASSWD" "$DASHBOARD_USER" "$DASHBOARD_PASSWORD"
add_user "$PASSWD" "$HEALTHCHECK_USER" "$HEALTHCHECK_PASSWORD"

add_user "$PASSWD_WS" "$DEVICE_USER" "$DEVICE_PASSWORD"
if [ "${DASHBOARD_ON_WEBSOCKETS:-true}" = "true" ]; then
	add_user "$PASSWD_WS" "$DASHBOARD_USER" "$DASHBOARD_PASSWORD"
fi

# One option per line, as mosquitto_pub/_sub -o expects.
umask 077
printf -- '-u %s\n-P %s\n' "$HEALTHCHECK_USER" "$HEALTHCHECK_PASSWORD" > "$HEALTHCHECK_OPTS"
umask 022

# Mosquitto wants its secrets non-world-readable and owned by the user it runs as.
chmod 0700 "$(dirname "$PASSWD")"
chmod 0600 "$PASSWD" "$PASSWD_WS" "$CONF/acl" "$HEALTHCHECK_OPTS"
chmod 0644 "$CONF/mosquitto.conf"
chown mosquitto:mosquitto "$PASSWD" "$PASSWD_WS" "$CONF/acl" "$CONF/mosquitto.conf"
chown -R mosquitto:mosquitto /mosquitto/secrets /mosquitto/data 2>/dev/null || true

exec /docker-entrypoint.sh "$@"
