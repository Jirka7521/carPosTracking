# CLAUDE.md — CarPosAPI (ASP.NET Core web backend)

Guidance for Claude Code when working in this folder (`API/CarPosAPI/`). These
instructions override default behaviour — follow them exactly.

---

## ⭐ Working agreement (read first, every time)

1. **Only touch [`API/CarPosAPI/`](.).** This is the only project you may edit.
   **Never** modify [`../../ESP32/`](../../ESP32/),
   [`../../Container/`](../../Container/), or any root file. Those are the API's
   *counterparties* — read them to learn the contract, never "fix" them. If a
   task genuinely needs a change outside this folder, **stop and ask
   explicitly**, explain what and why, and wait for approval.
2. **Confirm the assignment before writing code.** Restate what you understood
   the task to be, ask any clarifying questions, and **present a short plan**.
   Wait for the go-ahead before you start editing source files. Do not jump
   straight into code.
3. **Never use `var`. Always declare the explicit, static type.**
   `List<Device> devices = new List<Device>();` — never
   `var devices = new List<Device>();`. This applies to every new or modified
   line, including `foreach` variables, `out` declarations, `using` declarations
   and LINQ locals. The one unavoidable exception is an **anonymous type**
   (LINQ projections, structured-log scopes) — restructure to a named type or a
   record if you can; if you truly can't, keep it local and say so.
4. **One class per file; organise code into classes and methods.** Every class,
   record, interface and enum gets its own `.cs` file named after it, in the
   folder for its layer (below). No multi-type files, no free helper code doing
   real work outside a class, no fat `Program.cs`.
5. **Comment generously.** Explain *why*, not just *what*. An XML `<summary>`
   banner on every class describing its one job and its collaborators;
   `<param>`/`<returns>` on public methods; inline comments for the reasoning
   behind a decision, not a restatement of the line below it. Match the density
   of the sibling projects ([`../../ESP32/src/`](../../ESP32/src/)).
6. **Never change what doesn't need to be changed.** Touch only what the task
   requires. No drive-by reformatting, renaming, reordering, or "while I'm here"
   edits; keep diffs minimal and focused so they are easy to review. If you spot
   something worth improving outside the task, mention it — don't silently
   change it.
7. **Build after changing code.** Run `dotnet build` and report whether it
   succeeded or list the errors. Do not claim a change is done without a clean
   build. If tests exist, run them too.
8. **Keep [`README.md`](README.md) current.** When endpoints, configuration, or
   run steps change, update this project's README in the **same** change.

---

## What this project is

**CarPosAPI** is the **web backend** of the *carPosTracking* system: a REST API
over PostgreSQL that serves users, devices, GNSS positions, and device sharing
(access control), authenticated with **JWT bearer tokens**.

- **Framework:** ASP.NET Core on **.NET 10** (`net10.0`), `Nullable` and
  `ImplicitUsings` enabled. Built with the **.NET CLI**.
- **API style:** **MVC controllers** (`[ApiController]`, attribute routing) — one
  controller per resource, thin, delegating to services.
- **Data access:** **EF Core + Npgsql**.

### Where it sits in the system

The ESP32 firmware encrypts every GNSS fix end-to-end and publishes it over
MQTT; a separate subscriber decrypts those messages and writes the resulting
rows. **This API never touches MQTT and never decrypts anything.** It reads what
has already been written and exposes it over HTTPS. Do not add MQTT or crypto
here without asking.

> **Current state: this is still the stock `dotnet new webapi` scaffold.**
> [`Program.cs`](Program.cs) serves `/weatherforecast` and the `WeatherForecast`
> record. All of it is placeholder — **delete it** as part of the first real
> feature; don't build around it.

---

## The contract you must implement

| Method & route | Purpose |
|---|---|
| `POST /api/auth/register`, `POST /api/auth/login` | returns `{ token, user }` |
| `GET /api/me` | current user's profile |
| `GET /api/users?…`, `GET /api/users/{id}` | search / fetch users (for sharing) |
| `PUT /api/users/{id}`, `PUT /api/users/{id}/password` | update names; change password |
| `GET /api/me/devices` | caller's devices, each with `customName` + `permissions` |
| `POST /api/devices`, `DELETE /api/devices/{id}` | create (201); **soft**-delete (204) |
| `PUT /api/me/devices/{id}/alias` | set/clear the caller's personal device name |
| `GET /api/positions?deviceId=&from=&to=` | positions, `timestamp DESC`, **max 1000** |
| `GET /api/access?deviceId=`, `POST /api/access`, `PUT /api/access/{id}`, `DELETE /api/access/{id}` | sharing grants |
| `GET /health` | liveness (unauthenticated) |

**Business invariants — enforce them server-side, every time:**

