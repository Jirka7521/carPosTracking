# CLAUDE.md — carPosTracking (repo root)

GNSS vehicle tracker, end to end: the ESP32 seals every fix (RSA-OAEP + AES-256-GCM)
and publishes it over MQTT; Mosquitto only ever sees ciphertext; the ASP.NET Core API
decrypts, validates and stores it in PostgreSQL; the React dashboard shows it.
A personal, non-commercial project — but it holds precise location data (GDPR), so
privacy rules in the subprojects are real requirements, not decoration.
Overview: [README.md](README.md).

This file holds the rules shared by every subproject. Each subproject's own
CLAUDE.md adds its specifics and loads when you work in that folder.

## Subprojects

| Folder | Stack | Verify (must pass before "done") | Notes |
|---|---|---|---|
| [ESP32/](ESP32/) | C++ / ESP-IDF 5.3 via PlatformIO | `pio run` (in `ESP32/`) | [ESP32/CLAUDE.md](ESP32/CLAUDE.md) |
| [API/CarPosAPI/](API/CarPosAPI/) | .NET 10, ASP.NET Core, EF Core, PostgreSQL | `dotnet build` + `dotnet test ..\CarPosAPI.Tests` | [API/CarPosAPI/CLAUDE.md](API/CarPosAPI/CLAUDE.md) |
| [FE/](FE/) | React 19 + Vite + TypeScript, i18next | `npm run build && npm run lint && npm run i18n:check && npm run i18n:missing` | [FE/CLAUDE.md](FE/CLAUDE.md) |
| [Container/](Container/) | Docker Compose: Mosquitto + nginx, Postgres, app | — | broker ACL lives here |
| [scripts/](scripts/) | PowerShell: `Dev-QuickMenu.ps1` (Ctrl+Shift+M), deploy publisher, schema sync | — | |
| [Application/](Application/) | **Generated** by `scripts/Publish-Deployment.ps1` | — | never edit by hand |
| [docs/](docs/) | GDPR records, data inventory, motion-wake thresholds | — | |

Dev box is Windows; PowerShell is the primary shell.

## Working agreement

1. **Plan first.** Restate the task, ask what's unclear, present a short plan, and wait
   for the go-ahead before editing source files.
2. **Stay in the subproject the task is about.** If the task needs a change in another
   one (a contract counterpart, the broker ACL, the generated `ConfigTemplate.h.txt`),
   name it in the plan and get explicit approval first.
3. **Minimal diffs.** No drive-by reformatting, renames or "while I'm here" edits. If you
   spot something worth fixing outside the task, mention it — don't change it.
4. **Comment the why**, in the banner-header style of the file you're editing. Match the
   density of the surrounding code.
5. **One type per file** (one class / record / enum / React component), organised into
   classes and methods — no free-floating helper code doing real work.
6. **Docs travel with the change.** Update every affected README in the same commit, and
   this or a subproject CLAUDE.md when a command, path or rule it states changes.
7. **Verify before claiming done.** Run the subproject's verify command (table above) and
   report the result — or the errors. Firmware behaviour that needs hardware: say so and
   give the user a short on-device test list.
8. **IMPORTANT — secrets never leave their files.** Never commit, print, log or paste
   `ESP32/src/config/Config.h`, `appsettings.Local.json`, any `.env`, device private keys,
   password hashes or tokens.

## Cross-subsystem contracts

These pairs must stay in lockstep. Change both sides **in the same commit** and say so
in the plan; a mismatch usually fails silently (the server "corrects" a device, the
broker drops messages, the dashboard shows a fallback).

