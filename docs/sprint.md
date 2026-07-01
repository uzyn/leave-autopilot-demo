# Employee Leave Management System — Sprint Plan

**Sprint cadence:** 2.5 days per sprint (half a work-week); Sprint 2.5 is a short 0.5-day non-blocking cleanup sprint inserted after Sprint 2's review
**Team:** Solo developer with AI augmentation
**Total sprints:** 9 (including Sprint 2.5 cleanup)
**Timeline:** 20.5 working days (~4 weeks) — Days 1 through 20.5
**Source:** `docs/prd.md` (no SDD; PRD §8 provides the technical grounding)
**Stack:** ASP.NET Core (MVC / Razor Pages), C#, PostgreSQL via EF Core (Npgsql), ASP.NET Core Identity

**v1 Scope:** Web-based leave management for a small company (<50 users) — email/password auth with Employee/Manager/HR roles, HR administration (accounts, manager assignment, per-employee annual quotas), leave application with working-day/half-day counting, single-manager approval workflow, cancel/withdraw with balance restoration, and balance/history views. Everything on the PRD Out-of-Scope list is deferred (see *Deferred to v2*).

---

## How to use this document

- Each **acceptance criterion is a checkbox** — check it when verified, not merely when coded.
- Each sprint has a **Definition of Done** checkbox that gates moving on.
- Status legend for the summary table: ⬜ Not started · 🟨 In progress · ✅ Done
- Sprints are ordered by hard dependency. Do not start a sprint until its listed dependencies are ✅.

## Assumptions & resolved PRD open questions

These were open in PRD §11 and are resolved here for v1. Flag if any is wrong before the affected sprint.

1. **Manager-less approvals:** Users without an assigned manager (e.g., the top HR/owner) have their requests routed to **HR**, who acts as fallback approver. *(Sprint 5)*
2. **Year boundary:** Quotas are per **calendar year**; requests that span across 31 Dec → 1 Jan are **disallowed** in v1 (validation error). *(Sprint 4)*
3. **Password reset:** Since notifications are out of scope, **HR resets passwords** on a user's behalf. *(Sprint 2)*
4. **First admin:** The initial HR account is created via a **seed script** on first run. *(Sprint 1)*
5. **Medical/Sick evidence:** No attachments/certificates in v1.
6. **Team calendar / overlap detection:** Not in v1.
7. **UI testing:** From **Sprint 3 onward**, each sprint adds Playwright browser-based end-to-end tests covering that sprint's key UI flows, on top of unit/integration tests. Playwright is introduced in Sprint 3 (first sprint with meaningful HR-admin UI to exercise) and its suite grows sprint over sprint; it runs in the GitHub Actions CI pipeline (added in Sprint 1) against the app + Postgres running in Docker.

---

## Sprint 1 — Foundation & Data Model (Days 1–2.5) [DONE]

**Goal:** Stand up a running ASP.NET Core app wired to PostgreSQL with the full domain schema, migrations, and seed data — the foundation everything else builds on.

**Dependencies:** None

### S1-1 — Project scaffolding & configuration

*As a developer, I want a correctly configured ASP.NET Core project so that I can build features on a stable base.*

**Technical context:** ASP.NET Core MVC/Razor Pages project targeting current .NET LTS; EF Core with the Npgsql provider; connection string via configuration/user-secrets; HTTPS redirection and a shared `_Layout`.

**Acceptance criteria:**
- [x] Solution builds and runs; a home/landing page renders over HTTPS.
- [x] EF Core + Npgsql configured; app connects to a local PostgreSQL instance using a configurable connection string (no secrets committed).
- [x] `.gitignore`, solution structure, and a shared responsive layout in place.
- [x] README documents how to run the app and point it at PostgreSQL.

### S1-2 — Domain data model & migrations

*As a developer, I want the domain entities and schema so that features can persist and read leave data.*

**Technical context:** Core entities — `User`/Employee (name, email, role, `ManagerId` self-reference, `IsActive`), `LeavePolicy` (employee × leave type × year × allocated days), `LeaveRequest` (requester, type, start/end dates, start/end half-day flags, computed day count, state, decision metadata: decided-by, decided-at, note, created-at). Leave type is an enum: `Annual`, `Medical`, `Unpaid`. Request state is an enum: `Pending`, `Approved`, `Rejected`, `Cancelled`, `Withdrawn`.

