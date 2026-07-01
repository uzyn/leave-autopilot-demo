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

## Project structure

```
LeaveAutopilot.sln
src/LeaveAutopilot.Web/     ASP.NET Core MVC app, EF Core DbContext, entities, migrations, seed data
tests/LeaveAutopilot.Tests/ xUnit tests (unit + integration tests against a real Postgres instance)
docker-compose.yml          Postgres + app dev/build/test container
Dockerfile                  Multi-stage build (SDK -> ASP.NET runtime) for running the app as a container image
scripts/generate-dev-cert.sh  One-time local HTTPS dev certificate generation
.github/workflows/ci.yml    CI: build, format check, test (with a Postgres service container)
```

## Continuous integration

Every pull request and push to `main` triggers `.github/workflows/ci.yml`, which restores,
builds, checks formatting, and runs the full test suite against a PostgreSQL service
container. The check fails on any build error, formatting violation, or test failure.
