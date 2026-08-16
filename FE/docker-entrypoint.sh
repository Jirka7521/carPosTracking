#!/bin/sh
# ---------------------------------------------------------------------------
# Renders the nginx site config before nginx starts.
#
# nginx cannot read environment variables, so everything that varies per
# deployment is substituted into its config here: the backend address, the path
# prefix the app is published under, and the SPA's Google Maps key.
#
# Vite inlines import.meta.env at build time, which would mean one image per
# environment and a rebuild whenever a key rotates. Instead index.html loads
# /config.js and services/runtimeConfig.ts reads what it left on `window` — and
# nginx answers that request straight from the site config rather than from a
# file this script writes. One substitution step, one artefact: there is no
# window in which the server is up but its configuration is missing.
#
# Dropped into /docker-entrypoint.d/, so the stock nginx image runs it during
# start-up, in name order, before the server accepts a single request. Note that
# the image starts nginx even when a script here exits non-zero, which is why
# nothing below writes anything the running server depends on until the values
# it needs have all been validated.
# ---------------------------------------------------------------------------
set -eu

# Only to clear a stale file left by an older image — nothing is written here.
CONFIG_PATH=/usr/share/nginx/html/config.js
NGINX_TEMPLATE=/etc/nginx/carpos-default.conf.template
NGINX_CONF=/etc/nginx/conf.d/default.conf

# ---------------------------------------------------------------------------
# Backend address for the /api proxy.
#
# Defaulted rather than required: the compose service name is what this resolved
# to before the address was configurable, so an image run without CARPOS_BE_URL
# behaves exactly as it always did.
# ---------------------------------------------------------------------------
BE_URL="${CARPOS_BE_URL:-http://api:8080}"

# Strip a trailing slash. With one, proxy_pass has a URI part, and nginx then
# REPLACES the matched /api/ prefix instead of passing the path through — every
# backend route would 404. Accepting both spellings avoids that trap.
BE_URL="${BE_URL%/}"

case "$BE_URL" in
    http://*|https://*) ;;
    *)
        echo "CARPOS_BE_URL must start with http:// or https:// (got '${BE_URL}')." >&2
        exit 1
        ;;
esac

# ---------------------------------------------------------------------------
# Path prefix the app is published under.
#
# The Cloudflare tunnel routes jimajer.cz/carPosFE/* to this container and
# forwards the prefix as part of the path — tunnels have no way to strip it. So
# nginx strips it, and stamps it into the served index.html as its <base href>
# (see nginx.conf). Requests without the prefix are untouched, which is what
# makes both spellings work at the same time.
#
# Defaulted rather than required, so the deployment this repo describes keeps
# working with no new variable in .env. Set CARPOS_BASE_PATH to the empty string
# to turn prefix handling off entirely.
# ---------------------------------------------------------------------------
BASE_PATH="${CARPOS_BASE_PATH-/carPosFE}"

# Trailing slashes are added by the config where they are needed; carrying one
# here would produce '//' in every generated path.
while [ "${BASE_PATH%/}" != "$BASE_PATH" ]; do
    BASE_PATH="${BASE_PATH%/}"
done

if [ -n "$BASE_PATH" ]; then
    # The value lands inside an nginx location, a rewrite and a regex, so it has
    # to be a plain absolute path. Anything else would either fail to parse or —
    # worse — parse into something other than what was meant.
    case "$BASE_PATH" in
        /*) ;;
        *)
            echo "CARPOS_BASE_PATH must start with '/' (got '${BASE_PATH}')." >&2
            exit 1
            ;;
    esac

    if [ -n "$(printf '%s' "$BASE_PATH" | tr -d 'A-Za-z0-9/_.-')" ]; then
        echo "CARPOS_BASE_PATH may contain only letters, digits, '/', '_', '.' and '-' (got '${BASE_PATH}')." >&2
        exit 1
    fi
fi

# Disabled: substitute a path no client will ever ask for. The prefix blocks then
# exist but never match, which keeps one template for both cases instead of two
# that can drift apart.
SUBSTITUTED_BASE_PATH="${BASE_PATH:-/__carpos-base-path-disabled__}"

# ---------------------------------------------------------------------------
# Google Maps key for the SPA's runtime configuration.
#
# It is substituted into the site config, which answers /config.js directly —
# there is no generated file any more. The old arrangement wrote one here, which
# meant the app's configuration depended on a step that could fail *after* the
# site config had been rendered; since the stock nginx image starts the server
# even when a /docker-entrypoint.d/ script exits non-zero, that produced a
# container serving the whole app with /config.js missing. Now the key and the
# server that serves it land in the same file, in one step: either both are in
# place or nginx has no config and does not start.
# ---------------------------------------------------------------------------
: "${CARPOS_GOOGLE_MAPS_API_KEY:?set CARPOS_GOOGLE_MAPS_API_KEY for the frontend container}"

# The value is embedded in a JavaScript string literal inside an nginx directive,
# so anything that could close that literal — or start an nginx variable — has to
# go. A Google Maps key is base64-ish and contains none of these characters; if
# this strips something, the key is wrong, and failing here beats serving a page
# whose config line is broken JavaScript.
SAFE_KEY=$(printf '%s' "$CARPOS_GOOGLE_MAPS_API_KEY" | tr -d '"\\'"'"'<>&`$;')

if [ "$SAFE_KEY" != "$CARPOS_GOOGLE_MAPS_API_KEY" ]; then
    # Naming the offending characters turns the most common cause into a
    # self-diagnosing message: quotes written around the value in .env, which
    # some parsers pass through verbatim as part of the key.
    BAD_CHARS=$(printf '%s' "$CARPOS_GOOGLE_MAPS_API_KEY" | tr -dc '"\\'"'"'<>&`$;')
    echo "CARPOS_GOOGLE_MAPS_API_KEY contains characters that are not valid in an API key: ${BAD_CHARS}" >&2
    echo "If the value is quoted in .env, remove the quotes — write KEY=AIza... not KEY=\"AIza...\"." >&2
    exit 1
fi

# A stale config.js from an older image would sit at the same path and is now
# dead weight — the location block answers before nginx ever looks at the disk.
# Removing it keeps the container from having two answers to the same question.
rm -f "$CONFIG_PATH"

# Rendered from the pristine template on every start, so this is idempotent —
# restarting a container that was already configured cannot double-substitute or
# leave a stale address behind. '|' as the delimiter because the values have '/'.
sed -e "s|__CARPOS_BE_URL__|${BE_URL}|g" \
    -e "s|__CARPOS_BASE_PATH__|${SUBSTITUTED_BASE_PATH}|g" \
    -e "s|__CARPOS_MAPS_KEY__|${SAFE_KEY}|g" \
    "$NGINX_TEMPLATE" > "$NGINX_CONF"

# Fail loudly if a placeholder survived: nginx would refuse to start anyway, but
# with a parse error that says nothing about the real cause.
if grep -q '__CARPOS_BE_URL__\|__CARPOS_BASE_PATH__\|__CARPOS_MAPS_KEY__' "$NGINX_CONF"; then
    echo "Failed to substitute the configuration into ${NGINX_CONF}." >&2
    exit 1
fi

echo "Proxying /api/ to ${BE_URL}."
echo "Serving /config.js from the site config (Maps key length: ${#SAFE_KEY})."

if [ -n "$BASE_PATH" ]; then
    echo "Serving the app at / and at ${BASE_PATH}/."
else
    echo "Serving the app at / only (CARPOS_BASE_PATH is empty)."
fi