**Acceptance criteria:**
- [x] Entities defined with correct relationships (self-referencing manager; policy and request FKs to employee).
- [x] EF Core migration created and applies cleanly to an empty database.
- [x] Constraints enforced at DB level where sensible (unique employee email; unique policy per employee+type+year; non-negative allocated days).
- [x] Enums for leave type and request state persist correctly.

### S1-3 — Seed data & first HR account

*As an operator, I want an initial HR account and sample data so that the app is usable immediately and demoable.*

**Acceptance criteria:**
- [x] Seed routine creates one HR user with a known/configurable initial password on first run (idempotent — safe to run repeatedly).
- [x] Dev seed optionally creates a few sample employees, a manager, and quotas for local testing.
- [x] Seeding is documented and does not run destructively against existing data.

### S1-4 — Continuous integration (GitHub Actions)

*As the team, I want automated CI so that every PR is built and tested before merge.*

**Technical context:** GitHub Actions workflow triggered on pull requests (and pushes to `main`): restore/build the solution, run `dotnet test`, and (if practical) spin up PostgreSQL as a service container for integration tests.

**Acceptance criteria:**
- [x] `.github/workflows/ci.yml` builds the solution and runs the test suite on every PR.
- [x] CI fails the check when a build error or test failure occurs.
- [x] Workflow uses a PostgreSQL service container (or equivalent) if any tests need a live database.

**Definition of Done:**
- [x] App boots, migrates a fresh DB, seeds the HR account, and serves the landing page over HTTPS.
- [x] CI workflow runs on pull requests and passes on this sprint's own PR.

---

## Sprint 2 — Authentication & Authorization (Days 2.5–5) [DONE]

**Goal:** Users log in/out with email + password, roles are enforced across the app, and HR can reset passwords.

**Dependencies:** Sprint 1

### S2-1 — Email/password authentication

*As a user, I want to log in and out securely so that only authorized people access the system.*

**Technical context:** ASP.NET Core Identity over the existing `User` entity (or Identity user extended with domain fields); password hashing via Identity defaults; cookie-based session.

**Acceptance criteria:**
- [x] User can log in with a valid email/password and is rejected with a clear message on invalid credentials.
- [x] Passwords are stored only as secure hashes (verified by inspecting the DB — no plaintext).
- [x] User can log out; the session is invalidated afterward.
- [x] Deactivated (`IsActive = false`) users cannot log in.

### S2-2 — Roles & authorization

*As the business, I want role-based access so that employees, managers, and HR see and do only what they should.*

**Technical context:** Three roles — Employee, Manager, HR. Everyone is also an employee (has balances, can apply). Use policy/role-based authorization on controllers/pages; role-aware navigation and landing.

**Acceptance criteria:**
- [x] Roles Employee, Manager, HR exist and are assigned to seeded users.
- [x] Every controller action / page enforces authorization server-side (unauthenticated → login; unauthorized role → forbidden).
- [x] Navigation and landing content adapt to the current user's role(s).
- [x] A negative test confirms an Employee cannot reach HR-only or manager-only actions by direct URL. <!-- Verified against the HR-only reset-password action; no manager-only action exists yet (Sprint 5). Reviewer accepted this as a reasonable scope substitution. -->

### S2-3 — HR-assisted password reset

*As HR, I want to reset a user's password so that people who forget theirs can regain access without email notifications.*

**Acceptance criteria:**
- [x] HR can set a new password for any active user from an HR screen.
- [x] The affected user can log in with the new password; the old one no longer works.
- [x] Action is HR-only and enforced server-side.

**Definition of Done:**
- [x] A seeded HR user logs in, an Employee logs in, role restrictions hold, and HR can reset another user's password.

---

## Sprint 2.5 — Non-blocking Cleanup (Days 5–5.5) [IN PROGRESS]

**Goal:** Address non-blocking findings from the Sprint 2 review before starting HR administration — small, well-scoped fixes and test-coverage gaps identified by the reviewer, none of which blocked merge but which should not be left to drift.

**Dependencies:** Sprint 2 (merged)

### Stories

