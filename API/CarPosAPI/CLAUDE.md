# CLAUDE.md — CarPosAPI (ASP.NET Core backend)

Backend specifics. The shared rules — plan first, minimal diffs, git workflow,
subagents, and the cross-subsystem contracts — are in [../../CLAUDE.md](../../CLAUDE.md).

**Scope:** this project and its tests, [../CarPosAPI.Tests/](../CarPosAPI.Tests/). The
firmware, broker, FE and root files are counterparties — read them to learn the
contract, and ask before changing them (root rule 2).

## What this is

One application does everything server-side:

- **MQTT ingest** — a hosted service (`Services/Ingest/MqttIngestService`) subscribes to
  `devices/+` over WSS, decrypts each sealed envelope, validates the fix and writes it
  idempotently. It also ingests status messages and publishes acks, device config and
  schedule bundles.
- **REST API** over PostgreSQL for users, devices, positions, sharing and share links,
  schedules, device events and GDPR rights.

Stack: .NET 10 (`net10.0`), nullable + implicit usings on, MVC controllers, EF Core +
Npgsql, MQTTnet, xUnit tests. Endpoint reference, configuration, ops and the ACL setup
are in [README.md](README.md) — read the relevant section before changing behaviour.

## IMPORTANT — never use `var`

Always declare the explicit static type, on every new or modified line — including
`foreach` variables, `out` and `using` declarations, and LINQ locals:
`List<Device> devices = new List<Device>();`, never `var devices = …`. The one
exception is an anonymous type; prefer a named record, and if you truly can't, keep it
local and say so.

## Layout

```
Program.cs     composition root only: DI, auth, middleware, two CLI modes (schema-sync, import-device-key)
Controllers/   thin, one per resource; derive from ApiControllerBase
Services/<Feature>/   business rules + interfaces: Auth, Authorization, Common, Devices,
               Health, Ingest, Positions, Privacy, Provisioning, Scheduling, Security, Sharing
Data/          CarPosDbContext · Entities/ · Configurations/ (IEntityTypeConfiguration) · Migrations/ · SchemaSync/
Dtos/          request/response records — the wire contract, one per file
Options/       typed settings classes
Middleware/    GlobalExceptionHandler, CsrfProtectionMiddleware
```

One type per file, named after it; namespace mirrors the folder
(`CarPosAPI.Services.Devices`). The SDK picks new files up automatically.

## Build, test, run

```powershell
dotnet build                        # must be clean; watch for CARPOS001
dotnet test ..\CarPosAPI.Tests      # xUnit
dotnet run                          # http://localhost:5135 (https://localhost:7032)
dotnet format                       # before finishing
```

- The database must be up first: `docker compose up -d` in
  [../../Container/Postgres/](../../Container/Postgres/).
- If the build fails with file-lock errors (MSB3027/MSB3021), the API is still running —
  stop it; that is not a code error.
- Try endpoints with [CarPosAPI.http](CarPosAPI.http). OpenAPI is mapped in Development
  only.
- **Migrations:** add with `dotnet ef migrations add <Name>`, review the generated code,
  and never apply them automatically — the user applies them as `admin` (README →
  Database), or previews with `dotnet run -- schema-sync report`. No `EnsureCreated()`,
  no migrate-on-startup.

## Wire contract

