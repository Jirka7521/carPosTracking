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
#
# Base images are pinned by digest as well as tag: the tag says what it is, the
# digest makes a rebuild fetch exactly the bytes that were reviewed. Dependabot
# (.github/dependabot.yml) proposes the bumps.
# ---------------------------------------------------------------------------

FROM node:22-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914 AS build
WORKDIR /src

# npm ci (not install): it installs exactly what package-lock.json pins and
# fails loudly if the two have drifted, which is what makes the build
# reproducible. Copying only the manifests first keeps this layer cached.
#
# --ignore-scripts: no dependency gets to run code at install time. That is the
# hook a compromised package uses, and nothing in this tree needs it — the native
# bundler binaries arrive as platform-specific optional dependencies, not by a
# postinstall download.
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts

COPY . .
RUN npm run build

# ---------------------------------------------------------------------------
# Runtime image — nginx-unprivileged: the stock nginx build, but the master
# process runs as the nginx user (uid 101) from the start instead of as root.
# It still listens on port 80, because the tunnel's ingress targets
# http://carPosFE.local: the deployment compose lowers the container's
# net.ipv4.ip_unprivileged_port_start to 0 so a non-root process may bind it.
# Its temp paths and pid file live in /tmp, which is what lets the container run
# with a read-only root filesystem.
# ---------------------------------------------------------------------------
FROM nginxinc/nginx-unprivileged:1.30-alpine@sha256:f5116ed4606f3eb015dd4a8e28cb9b351476dc0ad4bc6e9f93b19cec130cdf03 AS runtime

# The base image switches to uid 101; the steps below install files and need root
# for a moment. USER is switched back at the end.
USER root

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
# arrive. It must be executable regardless of what the checkout preserved, and
# free of the CR a Windows checkout can leave behind: that would send the kernel
# looking for an interpreter named "/bin/sh<CR>" and fail the boot with 127.
RUN sed -i 's/\r$//' /docker-entrypoint.d/40-carpos-config.sh \
 && chmod +x /docker-entrypoint.d/40-carpos-config.sh

# Back to the image's unprivileged user. The entrypoint renders the site config
# into /etc/nginx/conf.d/ as this user: the base image makes that directory
# writable by it, and the deployment mounts a tmpfs there when the root
# filesystem is read-only.
USER 101

EXPOSE 80