#### S2.5-1: Fix dead validation attribute and cover missing test paths
**Context:** Reviewer found `ResetPasswordViewModel.UserId`'s `[Required(ErrorMessage = "Select a user.")]` attribute is dead code — `UserId` is a non-nullable `Guid`, so `[Required]` never fails on it. Submitting the placeholder `-- select a user --` option (empty string) actually fails at model-binding time with MVC's generic type-conversion error instead of the intended "Select a user." message. Not a security/correctness bug (the form is still rejected either way), just the wrong UX message. Also flagged: no test exists for the Manager-role branch of `Home/Index.cshtml` (`else if (User.IsInRole(Roles.Manager))`), and no test exists for `HrController.ResetPassword`'s "no user selected" (empty GUID) submission path.
- [ ] Make `ResetPasswordViewModel.UserId` nullable (`Guid?`) or otherwise handle the empty-selection case explicitly so the "Select a user." message actually surfaces.
- [ ] Add a test asserting the Manager-role landing-page branch renders correctly for a Manager user.
- [ ] Add a test covering `HrController.ResetPassword` submitted with no user selected.

#### S2.5-2: Add CSRF-rejection test coverage
**Context:** Reviewer noted that every existing form-submission test fetches a valid antiforgery token first, but nothing verifies that a POST to `/Account/Login`, `/Account/Logout`, or `/Hr/ResetPassword` with a missing/invalid `__RequestVerificationToken` is actually rejected. The PRD's NFR explicitly requires CSRF protection; these controllers currently rely on framework defaults with no regression coverage, so a future change (e.g., an accidental `[IgnoreAntiforgeryToken]`) could silently disable it.
- [ ] Add a test per POST action (`/Account/Login`, `/Account/Logout`, `/Hr/ResetPassword`) asserting a request with a missing or invalid antiforgery token is rejected.

#### S2.5-3: Fix HomeController.Error authorization and login redirect
**Context:** `HomeController.Error` inherits the class-level `[Authorize]` added in Sprint 2. If an unhandled exception occurs for an anonymous request (e.g., inside `AccountController` before sign-in), `UseExceptionHandler("/Home/Error")` redirects to an action that now requires authentication, bouncing the user to the login page instead of showing a friendly error page — masking the real failure. Separately, reviewer noted `GET /Account/Login` does not redirect an already-authenticated user back to home; harmless today but can be confusing.
- [ ] Add `[AllowAnonymous]` to `HomeController.Error` so anonymous requests see the friendly error page instead of a login redirect.
- [ ] Redirect an already-authenticated user hitting `GET /Account/Login` to the home page instead of showing the login form.

#### S2.5-4: Sprint 1 backlog carryover — container naming and seeder test coverage
**Context:** Two items from the Sprint 1 review backlog are ready to action now rather than continue accumulating. The `leave-postgres` container name in `docker-compose.yml` is hardcoded, so a second checkout of the repo (e.g. a `git worktree` used for parallel branch work) fails `docker compose up -d db` with a container-name collision. Separately, `DataSeeder.EnsureRolesAsync`/`EnsureUserAsync`/`EnsurePolicyAsync` have `InvalidOperationException` throw paths (triggered when an ASP.NET Core Identity operation fails) that are currently untested.
- [ ] Derive the Postgres container name in `docker-compose.yml` from the project/directory or an environment variable so parallel checkouts (worktrees) don't collide.
- [ ] Add test coverage for the `InvalidOperationException` throw paths in `DataSeeder.EnsureRolesAsync`, `EnsureUserAsync`, and `EnsurePolicyAsync`.

**Definition of Done:**
- [ ] All Sprint 2.5 test additions pass in CI; the dead-code validation fix and both authorization fixes are verified manually and by test.

---

## Sprint 3 — HR Administration (Days 5.5–8) [NOT STARTED]

**Goal:** HR can fully configure the organization — create/manage employees, assign each a manager, and set annual quotas per leave type. This must precede leave application, which depends on users, managers, and quotas existing.

**Dependencies:** Sprint 2

### S3-1 — Employee account management

*As HR, I want to create, edit, and deactivate employee accounts so that only current staff have access.*

**Acceptance criteria:**
- [ ] HR can create an employee (name, email, role, initial password) — duplicate email is rejected.
- [ ] HR can edit an employee's name, email, and role.
- [ ] HR can deactivate/reactivate an employee; deactivated users cannot log in and do not appear as assignable managers.
- [ ] All employee-admin actions are HR-only (server-side enforced).

### S3-2 — Manager assignment

*As HR, I want to assign each employee exactly one manager so that leave requests route to the right approver.*

