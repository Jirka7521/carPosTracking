#!/bin/sh
# Broker healthcheck.
#
# Publishes a retained token as the dedicated `healthcheck` account and reads it
# straight back. That is a full round-trip through connect, auth, ACL write, ACL
# read and delivery - so it fails on a broken password file, a mistyped ACL, or a
# broker that is wedged but still accepting TCP connections.
#
# It authenticates like any other client: there is no unauthenticated path into
# the broker, not even for its own probe. The credentials come from the options
# file entrypoint-passwd.sh writes at boot (-o), never from -u/-P: this runs every
# 30 seconds, and a command-line argument is readable by every process on the host.
# It probes the internal 1883 listener; the healthcheck account does not exist on
# the WebSocket one.
set -eu

HOST=127.0.0.1
PORT=1883
TOPIC=healthcheck/probe
TIMEOUT=3
OPTS=/mosquitto/healthcheck.opts

if [ ! -r "$OPTS" ]; then
	echo "healthcheck: $OPTS is missing - the entrypoint did not finish" >&2
	exit 1
fi

TOKEN="probe-$(date +%s)-$$"

# -i gives the probe a recognisable client id in the broker log; without it these
# connections show up as <unknown>, which is indistinguishable from a real client
# failing to connect.
if ! mosquitto_pub -o "$OPTS" -h "$HOST" -p "$PORT" -i "hc-pub-$$" \
	-t "$TOPIC" -m "$TOKEN" -q 1 -r >/dev/null 2>&1; then
	echo "healthcheck: publish to $TOPIC failed - bad credentials or ACL" >&2
	exit 1
fi

# The publish above is retained, so a fresh subscribe is handed the token at once;
# -C 1 exits on the first message rather than waiting out the full timeout.
GOT=$(mosquitto_sub -o "$OPTS" -h "$HOST" -p "$PORT" -i "hc-sub-$$" \
	-t "$TOPIC" -C 1 -W "$TIMEOUT" 2>/dev/null) || {
	echo "healthcheck: subscribed to $TOPIC but nothing came back within ${TIMEOUT}s" >&2
	exit 1
}

if [ "$GOT" != "$TOKEN" ]; then
	echo "healthcheck: payload mismatch - sent '$TOKEN', got '$GOT'" >&2
	exit 1
fi

echo "healthcheck: publish/subscribe round-trip OK"
