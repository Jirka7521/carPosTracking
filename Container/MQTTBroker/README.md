# Mosquitto MQTT broker + nginx (Cloudflare tunnel)

The broker stack for carPosTracking, built for **arm64 Linux** (Raspberry Pi &
friends) — both images are multi-arch and run natively there.

```
ESP32 / LAN  ──mqtt://…:1883──────────────────────────┐
                                                      ├─► mosquitto ─► (MQTTpublic) ─► carpos-postgres, CarPosAPI
browser / desktop ──wss://jimajer.cz/mqttBroker──►    │
                    Cloudflare ─► cloudflared ─► nginx ─► ws://mqtt.local:9001/
```

- **mosquitto** — listens on `1883` (plain MQTT, published to the LAN) and `9001`
  (WebSocket, *not* published: only nginx reaches it). It answers to `mqtt.local`
  on the Docker network via a network alias, which is the name nginx proxies to.
- **nginx** — accepts the plain HTTP that the Cloudflare tunnel delivers and
  upgrades `/mqttBroker` to a WebSocket against the broker. TLS is terminated by
  Cloudflare, so nothing here needs certificates.
- **autoheal** — restarts a container that has gone unhealthy (see below).
- **MQTTpublic** — the shared Docker network. Both this and the Postgres stack
  join it as `external`, so it has to exist before either comes up.

## Setup

```bash
docker network create MQTTpublic   # once per host; shared with the Postgres stack

cd Container/MQTTBroker
cp .env.example .env
nano .env                    # set all four passwords
docker compose up -d
docker compose ps            # mosquitto and nginx should both reach (healthy)
docker compose logs -f mosquitto
```

## Accounts & authorization

Nothing reaches the broker unauthenticated. `allow_anonymous false` rejects
credential-less clients at CONNECT, and every account that does get in is then
held to the per-user rules in [`mosquitto/acl`](mosquitto/acl) — a topic with no
matching rule is denied, so each account can touch only what is listed for it:

| account       | may publish        | may subscribe        |
|---------------|--------------------|----------------------|
| `admin`       | everything         | everything, `$SYS/#` |
| `GNSS01`      | `devices/GNSS01`   | `devices/GNSS01/cmd` |
| `dashboard`   | `devices/+/cmd`    | `devices/#`          |
| `healthcheck` | `healthcheck/probe`| `healthcheck/probe`  |

All four are mandatory — an empty user or password in `.env` aborts the boot
rather than producing a broker with an account nobody can log into.

The usernames in the ACL are literal: if you rename a `*_USER` in `.env`, rename
it in the ACL too, or that account keeps its login but loses every permission.

Passwords are hashed into a named volume at boot by
[`mosquitto/entrypoint-passwd.sh`](mosquitto/entrypoint-passwd.sh), straight from
`.env`. The file is rebuilt from scratch each boot rather than patched, so
rotating a password — or deleting an account — is an edit to `.env` plus
`docker compose up -d --force-recreate mosquitto`.

## Health & self-healing

The broker healthcheck ([`mosquitto/healthcheck.sh`](mosquitto/healthcheck.sh))
does not just poke the port: it logs in as the dedicated `healthcheck` account,
publishes a retained token to `healthcheck/probe` and reads it straight back. That
covers connect, auth, ACL write, ACL read and delivery in one go — a broken
password file, a mistyped ACL or a wedged broker all leave the TCP socket
cheerfully accepting connections, and only a real round-trip catches them.

The probe authenticates like every other client; there is no unauthenticated path
into the broker, not even for its own healthcheck. It holds one throwaway topic
and nothing else, so those credentials give no access to device data.

nginx's healthcheck likewise checks both that it serves and that it can still open
a connection to the broker's WebSocket listener, so it cannot report healthy while
sitting in front of a dead upstream.

Two guarantees on top of that:

- **nginx will not start until the broker is healthy** (`depends_on: condition:
  service_healthy`), and is restarted whenever the broker is. An nginx in front of
  a dead broker converts a clean connection refusal into 502s.