| One side | Counterpart | What must match |
|---|---|---|
| `ESP32/src/crypto/PayloadCrypto` | `API/CarPosAPI/Services/Ingest/PayloadCryptoService` + `EnvelopeCodec` | sealed fix envelope, byte for byte |
| `ESP32/src/crypto/AckCrypto` | `API/CarPosAPI/Services/Ingest/AckSealer` | delivery-ack envelope |
| `ESP32/src/settings/SettingsCodec` | `API/CarPosAPI/Dtos/DeviceConfigDocumentDto` | `devices/<id>/config` JSON |
| `ESP32/src/settings/ScheduleCodec`, `ScheduleEvaluator` | `API/CarPosAPI/Dtos/DeviceScheduleBundleDto` + `ScheduleBundle*Dto`, `Services/Scheduling/ScheduleEvaluator.cs` | bundle shape **and** identical evaluation |
| `ESP32/src/mqtt/StatusPublisher`, `OfflineReason` | `API/CarPosAPI/Services/Ingest/DeviceStatusValidator`, `DeviceEventClassifier`, `DeviceStatusTopic` → FE `src/utils/deviceEvents.ts` badge tables | `devices/<id>/status` message: reason words, detail codes |
| `ESP32/src/config/Config.example.h` | `API/CarPosAPI/Services/Provisioning/ConfigTemplate.h.txt` | generated copy: `dotnet build` refreshes it (warning `CARPOS001` when stale) — commit it |
| any new or renamed MQTT topic | `Container/MQTTBroker/mosquitto/acl` | read/write grants — without one the broker ACKs and silently drops |
| `API/CarPosAPI/Dtos/` | `FE/src/services/apiTypes.ts` | camelCase wire shapes |
| `API/CarPosAPI/Services/Common/ErrorCodes.cs` | `FE/src/i18n/locales/*/errors.json` | every code translated (`npm run i18n:api-codes`) |

## Git workflow

- **IMPORTANT — never update `main`.** Never commit to, push to, merge into, rebase or
  reset `main`, and never merge a pull request. `main` changes only when the user merges
  a PR.
- **At the start of every task** check `git status` and `git branch --show-current`:
  - on `main` → `git fetch origin` then `git switch -c <type>/<kebab-summary> origin/main`;
  - on a feature branch that matches the task → keep working there;
  - on an unrelated branch, or with uncommitted changes you did not make → **ask** before
    branching or committing. Never stash, discard or reset someone else's work.
- **Branch names:** `feat/…`, `fix/…`, `refactor/…`, `docs/…`, `chore/…`, `test/…` +
  short kebab-case summary, e.g. `feat/motion-wake-hysteresis`.
- **Commit at every verified checkpoint** — after the verify command passes — and right
  before a risky step (large refactor, migration, contract change). `/rewind` does not
  undo changes made through the shell; git does.
- **Commit messages:** Conventional Commits with the subsystem as scope —
  `feat(esp32): …`, `fix(api): …`, `docs(fe): …`, `chore(container): …`,
  `feat(esp32,api): …` for a contract change. Imperative subject ≤ 72 chars; the body
  says *why*.
- **Stage explicit paths** and read `git diff --staged` for secrets before committing.
  Never `git add -A` / `git add .`, never `--no-verify`, never force-push, never amend a
  commit that has been pushed.
- **When the branch is reviewable:** `git push -u origin <branch>`, then open a PR into
  `main` — `gh pr create --base main` if the GitHub CLI is installed, otherwise give the
  user `https://github.com/Jirka7521/carPosTracking/compare/main...<branch>?expand=1`.
  The PR body says what and why, which subsystems changed, what was verified, and any
  contract/ACL follow-ups.

## Subagents — keep the expensive model for the decisions

Delegate work whose raw output you don't need in the main context: broad searches,
build and test logs, contract and docs drift checks. Keep design decisions, questions
to the user, and edits to code that ships in the main session.

| Agent (`.claude/agents/`) | Model | Use it for |
|---|---|---|
| `scout` | Haiku | "Where is X / what uses Y / which files do Z" across all subprojects |
| `build-verifier` | Haiku | Running a subproject's verify command and summarising the result |
| `contract-reviewer` | Sonnet | Checking a diff against the contracts table above |
| `docs-checker` | Haiku | Finding README / CLAUDE.md text a diff made stale |

- The built-in `Explore` and `general-purpose` agents **inherit the main model** unless
  told otherwise — pass `model: "haiku"` for lookups, `model: "sonnet"` when the task
  needs judgment.
- Run independent agents in parallel. Don't spawn one for a single grep or file read —
  that's cheaper done directly.
- Treat a report as leads: spot-check the claims you act on. Never put secrets in an
  agent prompt.