**Acceptance criteria:**
- [ ] HR can set/change an employee's single assigned manager from the list of active users.
- [ ] An employee may have no manager (allowed — handled by HR fallback in Sprint 5).
- [ ] Self-assignment as one's own manager is prevented.
- [ ] Deactivated users are not selectable as managers.

### S3-3 — Annual quota management

*As HR, I want to set each employee's annual quota per leave type so that entitlements match their contract.*

**Technical context:** Quota = `LeavePolicy` rows per employee × balance-backed type (`Annual`, `Medical`) × current calendar year. `Unpaid` has no quota.

**Acceptance criteria:**
- [ ] HR can set/edit allocated days for Annual and Medical for a given employee and year.
- [ ] Allocated days must be non-negative; Unpaid is not settable (no quota).
- [ ] Editing a quota is reflected immediately in the employee's remaining-balance calculation.
- [ ] Quota management is HR-only.

### S3-4 — Playwright UI test setup & HR admin coverage

*As the team, I want automated browser-based UI tests so that critical screens are verified end-to-end, not just at the unit/integration level.*

**Technical context:** Introduce Playwright (first sprint with meaningful UI to exercise). Set up a Playwright test project that runs against the app served in Docker (via the existing `docker-compose.yml`/CI Postgres service), wire it into `.github/workflows/ci.yml` as a job that builds/starts the app and runs the Playwright suite headless. This suite grows in every subsequent sprint (S4-4, S5-4, S6-4, S7-4) rather than being re-set-up each time.

**Acceptance criteria:**
- [ ] Playwright project set up (browsers installed in CI, config points at the Dockerized app + Postgres).
- [ ] E2E test: HR logs in, creates an employee, assigns a manager, and sets an Annual/Medical quota — asserting the UI reflects each step.
- [ ] E2E test: HR cannot be reached by a non-HR role (negative UI test — redirected/forbidden).
- [ ] Playwright suite runs in CI on every PR and blocks merge on failure.

**Definition of Done:**
- [ ] HR can, end to end, create an employee, assign a manager, and set that employee's Annual and Medical quotas.
- [ ] Playwright is set up and its HR-admin suite passes in CI.

---

## Sprint 4 — Leave Application & Balance Engine (Days 8–10.5) [NOT STARTED]

**Goal:** Employees can submit leave requests with correct working-day/half-day counting and balance reservation. This is the core value and holds the trickiest logic — it gets a dedicated sprint.

**Dependencies:** Sprint 3

### S4-1 — Working-day & half-day calculation

*As an employee, I want only working days counted so that weekends aren't deducted from my leave.*

**Technical context:** A single, unit-tested utility computing chargeable days between start and end dates: count Mon–Fri only; a half-day flag on the start and/or end date subtracts 0.5 each. No public-holiday calendar in v1. Centralize here so submission, approval, and balance logic all agree.

**Acceptance criteria:**
- [ ] Utility returns correct counts for: single full day, multi-day span including a weekend, a Fri→Mon span, and half-day on start/end.
- [ ] A range consisting only of weekend days yields 0 chargeable days and is rejected upstream as invalid.
- [ ] Unit tests cover all above boundary cases and pass.

### S4-2 — Balance calculation service

*As the system, I want an authoritative remaining-balance calculation so that employees can't exceed their entitlement.*

**Technical context:** Remaining = allocated quota − (Approved + Pending chargeable days) for that employee × type × year. Pending requests **reserve** balance. Unpaid is uncapped and never reduces a balance. All mutations run inside DB transactions.

**Acceptance criteria:**
- [ ] Remaining balance per type computed correctly given a mix of Approved and Pending requests.
- [ ] Pending requests reduce remaining balance (reservation); Unpaid never affects any balance.
- [ ] Service reads and computes within a transaction to avoid stale reads.
- [ ] Unit/integration tests verify balance math across states.

### S4-3 — Leave request submission

*As an employee, I want to submit a leave request so that my manager can review it.*

**Technical context:** Form captures type, start/end dates, start/end half-day flags, optional reason. Validations: end ≥ start; dates not in the past; **no cross-calendar-year spans**; sufficient remaining balance for Annual/Medical (Unpaid exempt). On submit → `Pending` and balance reserved.

**Acceptance criteria:**
- [ ] Employee can submit a valid request; it appears as `Pending` and reserves the correct chargeable days.
- [ ] Submission is rejected with clear messages for: end before start, past dates, cross-year span, and insufficient balance (Annual/Medical).
- [ ] Unpaid requests submit regardless of balance and reserve no balance.
- [ ] Users of any role (Employee/Manager/HR) can submit their own requests.