- **An unhealthy container gets restarted.** Docker's `restart: unless-stopped`
  only reacts to a container *exiting* — a container that is running but unhealthy
  is left alone forever. The `autoheal` sidecar watches health status and restarts
  anything labelled `autoheal=true`, which is both mosquitto and nginx.

The cost of autoheal is that it mounts the Docker socket (read-only, but that is
still effectively root on the host). If you would rather not pay that, delete the
`autoheal` service — the healthchecks keep working, they just stop triggering a
restart.

```bash
docker inspect --format '{{.State.Health.Status}}' carpos-mosquitto
docker inspect --format '{{json .State.Health}}' carpos-mosquitto   # last probe output
docker compose logs -f autoheal                                     # what it restarted, and when
```

## Cloudflare tunnel

Point the tunnel's ingress at nginx, **not** at Mosquitto. Two ways:

**cloudflared as a container** (preferred — no host ports involved). Attach it to
the `MQTTpublic` network and use the service name:

```yaml
ingress:
  - hostname: jimajer.cz
    path: /mqttBroker
    service: http://carpos-mqtt-nginx:80
  - service: http_status:404
```

**cloudflared on the host** — nginx is published on `127.0.0.1:8083` for exactly
this case:

```yaml
ingress:
  - hostname: jimajer.cz
    path: /mqttBroker
    service: http://localhost:8083
  - service: http_status:404
```

Cloudflare proxies WebSockets on the orange cloud by default; no extra setting is
needed. Clients then connect to `wss://jimajer.cz:443/mqttBroker`.

## Testing

A round-trip through the LAN listener, using the broker's own CLI so no client
install is needed:

```bash
# terminal 1 - subscribe as dashboard
docker compose exec mosquitto mosquitto_sub -h localhost -u dashboard -P '…' -t 'devices/#' -v

# terminal 2 - publish as the tracker
docker compose exec mosquitto mosquitto_pub -h localhost -u GNSS01 -P '…' -t devices/GNSS01 -m 'hello'
```

If the message arrives, auth *and* the ACL's delivery rules are both good. The
WSS path is worth testing separately with a WebSocket client against
`wss://jimajer.cz/mqttBroker`, since that exercises Cloudflare and nginx too.

## Troubleshooting

**Subscribe succeeds but no messages ever arrive.** Mosquitto needs an explicit
`topic read` rule before it will *deliver* to a subscriber; without one it still
ACKs the SUBSCRIBE and then drops every message silently. This bit this project
before — check `mosquitto/acl` first.

**Container restart-loops right after an ACL or credential change.** If the
`healthcheck` account can no longer publish and read back `healthcheck/probe`, the
broker reports unhealthy even though it is serving everyone else fine, and autoheal
keeps restarting it. `docker inspect --format '{{json .State.Health}}'
carpos-mosquitto` shows which step of the round-trip failed.

**`mosquitto: not found` / entrypoint fails immediately.** The shell scripts got
CRLF line endings. `.gitattributes` pins them to LF; re-clone or run
`dos2unix mosquitto/*.sh`.

**502 from Cloudflare.** nginx can't reach Mosquitto, or cloudflared isn't on the
`MQTTpublic` network. `docker compose exec nginx nc -z mqtt.local 9001` should
succeed.

**`Client <unknown> disconnected due to protocol error`.** Something set
`allow_zero_length_clientid false`. `mosquitto_pub`/`mosquitto_sub` send an empty
client id by default and let the broker assign one, so that setting rejects them
at CONNECT — before there is a client id to log, hence `<unknown>`. See the note
in `mosquitto/mosquitto.conf`.

**`chown: /mosquitto/config/...: Read-only file system`, or Mosquitto warning that
the ACL is world readable / not owned by mosquitto.** The config was bind-mounted
straight onto `/mosquitto/config`, where nothing can fix its permissions. It
belongs at `/config-src`, from which the entrypoint copies it into place.

```bash
docker compose down          # stop, keep data + password volume
docker compose down -v       # also wipe retained messages and passwords
```

The `MQTTpublic` network outlives both stacks; remove it with
`docker network rm MQTTpublic` once nothing is attached.
