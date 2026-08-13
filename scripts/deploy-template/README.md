# CarPos deployment bundle (API + frontend)

This folder is **self-contained**: it carries the built sources (`api-src/`,
`fe-src/`), the combined `docker-compose.yml`, and a `.env` with the real
secrets. It was produced on the dev machine by `scripts/Publish-Deployment.ps1`
(Ctrl+Shift+M) — you do not edit anything here by hand.

Copy the whole folder to the Raspberry Pi and bring it up with the steps below.

---

## Prerequisites (one-time, on the Pi)

1. **Docker starts on boot.** `restart: unless-stopped` only brings the
   containers back once the Docker daemon itself is running, so enable it:

   ```bash
   sudo systemctl enable --now docker
   ```

2. **Both addresses are free for the containers to claim.** `FE_BIND_ADDR` and
   `BE_BIND_ADDR` in `.env` are the addresses `docker-compose.yml` pins the two
   containers to on the macvlan network, so nothing else on the LAN — the Pi
   included — may already hold them, and your router's DHCP pool must not hand
   them out:

   ```bash
   ip -4 addr                       # neither address should be on the Pi itself
   ping -c1 192.168.124.5           # both should be silent while the stack is down
   ```

3. **The shared network and its neighbours exist.** Both services join the
   external `MQTTpublic` network, which is *created by the broker stack*, not by
   this one. Bring those up first (`Container/MQTTBroker` and
   `Container/Postgres` in the repo):

   ```bash
   # Only if it does not already exist. The flags are not optional: MQTTpublic is
   # a macvlan network on the LAN, and compose can only pin an ipv4_address on a
   # network whose subnet was configured at creation time.
   docker network create -d macvlan \
     --subnet 192.168.124.0/24 --gateway 192.168.124.1 -o parent=eth0 \
     MQTTpublic

   docker compose -f .../Container/Postgres/docker-compose.yml   up -d
   docker compose -f .../Container/MQTTBroker/docker-compose.yml up -d
   ```

   The API connects to Postgres as the **BE** role and to the broker as
   `MQTT_USERNAME`. Those credentials must match what the Postgres and MQTTBroker
   stacks were initialised with, or the API's `/health` stays unhealthy.

---

## Bring the stack up

From inside this folder:

```bash
docker compose up -d --build
```

`--build` compiles both images **on the Pi** (`dotnet publish` for the API,
`npm ci && npm run build` for the frontend) — expect the first build to take a
while on ARM. Later deploys reuse Docker's layer cache unless a dependency
changed.

---

## Addressing

Everything is keyed off two names, `FE_HOST` and `BE_HOST` — the LAN names your
DHCP/DNS server serves (e.g. `carposfe.local`, `carposbe.local`). Each is **also
registered as a Docker network alias** on `MQTTpublic`, so the same name resolves
on both sides of the container boundary:

| From | `carposbe.local` resolves to | reaching |
|---|---|---|
| A browser / the LAN | the host, via your DHCP server's DNS | `BE_BIND_ADDR:BE_PORT` → the published port |
| Inside a container | the API container, via Docker's embedded DNS | port `8080` directly, never leaving the network |

That alias is what makes the setup work. Without it, a container resolving
`carposbe.local` gets the *host* address and has to hairpin back in through the
published port — which is how the API ended up marked `unhealthy` while it was in
fact serving perfectly well.

Both addresses are **pinned** in `docker-compose.yml` (`ipv4_address`):
`192.168.124.5` for the API, `192.168.124.6` for the frontend. `MQTTpublic` is a
macvlan network, so those are ordinary LAN addresses, and left to Docker's IPAM
they would be handed out in whatever order the containers happened to start —
after a reboot that can put the frontend on the API's address and leave
`BE_BIND_ADDR` in `.env` pointing at the wrong container. The full map lives in
`Container/MQTTBroker/README.md`; keep the pins and the DNS records in step.

**Keep `BE_PORT=8080` and `FE_PORT=80`** (the container ports). Then a name means
exactly the same thing from either side. Change them and it does not: the alias
still reaches the container port while a browser needs the published one.

The remaining four variables are **derived** by the publisher and should not be
hand-edited — change the name or the port and re-run the wizard:

| Derived | From | Why it exists |
|---|---|---|
| `FE_BIND_ADDR`, `BE_BIND_ADDR` | `FE_HOST` / `BE_HOST` resolved to an IP | A published port is `<hostIP>:<hostPort>:<containerPort>` and Docker parses the host side as an IP literal — a name is rejected outright. |
| `BE_URL` | `BE_HOST` + the API's container port | The frontend's nginx proxies `/api/` here **and** the API healthcheck probes `$BE_URL/health`, so the check exercises the route the frontend actually uses. |
| `FE_URL` | `FE_HOST` + `FE_PORT` | Documentation only — no container reads it. |

`BE_URL` must have **no trailing slash and no path**: give nginx's `proxy_pass` a
URI part and it replaces the matched `/api/` prefix instead of passing the path
through, so every backend route 404s. The frontend's entrypoint strips a stray
slash defensively and prints the address it settled on at start-up.

### Path prefixes (the Cloudflare tunnel)

The tunnel publishes each service under a path — `jimajer.cz/carPosFE` for the
dashboard, `jimajer.cz/carPosAPI` for the API — and forwards that prefix **as
part of the path**, because a tunnel has no way to strip it. Each side strips its
own, and only from requests that actually carry it, so the prefixed and the
un-prefixed URLs are **live at the same time**: the tunnel reaches
`/carPosFE/login` while the published ports, the healthchecks and the in-network
`/api` proxy keep using the un-prefixed paths exactly as before.

| Variable | Default | Read by |
|---|---|---|
| `FE_BASE_PATH` | `/carPosFE` | the frontend container → nginx (`CARPOS_BASE_PATH`) |
| `API_PATH_BASE` | `/carPosAPI` | the API container → `Hosting__PathBase` |

Both are **optional** — the compose file defaults them, so an older `.env`
without them behaves correctly. Set one to an empty value to serve that side at
the root only. Change them only if the tunnel's routes are renamed; a rename is a
`docker compose up -d` away, not a rebuild.

---

## Verify

```bash
docker compose ps          # both carpos-api and carpos-fe should read "healthy"
docker compose logs -f api # watch the API connect to Postgres + the broker
docker compose logs fe | grep Proxying   # confirms the backend address in use

# Resolve the alias from inside the network, then use it the way nginx does:
docker exec carpos-fe getent hosts carposbe.local
docker exec carpos-fe wget -qO- http://carposbe.local:8080/health
```

If that `getent` prints nothing, the network alias is missing — check that
`BE_HOST` is set in `.env` and that the containers were recreated
(`docker compose up -d --force-recreate`) after it changed. An alias is applied
when a container joins the network, not on a plain restart.

- **`carpos-api`** — normal traffic reaches it only through the frontend's nginx
  over `MQTTpublic`; the SPA calls the relative path `/api`, never the API
  directly. Its `/health` checks the DB connection and MQTT ingest. Because nginx
  deliberately does not proxy `/health` or `/openapi`, the port is also published
  at `${BE_BIND_ADDR}:${BE_PORT}` so readiness can be probed from the LAN. Moving
  `BE_BIND_ADDR` off loopback publishes the **whole** API on that interface in
  plaintext, sign-in endpoints included — trusted networks only.

- **`carpos-fe`** — published on `${FE_BIND_ADDR}:${FE_PORT}`. A LAN address
  serves the dashboard over plain HTTP, so `AUTH_SECURE_COOKIES` must be `false`
  or the browser drops the session cookie and every request after sign-in 401s.
  Behind a TLS terminator (the Cloudflare tunnel) use loopback and leave the flag
  `true`. Its own healthcheck deliberately uses loopback, not `FE_URL`, so a
  tunnel or DNS hiccup cannot mark the container unhealthy.

---

## Update / redeploy

Re-run the wizard on the dev machine (Ctrl+Shift+M), copy the refreshed folder
over, and run `docker compose up -d --build` again. To roll a single service:
`docker compose up -d --build api` (or `fe`).

## Tear down

```bash
docker compose down        # stop + remove containers; images and the network stay
```

The `MQTTpublic` network is owned by the broker stack, so `down` here never
removes it.