- **`CanRead` is always true** on any active grant; **`CanShare` implies
  `CanModifySettings`** (coerce it on, don't reject).
- **Creating a device grants the creator all four capabilities.**
  `additionalAccesses` entries reference users by **email**; unknown emails are
  **skipped silently**, and each produces one access grant.
- **Deleting a device is a soft delete**: it is marked inactive and stamped with
  a deactivation time. Records are never physically removed.
- Device `uuid` is stored **upper-cased**; user `email` **lower-cased**.
- The `permissions` flags a client receives are **UX hints**. Every mutation must
  be **re-authorized from the caller's access grant** — never trust the client.
- **Device RSA private keys are secrets.** Never select one into a DTO, never
  log it, never expose it on any endpoint, never include it in an OpenAPI
  example. Exclude it from the entity or guard every projection.

---

## Target project layout

Grow into this as features land — one type per file, folder per layer:

```
API/CarPosAPI/
├── Program.cs              ← composition root ONLY: DI, auth, middleware, MapControllers
├── Controllers/            ← thin: model-bind, authorize, call a service, map to ActionResult
├── Services/               ← the business rules (permissions, invariants) + interfaces
├── Data/                   ← CarPosDbContext, entities, IEntityTypeConfiguration<T>, migrations
├── Dtos/                   ← request/response records — the wire contract, one per file
├── Options/                ← strongly-typed settings classes (JwtOptions, …)
├── Middleware/             ← cross-cutting (exception handling → ProblemDetails)
├── appsettings.json        ← non-secret defaults only
└── README.md               ← keep current (rule 8)
```

Namespace mirrors the folder: `CarPosAPI.Controllers`, `CarPosAPI.Data`, …
Nothing to register by hand — the SDK project picks up new files automatically.

**Never let an EF entity reach the wire.** Controllers accept and return **DTOs**
(`record`s in `Dtos/`); entities stay behind the service layer. Serialization is
**camelCase** (System.Text.Json's default) — clients expect that, so don't change
the JSON naming policy.

---

## Build & run

```powershell
dotnet restore
dotnet build                       # must be clean before you claim a change is done
dotnet run                         # http://localhost:5135 (https://localhost:7032)
dotnet format                      # style/whitespace, before finishing
```

- Dev profiles are in [`Properties/launchSettings.json`](Properties/launchSettings.json);
  OpenAPI is mapped **in Development only**.
- The database must be running first:
  `docker compose up -d` in [`../../Container/Postgres/`](../../Container/Postgres/).
- CORS must name allowed origins **explicitly** in Development — **never
  `AllowAnyOrigin()` with credentials**, and never a wildcard policy in
  Production.
- Poke endpoints with [`CarPosAPI.http`](CarPosAPI.http) (its `/weatherforecast`
  request dies with the scaffold — replace it with the real ones).

---

## Configuration & secrets

- **No secret is ever committed.** The connection string (with the database
  password) and the **JWT signing key** come from **user-secrets** in
  development (`dotnet user-secrets set "ConnectionStrings:CarPos" "…"`) and from
  **environment variables** in production. `appsettings.json` carries only
  non-secret defaults; [`appsettings.Development.json`](appsettings.Development.json)
  is tracked on purpose (see the note in the root `.gitignore`) and must stay
  secret-free.
- **Bind settings to typed `Options` classes** (`JwtOptions`, …) with
  `builder.Services.AddOptions<T>().Bind(…).ValidateDataAnnotations()
  .ValidateOnStart()`, so a missing key fails **at startup**, not on first
  request. Never read `IConfiguration` deep inside a service.
- **The API must refuse to start in Production without a real JWT key.** No
  fallback default, and never a placeholder key checked into the repo.
- Never print, log, or echo: the JWT key, DB passwords, device private keys,
  password hashes, or raw tokens.

---

## C# / ASP.NET Core best practices (apply to every change)

**Async & cancellation**
- Everything touching the network or the DB is `async`, returns `Task`/
  `Task<T>`, and is suffixed `Async`. **No `.Result`, no `.Wait()`, no
  `.GetAwaiter().GetResult()`** — they deadlock and exhaust the thread pool.
- **Take a `CancellationToken` on every async method** and pass it all the way
  down to EF Core. Controller actions get it injected for free — plumb it.
- No `async void` anywhere except event handlers (there are none here).

**Dependency injection & lifetimes**
- Constructor injection only; depend on the interface, not the concrete type.
- `DbContext` and per-request services are **Scoped**. Never inject a scoped
  service into a singleton (captive dependency). Stateless helpers can be
  Singleton; **`HttpClient` is never `new`ed** — use `IHttpClientFactory`.
- Classes are `sealed` by default and `internal` unless they must be public.

**Controllers**
- `[ApiController]` + attribute routing; **thin** — no business logic, no LINQ
  over `DbContext`, no manual `ModelState` checks (`[ApiController]` returns a
  400 `ValidationProblemDetails` for you).
- Return `ActionResult<T>` with the **right status code**: `200`/`201 Created`
  (with a location) / `204 No Content` / `400` / `401` / `403` / `404` / `409`.
  A permission failure is **403**, a missing row is **404**, a duplicate is
  **409**.
- Validate input with **DataAnnotations on the request DTO** (`[Required]`,
  `[EmailAddress]`, `[StringLength]`, `[Range]`) so the rules live with the
  contract.

**Errors**
- One **global exception-handling middleware** (or `IExceptionHandler`) that logs
  the exception and returns **`ProblemDetails`** — the standard error shape. No
  `try`/`catch` around every action; no `catch (Exception) { }`; and **never leak
  a stack trace, SQL, or an exception message to the client** in Production.
- Expected failures (no permission, not found, duplicate) are **return values or
  typed results, not exceptions**. Exceptions are for the unexpected.

**Security**
- Passwords: **ASP.NET Core `PasswordHasher<T>`** (PBKDF2) or **BCrypt** —
  never SHA-256, never MD5, never unsalted.
- JWT: validate **issuer, audience, lifetime and signing key**
  (`ValidateIssuerSigningKey = true`), use a **≥ 32-byte** HMAC key, and put the
  user id in `sub`. `[Authorize]` on every controller;
  **`[AllowAnonymous]` only on register, login and `/health`.**
- **Authorize the resource, not just the request.** A valid token says *who* the
  caller is; every device/position/access operation must additionally check that
  caller's access grant. This is the single most likely place to introduce a
  real vulnerability — the client cannot protect you.
- Always parameterised queries (EF Core does this; if you ever write raw SQL, use
  `FromSqlInterpolated`, never string concatenation). Enable HTTPS redirection,
  and add **rate limiting** on the auth endpoints.

**EF Core**
- **`AsNoTracking()` on every read-only query** — all GET paths.
- Project to the DTO **in the query** (`Select(...)`) so you don't fetch columns
  you don't need — this is also how device private keys stay out of memory.
- **Beware N+1**: no queries inside a `foreach`; use `Include`/`Select` or a
  single join. Filter and paginate **in SQL**, never with `ToList()` then LINQ in
  memory (positions is `ORDER BY timestamp DESC LIMIT 1000` — in the query).
- One `SaveChangesAsync` per unit of work; wrap multi-table writes (create device
  + its access grants) in a **transaction**.
- Migrations are **reviewed before they're applied**; never call
  `EnsureCreated()`, and never auto-migrate a production database on startup.

**Logging & observability**
- `ILogger<T>` with **structured** messages (`_logger.LogInformation("Device
  {DeviceId} deactivated by {UserId}", …)`) — never string interpolation into the
  template, never `Console.WriteLine`.
- Log at the right level, **never log secrets or PII** (no passwords, tokens,
  keys, or full request bodies).
- Keep `/health` cheap and unauthenticated (`AddHealthChecks()`, plus a DB check).

**General C#**
- Nullable reference types are **on** — honour them. No `!` null-forgiving
  operator to silence a warning you haven't actually reasoned about.
- `record`s for DTOs (immutable wire contracts), classes for entities/services.
- **Named constants over magic numbers** (`MaxPositionsPerQuery = 1000`,
  `TokenLifetimeHours = 24`) — and comment where the value comes from.
- 4-space indent, no tabs, Allman braces, `_camelCase` private fields, `Async`
  suffix on async methods. `dotnet format` before you finish.

---

## Best-practices checklist (apply to every change)

- [ ] Assignment confirmed and a plan agreed **before** coding.
- [ ] **Nothing outside [`API/CarPosAPI/`](.) was touched** — or permission was
      asked for and granted explicitly.
- [ ] **No `var`** — every declaration has an explicit static type.
- [ ] One type per file, in the right layer folder; controllers stayed thin.
- [ ] Entities never crossed the wire; DTO shapes still match the documented
      contract.
- [ ] Every endpoint `[Authorize]`d and **re-authorized against the caller's
      access grant**; `CanRead`/`CanShare` invariants enforced server-side.
- [ ] `async` all the way down with `CancellationToken` plumbed through; reads
      use `AsNoTracking()`; no N+1.
- [ ] No secrets in tracked files, logs, or OpenAPI; device private keys never
      selected or returned.
- [ ] Code is thoroughly commented (why, not what), XML `<summary>` on new types.
- [ ] `dotnet build` succeeds (and `dotnet format` run); result reported.
- [ ] [`README.md`](README.md) updated in the same change.
- [ ] Change is self-contained and easy to review — no unrelated churn.

---

## Gotchas

- **The wire contract is a fixed target.** Clients are already built against the
  endpoints above and expect `camelCase` JSON and bearer auth. A shape change
  here is a breaking change there — flag it, don't absorb it.
- **Npgsql and `DateTime` kinds.** `TIMESTAMP` (*without* time zone) maps to
  `DateTimeKind.Unspecified` and Npgsql will **throw** if you hand it a
  `Kind.Utc` `DateTime` (and vice-versa for `timestamptz`). Decide this
  deliberately, be consistent, and store UTC.
- **`positions` grows without bound.** Always bound the query (`deviceId` + time
  range + `LIMIT`). An unfiltered `SELECT *` will eventually take the API down.
- **`.NET 10` + minimal-API scaffold.** The default template is minimal APIs; we
  are deliberately on **controllers** — you must add
  `AddControllers()` / `MapControllers()` yourself, and delete the
  `/weatherforecast` sample as you go.