- The full endpoint list is in [README.md → REST API](README.md#rest-api). Clients
  (FE `apiTypes.ts`, the firmware) are built against it — a shape change is a breaking
  change: flag it, don't absorb it.
- Controllers take and return **DTOs** (`record`s in `Dtos/`); an EF entity never reaches
  the wire. JSON is camelCase (System.Text.Json default) — don't change it.
- **Devices are addressed by their string `deviceId`** (the MQTT identity, e.g.
  `GNSS01`). The internal row Guid stays server-side.
- **Sessions are cookies, not bearer tokens.** The JWT is in an `HttpOnly` / `Secure` /
  `SameSite=Strict` cookie and never in a response body. Every mutating request must
  echo the readable `carpos_csrf` cookie as `X-CSRF-Token`
  ([Middleware/CsrfProtectionMiddleware.cs](Middleware/CsrfProtectionMiddleware.cs)).
- **No CORS policy, by design** — the FE is same-origin (nginx / Vite proxy `/api`).
  Don't add one.
- Expected failures are values, not exceptions: services return `OperationResult` /
  `ServiceError` with a code from `Services/Common/ErrorCodes.cs`, and
  `ApiControllerBase` maps the outcome to the status code. A new code needs an FE
  translation (root contracts table). The unexpected goes to `GlobalExceptionHandler`
  → ProblemDetails; never leak SQL, stack traces or exception messages.

## Business invariants — enforce server-side, every time

- `CanRead` is always true on an active grant; **`CanShare` implies
  `CanModifySettings`** — coerce it on, don't reject.
- Creating a device grants the creator all four capabilities. `additionalAccesses`
  reference users by email; unknown emails are skipped silently.
- **Authorize the resource, not just the request.** Every device / position / access /
  schedule operation re-checks the caller's grant; the `permissions` flags clients get
  are UX hints only. This is the most likely place to introduce a real vulnerability.
- A device the caller can't see answers **404, not 403** (403 confirms the id exists).
- Deleting a device is a **soft delete**. The only physical deletes are the two GDPR
  erasure paths: `DELETE /api/me` and `DELETE /api/devices/{id}/positions`
  (`Services/Privacy/`, `Services/Positions/`).
- **Positions are never deleted automatically** — no retention job, no TTL. The privacy
  policy states indefinite retention; don't change that without asking.
- **The data export never carries a secret** — it projects into `*ExportRow` records;
  `DataExportShapeTests` fails if a hash or key gets a field.
- `deviceId` is stored **exact-case** (MQTT topics are case-sensitive); user email is
  lower-cased.
- **Device RSA private keys** (encrypted at rest by `Services/Security/MasterKeyProtector`)
  are never selected into a DTO, logged, returned or used as an OpenAPI example.

## Configuration & secrets

- [appsettings.json](appsettings.json) is tracked, non-secret, and doubles as the example:
  every key present, secret ones **empty** (startup validation then fails loudly).
  Development values go in git-ignored `appsettings.Local.json` (loaded last) or
  user-secrets; production uses environment variables only.
  `appsettings.Development.json` is tracked and must stay secret-free.
- **A new secret = three edits:** empty placeholder in `appsettings.json`, real value in
  `appsettings.Local.json`, and a row in the README config table.
- Bind settings to a typed class in `Options/` with
  `AddOptions<T>().Bind(…).ValidateDataAnnotations().ValidateOnStart()`. Never read
  `IConfiguration` inside a service. No fallback JWT key, ever.

## Practices this codebase relies on

- `async` all the way, `CancellationToken` accepted and passed down to EF Core; no
  `.Result` / `.Wait()`.
- Classes are `sealed`, and `internal` unless they must be public (tests see internals
  via `InternalsVisibleTo`). Constructor injection of interfaces; `DbContext` and
  per-request services are Scoped.
- Reads use `AsNoTracking()` and project to the DTO **inside the query**; no queries in a
  loop; filter and page in SQL. Positions are always bounded by `deviceId` + time range
  + `MaxPositionsPerQuery` (1000).
- Multi-table writes run in a transaction **inside `Database.CreateExecutionStrategy()`**
  — the Npgsql retrying strategy rejects a bare user transaction. See `DeviceService`.
- Validation lives on the request DTO (DataAnnotations); `[ApiController]` returns the
  400 for you.
- `[Authorize]` everywhere; `[AllowAnonymous]` only on auth, the privacy policy, share-link
  redemption and `/health`. Auth, share redemption and the GDPR operations carry a
  `RateLimitPolicies` policy — give any new endpoint of that kind one too.
- Passwords go through the existing `Services/Auth/PasswordHasher`; don't add another.
- `ILogger<T>` with structured templates (`"Device {DeviceId} deactivated"`), never
  interpolation. **Never log secrets, PII or coordinates** — ingest logs device ids,
  counts and reasons only.
- Named constants over magic numbers, with a comment saying where the value comes from.
- XML `<summary>` on every type (its one job and its collaborators), `<param>` /
  `<returns>` on public methods. 4-space indent, Allman braces, `_camelCase` fields.

## Gotchas

- **`Services/Provisioning/ConfigTemplate.h.txt` is generated** from
  `ESP32/src/config/Config.example.h` on every build. Don't edit it by hand; commit the
  refreshed copy when the firmware template changes (warning `CARPOS001` when stale).
- **Npgsql and `DateTime` kinds:** `timestamp` (without time zone) wants
  `DateTimeKind.Unspecified`, `timestamptz` wants `Utc`, and the wrong kind throws.
  Store UTC and match the column type.
- **`positions` grows without bound** — an unbounded query will eventually take the API
  down.
- New MQTT topics the API publishes or subscribes to need a grant in
  `Container/MQTTBroker/mosquitto/acl`, or the broker silently drops them.