### S4-4 — Playwright coverage: leave application

*As the team, I want browser-based coverage of the leave application flow so that regressions in the trickiest logic (working-day/balance math) surface at the UI level too.*

**Acceptance criteria:**
- [ ] E2E test: employee submits an Annual request spanning a weekend and sees the correct chargeable-day count and reserved balance reflected in the UI.
- [ ] E2E test: employee submission is blocked in the UI with a clear message for insufficient balance and for a cross-year span.
- [ ] Playwright suite (cumulative with Sprint 3's) passes in CI.

**Definition of Done:**
- [ ] An employee submits Annual, Medical, and Unpaid requests; day counts and balance reservations are correct; invalid inputs are blocked.
- [ ] Playwright leave-application suite passes in CI.

---

## Sprint 5 — Approval Workflow (Days 10.5–13) [NOT STARTED]

**Goal:** Managers approve/reject their reports' requests with correct balance transitions; manager-less requests fall back to HR.

**Dependencies:** Sprint 4

### S5-1 — Manager approval queue

*As a manager, I want to see pending requests from my reports so that I can act on them.*

**Acceptance criteria:**
- [ ] Manager sees a list of `Pending` requests only from employees assigned to them.
- [ ] Each row shows requester, leave type, dates, computed chargeable days, and requester's remaining balance for that type.
- [ ] A manager cannot see or act on requests from employees not assigned to them (verified by negative test).

### S5-2 — Approve / reject decisions

*As a manager, I want to approve or reject a request so that the employee gets a clear decision and balances stay correct.*

**Technical context:** Approve → `Approved` (reserved balance becomes a confirmed deduction; no further balance change). Reject → `Rejected` with optional note (reserved balance released). Re-validate remaining balance at approval time to guard against overspend from concurrent changes. All transitions transactional; only valid transitions from `Pending` allowed.

**Acceptance criteria:**
- [ ] Approve moves request to `Approved`; the requester's remaining balance reflects a confirmed deduction.
- [ ] Reject moves request to `Rejected`, records the optional note, and releases the reserved balance.
- [ ] Approval re-checks balance and fails safely if the balance is no longer sufficient.
- [ ] Only `Pending` requests can be approved/rejected; terminal requests cannot be re-decided.
- [ ] Decision metadata (decider, timestamp, note) is persisted.

### S5-3 — Manager-less approval fallback (HR)

*As HR, I want to approve requests from users who have no assigned manager so that everyone can get a decision.*

**Acceptance criteria:**
- [ ] Requests from users with no assigned manager surface to HR for approval/rejection.
- [ ] HR can approve/reject these with the same balance rules as a manager.
- [ ] A user with an assigned manager does NOT appear in the HR fallback queue.

### S5-4 — Playwright coverage: approval workflow

*As the team, I want browser-based coverage of the manager/HR approval flow so that the apply→approve/reject cycle is verified end-to-end in the UI.*

**Acceptance criteria:**
- [ ] E2E test: manager logs in, sees a pending request from a report, approves it, and the requester's balance/history reflects the decision.
- [ ] E2E test: manager rejects a request with a note; requester sees the rejection and released balance.
- [ ] E2E test: a manager-less user's request surfaces in the HR fallback queue and HR can decide it.
- [ ] Playwright suite (cumulative) passes in CI.

**Definition of Done:**
- [ ] Full apply→approve and apply→reject cycles work with correct balance effects; manager-less requests are handled by HR.
- [ ] Playwright approval-workflow suite passes in CI.

---

## Sprint 6 — Self-Service: Balances, History, Cancel & Withdraw (Days 13–15.5) [NOT STARTED]

**Goal:** Employees can see their balances and history and can cancel pending or withdraw approved future-dated requests with correct balance restoration.

**Dependencies:** Sprint 5

### S6-1 — Balance & history views

*As an employee, I want to see my balances and request history so that I know where I stand.*

**Acceptance criteria:**
- [ ] Employee sees current remaining balance for each balance-backed type (Annual, Medical).
- [ ] Employee sees a history of their own requests with state, type, dates, and chargeable days.
- [ ] History reflects all states (Pending, Approved, Rejected, Cancelled, Withdrawn).
- [ ] An employee sees only their own balances and history.

### S6-2 — Cancel a pending request

*As an employee, I want to cancel a pending request so that I can correct mistakes.*

**Acceptance criteria:**
- [ ] Employee can cancel their own `Pending` request → state `Cancelled`; reserved balance is released.
- [ ] Only `Pending` requests can be cancelled; other states cannot.
- [ ] An employee cannot cancel another user's request (server-side enforced).

### S6-3 — Withdraw an approved future-dated request

*As an employee, I want to withdraw an approved future request so that my balance is restored when plans change.*

**Technical context:** Withdrawal allowed only when the approved leave's dates are entirely in the future. On withdraw → `Withdrawn` and the confirmed deduction is restored to the balance. Requests whose leave has started or passed cannot be withdrawn in v1.

**Acceptance criteria:**
- [ ] Employee can withdraw their own `Approved` request whose dates are entirely in the future → state `Withdrawn`; balance restored.
- [ ] Withdrawal is blocked for approved requests that have started or are in the past.
- [ ] An employee cannot withdraw another user's request.

### S6-4 — Playwright coverage: self-service

*As the team, I want browser-based coverage of balances/history/cancel/withdraw so that balance-restoration logic is verified end-to-end in the UI.*

**Acceptance criteria:**
- [ ] E2E test: employee views balances/history, cancels a pending request, and sees the balance restored in the UI.
- [ ] E2E test: employee withdraws a future-dated approved request and sees the balance restored; withdrawal is blocked in the UI for a past/started approved request.
- [ ] Playwright suite (cumulative) passes in CI.

**Definition of Done:**
- [ ] Balances/history render correctly; cancel and withdraw both adjust balances correctly and enforce their state/date rules.
- [ ] Playwright self-service suite passes in CI.

---

## Sprint 7 — HR Oversight, State-Machine & Concurrency Hardening (Days 15.5–18) [NOT STARTED]

**Goal:** Close correctness gaps — HR company-wide visibility (P1), strict enforcement of the request state machine, and balance concurrency safety — plus responsive/accessibility polish.

**Dependencies:** Sprint 6

### S7-1 — HR company-wide leave records & balances (P1)

*As HR, I want to view leave records and balances across all employees so that I can audit and answer questions.*

**Acceptance criteria:**
- [ ] HR can view all employees' remaining balances per type.
- [ ] HR can view all requests across the company with state, type, dates, and day count, filterable by employee and/or state.
- [ ] View is HR-only.

### S7-2 — State-machine & concurrency hardening

*As the business, I want balances to stay correct under all transitions and concurrent actions so that records are trustworthy (0 discrepancies goal).*

**Technical context:** Enforce the PRD state model centrally: `Pending`→{Approved,Rejected,Cancelled}; `Approved`→{Withdrawn (future only)}; Rejected/Cancelled/Withdrawn terminal. Wrap all balance mutations in transactions; re-validate at approval; prevent double-spend under concurrent submit/approve.

**Acceptance criteria:**
- [ ] Invalid transitions are rejected server-side (e.g., approving a Cancelled request, cancelling an Approved one).
- [ ] Concurrent submissions cannot drive a balance negative (test simulates two overlapping requests near the quota limit).
- [ ] A manual audit of balances after a scripted sequence of apply/approve/reject/cancel/withdraw shows **0 discrepancies**.
- [ ] Regression tests cover the full transition matrix and balance effects.

### S7-3 — Responsive UI & accessibility polish

*As a mobile user, I want the app to work well on my phone browser so that I can request leave on the go.*

**Acceptance criteria:**
- [ ] Key screens (login, apply, approval queue, balances/history, HR admin) are usable on a small/mobile viewport.
- [ ] Forms have proper labels and are keyboard-operable.
- [ ] No horizontal-scroll/overflow breakage on common mobile widths.

### S7-4 — Playwright coverage: HR oversight & responsive checks

*As the team, I want browser-based coverage of HR's company-wide view and a mobile-viewport smoke pass so that oversight and responsive polish are verified end-to-end.*

**Acceptance criteria:**
- [ ] E2E test: HR views company-wide balances/requests and filters by employee and/or state.
- [ ] E2E test (mobile viewport): login and leave-application flow render and function correctly on a small viewport (no overflow/broken layout).
- [ ] Playwright suite (cumulative) passes in CI.

**Definition of Done:**
- [ ] HR oversight works; state machine and concurrency are provably safe (tests green, audit clean); UI is responsive and accessible.
- [ ] Playwright HR-oversight/responsive suite passes in CI.

---

## Sprint 8 — Hardening, Security Review, Testing & Launch (Days 18–20.5) [NOT STARTED]

**Goal:** Ship a tested, secure, deployable MVP.

**Dependencies:** Sprint 7

### S8-1 — End-to-end & integration testing

*As the team, I want automated coverage of critical flows so that we can ship with confidence.*

**Acceptance criteria:**
- [ ] Automated tests cover: apply→approve→balance-deducted; apply→reject→balance-released; cancel; withdraw-restores; HR create-employee→assign-manager→set-quota.
- [ ] Authorization negative tests: each role is blocked from others' privileged actions and data.
- [ ] The cumulative Playwright suite (Sprints 3–7) runs green in CI alongside unit/integration tests — no flaky or skipped E2E specs.
- [ ] Test suite (unit + integration + Playwright) runs green in one command and is documented in the README.

### S8-2 — Security review & hardening

*As the business, I want the app to meet the PRD's security bar so that leave data is protected.*

**Acceptance criteria:**
- [ ] Every state-changing action enforces server-side authorization (no reliance on hidden UI) — spot-checked across roles.
- [ ] CSRF protection active on all forms; app served over HTTPS; secure session/cookie settings verified.
- [ ] Passwords confirmed hashed; no secrets committed to the repo.
- [ ] Balance mutations confirmed transactional (from Sprint 7) — no unguarded write paths remain.

### S8-3 — Deployment readiness & runbook

*As an operator, I want to deploy and run the app reliably so that staff can start using it.*

**Acceptance criteria:**
- [ ] Migrations apply on deploy; a documented step seeds the initial production HR account securely.
- [ ] Deployment configuration (connection string, HTTPS, environment settings) documented in a runbook/README.
- [ ] A post-deploy smoke test passes: log in as HR, create an employee, that employee applies for leave, manager/HR approves, balance updates.

**Definition of Done:**
- [ ] MVP is tested, security-reviewed, deployable, and passes the smoke test — ready for launch.

---

## Summary Table

| Sprint | Days | Focus | Key Output | Status |
|--------|------|-------|------------|--------|
| 1 | 1–2.5 | Foundation & data model | Running app + PostgreSQL + schema/migrations + seeded HR | ✅ |
| 2 | 2.5–5 | Auth & authorization | Login/logout, roles enforced, HR password reset | ✅ |
| 2.5 | 5–5.5 | Non-blocking cleanup | Sprint 2 review follow-ups: dead-code fix, CSRF/manager-landing/no-user-selected tests, HomeController.Error + login-redirect fixes | 🟨 |
| 3 | 5.5–8 | HR administration | Employee CRUD, manager assignment, annual quotas | ⬜ |
| 4 | 8–10.5 | Leave application & balance engine | Working-day calc, balance reservation, request submission | ⬜ |
| 5 | 10.5–13 | Approval workflow | Manager queue, approve/reject, HR fallback | ⬜ |
| 6 | 13–15.5 | Self-service | Balances/history, cancel, withdraw-with-restore | ⬜ |
| 7 | 15.5–18 | Oversight & hardening | HR company view, state-machine/concurrency safety, responsive polish | ⬜ |
| 8 | 18–20.5 | Testing, security & launch | E2E tests, security review, deploy runbook, smoke test | ⬜ |

## Critical dependency path

`S1 (foundation) → S2 (auth) → S2.5 (non-blocking cleanup) → S3 (HR setup: users, managers, quotas) → S4 (apply + balance) → S5 (approve) → S6 (cancel/withdraw) → S7 (hardening) → S8 (launch)`

HR administration (S3) is intentionally **before** leave application (S4): you cannot apply for leave without a user, an assigned manager, and a quota to draw against.

## Deferred to v2

| Feature | Rationale |
|---------|-----------|
| Notifications (email/in-app/push) | Explicitly out of PRD scope; workflow functions without them for a small team |
| Leave accrual, carry-over, pro-rating | PRD uses fixed HR-set annual quotas for v1; accrual adds significant logic |
| Public-holiday calendar & regional work-week rules | Working-day calc is Mon–Fri only in v1; holidays add config + maintenance |
| Third-party integrations (payroll, calendar, HRIS, SSO) | Out of scope; no external dependencies in MVP |
| Native mobile apps | Responsive web browser covers the mobile requirement |
| Advanced reporting/analytics & dashboards | HR company-wide view (S7-1) covers basic oversight; analytics is post-MVP |
| Document/attachment uploads (e.g., medical certificates) | Not required for MVP approval flow |
| Multi-level / delegated approval chains | Single assigned manager (+ HR fallback) is sufficient for a small company |
| Withdrawal of in-progress/past approved leave | v1 restricts withdrawal to fully-future approved requests |

## Non-blocking Review Backlog

This section collects non-blocking feedback from sprint reviews. Questions need human answers (edit inline). Improvements accumulate until triaged into a sprint.

### Questions
Items needing human judgment. Answer inline by replacing the `_awaiting answer_` text, then check the box.

_None yet._

### Improvements
Concrete items with clear implementation direction. Will be triaged into a cleanup sprint periodically.

- [x] **(Sprint 1)** Parameterize/namespace the `leave-postgres` container name in `docker-compose.yml` — it's currently hardcoded, so a second checkout of the repo (e.g., via `git worktree`, used for parallel branch work) will fail `docker compose up -d db` with a container-name collision. Derive the name from the project/directory or an env var. — _Triaged into Sprint 2.5 — Non-blocking Cleanup._
- [x] **(Sprint 1)** Add test coverage for the `InvalidOperationException` throw paths in `DataSeeder.EnsureRolesAsync`/`EnsureUserAsync`/`EnsurePolicyAsync` (triggered when an ASP.NET Core Identity operation fails) — currently untested; low risk under normal seeded data but worth covering. — _Triaged into Sprint 2.5 — Non-blocking Cleanup._
- [ ] **(Sprint 1)** Carry the `Seed:HrPassword` production-override requirement into Sprint 8's security review (S8-2) as an explicit checklist item — `appsettings.json` ships a real default (`ChangeMe123!`) that would create a publicly-known-password HR account if a production deploy forgot to override `Seed__HrPassword`. Currently this is only called out in the README; make sure S8-2 verifies the override actually happens before launch. *(Earmarked for Sprint 8 S8-2, not immediate cleanup.)*
- [x] **(Sprint 2)** `ResetPasswordViewModel.UserId`'s `[Required]` attribute is dead code on a non-nullable `Guid` — never fails validation; empty-selection submission instead fails at model-binding with a generic error, not the intended "Select a user." message. Fix by making it nullable or handling empty selection explicitly. — _Triaged into Sprint 2.5 — Non-blocking Cleanup._
- [x] **(Sprint 2)** No test verifies CSRF-token rejection (missing/invalid `__RequestVerificationToken`) on `/Account/Login`, `/Account/Logout`, or `/Hr/ResetPassword` — a regression (e.g. accidental `[IgnoreAntiforgeryToken]`) would go undetected. — _Triaged into Sprint 2.5 — Non-blocking Cleanup._
- [x] **(Sprint 2)** No test covers the Manager-role landing-page branch (`Home/Index.cshtml`, `else if (User.IsInRole(Roles.Manager))`) or the `HrController.ResetPassword` "no user selected" (empty GUID) submission path. — _Triaged into Sprint 2.5 — Non-blocking Cleanup._
- [x] **(Sprint 2)** `HomeController.Error` inherits the class-level `[Authorize]` added this sprint — an unhandled exception on an anonymous request (e.g. inside `AccountController` pre-sign-in) redirects to login instead of showing the friendly error page, masking the real failure. Also, `GET /Account/Login` doesn't redirect an already-authenticated user back to home. — _Triaged into Sprint 2.5 — Non-blocking Cleanup._
- [ ] **(Sprint 2)** No brute-force/lockout protection on login — `AccountController.Login` calls `PasswordSignInAsync(..., lockoutOnFailure: false)`, so failed attempts never increment `AccessFailedCount` and Identity's default lockout never engages, allowing unlimited password guesses against a known email. Low severity; reviewer explicitly flagged this for Sprint 8's security review (S8-2), not immediate action. *(Earmarked for Sprint 8 S8-2.)*
- [ ] **(Sprint 2)** No explicit cookie hardening (`CookieSecurePolicy.Always`, explicit `HttpOnly`) in `ConfigureApplicationCookie` — ASP.NET Core's defaults are already reasonable, but reviewer suggested making it explicit. Reviewer flagged this for Sprint 8's security review (S8-2). *(Earmarked for Sprint 8 S8-2.)*
