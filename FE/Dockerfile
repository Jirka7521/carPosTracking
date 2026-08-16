# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# Frontend — the React SPA, served by nginx, which also proxies /api to the
# backend container.
#
# That proxying is the point of putting them behind one server: the browser
# then talks to a single origin, so the session can live in a same-site
# HttpOnly cookie and there is no CORS configuration to get wrong.
#
# The image is environment-independent. The Google Maps key is substituted into
# the site config when the container starts (see docker-entrypoint.sh), where a
# location block serves it as /config.js — so the same build runs in staging and
# production and no VITE_ variables are baked in.
# ---------------------------------------------------------------------------

FROM node:22-alpine AS build
WORKDIR /src

# npm ci (not install): it installs exactly what package-lock.json pins and
# fails loudly if the two have drifted, which is what makes the build
# reproducible. Copying only the manifests first keeps this layer cached.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# ---------------------------------------------------------------------------
# Runtime image
# ---------------------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

COPY --from=build /src/dist /usr/share/nginx/html
# Not straight into conf.d/: this is a template whose backend address is filled in
# at start-up. docker-entrypoint.sh renders it to conf.d/default.conf on every
# boot, so changing CARPOS_BE_URL takes effect on a plain restart rather than
# needing the image rebuilt — and re-rendering from a pristine template keeps the
# result identical no matter how many times the container has been restarted.
COPY nginx.conf /etc/nginx/carpos-default.conf.template
# Included by every location block in default.conf — see the comment at the top
# of that file for why the headers cannot simply be set once at server level.
# Deliberately NOT in conf.d/: the stock nginx.conf globs that directory into
# the http context, which would apply these directives a second time, outside
# the server block where they belong.
COPY nginx-security-headers.conf /etc/nginx/carpos-security-headers.conf
COPY docker-entrypoint.sh /docker-entrypoint.d/40-carpos-config.sh

# The nginx image runs the scripts in /docker-entrypoint.d/ in name order before
# starting the server, so the config file exists before the first request can
# arrive. It must be executable regardless of what the checkout preserved.
RUN chmod +x /docker-entrypoint.d/40-carpos-config.sh

EXPOSE 80
