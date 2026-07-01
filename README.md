# Employee leave management system

See `docs/*` for the key documents on the project (PRD, sprint plan).

## Stack

ASP.NET Core (MVC/Razor Pages) on .NET 8, PostgreSQL via EF Core (Npgsql), ASP.NET Core
Identity for authentication/roles.

## Running locally (Docker only — no local .NET install required)

All builds, tests, and migrations run inside Docker containers. You only need Docker
installed on your machine.

### 1. One-time setup: generate a local HTTPS dev certificate

```bash
./scripts/generate-dev-cert.sh
```

This uses the dockerized .NET SDK to create a self-signed certificate at `.certs/aspnetapp.pfx`
(gitignored). Your browser will show a security warning for this cert — that's expected for
local development.

### 2. Start PostgreSQL and the app

```bash
docker compose up -d db      # start PostgreSQL and wait for it to become healthy
docker compose up app        # build + run the app in the foreground (Ctrl+C to stop)
```

On startup the app applies pending EF Core migrations and seeds the first HR account
automatically (see "Seed data" below). Then visit:

- https://localhost:8081 (self-signed cert — accept the browser warning)
- http://localhost:8080

The `docker-compose.yml` `app` service mounts the repo into the container and runs
`dotnet run`, so code changes are picked up on the next container restart
(`docker compose restart app`).

Running a second checkout in parallel (e.g. a `git worktree` for another branch)? Compose
names containers after the current directory by default, so a differently-named checkout
directory won't collide. If you ever do need an explicit, stable project name, pass
`docker compose -p <name> ...` or set the `COMPOSE_PROJECT_NAME` environment variable.

### Seed data

A first HR account is created on first run, idempotently (safe to restart the app
repeatedly — it will not recreate or duplicate the account):

| Email | Password |
|---|---|
| `hr@leaveautopilot.local` | `ChangeMe123!` |

These are **local development defaults only** — override them via the `Seed:HrEmail` /
`Seed:HrPassword` configuration keys (e.g. environment variables `Seed__HrEmail` /
`Seed__HrPassword`) for any shared or production environment. Never commit real credentials.

In the `Development` environment (the default for `docker compose up app`), a small set of
sample data is also seeded for local testing: a manager and two employees, each with Annual
and Medical quotas for the current year, all with password `Password123!`:

| Email | Role | Manager |
|---|---|---|
| `manager@leaveautopilot.local` | Manager | HR (fallback) |
| `alice@leaveautopilot.local` | Employee | manager@leaveautopilot.local |
| `bob@leaveautopilot.local` | Employee | manager@leaveautopilot.local |

Sample data seeding is controlled by `Seed:IncludeSampleData` (`true` in
`appsettings.Development.json`, `false` by default) and is also idempotent.

### Running tests

Tests run against a **separate** `leaveapp_test` database (dropped and recreated by the test
fixture on each run), so they never touch or wipe your local dev data:

```bash
docker compose run --rm app dotnet test LeaveAutopilot.sln
```

### Formatting

```bash
docker compose run --rm --no-deps app dotnet format LeaveAutopilot.sln
```

CI runs `dotnet format --verify-no-changes` and fails the build if formatting is off.

### Adding a new EF Core migration

```bash
docker compose run --rm --no-deps app bash -c \
  "dotnet tool restore && dotnet tool run dotnet-ef migrations add <MigrationName> \
   --project src/LeaveAutopilot.Web -o Data/Migrations"
```

### Ad-hoc dotnet commands

Any other `dotnet` command can be run the same way, without installing .NET locally:

```bash
docker compose run --rm --no-deps app dotnet <command>
```

### Browser-based E2E tests (Playwright)

Starting in Sprint 3, `e2e/` holds a Playwright (TypeScript) test project covering key UI
flows end-to-end. It's a separate toolchain from the .NET app, and — like everything
else — runs entirely in Docker; no local Node.js install required. The suite runs against
the app + Postgres started via this same `docker-compose.yml`, not its own server.

```bash
# 1. Generate the dev cert (if you haven't already) and start the app + Postgres:
./scripts/generate-dev-cert.sh
docker compose up -d --wait db app

# 2. Run the Playwright suite (installs npm deps on first run):
docker compose run --rm playwright bash -c "npm install && npx playwright test"
```

The `playwright` compose service's image tag (`mcr.microsoft.com/playwright:v1.48.0-jammy`)
already ships browsers pre-installed for that exact Playwright version, matching
`e2e/package.json`'s pinned `@playwright/test` version — so there's no separate browser
download/install step, and no risk of a client/browser version mismatch.

The suite runs in `Development`-environment sample data (see "Seed data" above) — HR logs
in as `hr@leaveautopilot.local`, and the negative HR-authorization test uses the seeded
`alice@leaveautopilot.local` Employee account. Reports/traces from a failed run land in
`e2e/playwright-report/` (a named Docker volume; see `docker-compose.yml`).

The suite grows every sprint from here (Sprint 4 onward each adds its own spec file to
`e2e/tests/`) rather than being re-set-up per sprint.

## Project structure

```
LeaveAutopilot.sln
src/LeaveAutopilot.Web/     ASP.NET Core MVC app, EF Core DbContext, entities, migrations, seed data
tests/LeaveAutopilot.Tests/ xUnit tests (unit + integration tests against a real Postgres instance)
e2e/                        Playwright (TypeScript) browser-based E2E tests, run against the docker-compose app + Postgres
docker-compose.yml          Postgres + app dev/build/test container + Playwright E2E runner
Dockerfile                  Multi-stage build (SDK -> ASP.NET runtime) for running the app as a container image
scripts/generate-dev-cert.sh  One-time local HTTPS dev certificate generation
.github/workflows/ci.yml    CI: build, format check, unit/integration test, and Playwright E2E jobs
```

## Continuous integration

Every pull request and push to `main` triggers `.github/workflows/ci.yml`, which runs two jobs:

- **`build-and-test`** — restores, builds, checks formatting, and runs the .NET unit/integration
  test suite against a PostgreSQL service container.
- **`playwright`** — builds and starts the app + Postgres via `docker compose`, then runs the
  Playwright E2E suite headless against it in Docker.

Both jobs must pass; either fails the check on a build error, formatting violation, or test
failure (unit, integration, or E2E).
