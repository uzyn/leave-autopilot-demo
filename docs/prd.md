# Employee Leave Management System — Product Requirements Document

*Status: Draft v1 · Owner: Product · Target: MVP launch in 4 weeks*

## 1. Overview

A web-based leave management system for a small company (under 50 employees) that lets employees apply for leave, managers approve or reject requests, and HR administer accounts and per-employee leave entitlements. It replaces ad-hoc, manual leave tracking (email/spreadsheets) with a single source of truth for balances and history. This document defines the **MVP only** — features listed as out of scope are deliberately excluded from v1.

## 2. Problem Statement

Today the company tracks leave manually — through email requests, chat messages, and spreadsheets. This creates several problems:

- **No single source of truth.** Balances live in scattered spreadsheets and are easy to get wrong.
- **Opaque approvals.** Employees don't know the status of a request; managers lose track of what's pending.
- **Error-prone accounting.** Manually decrementing balances leads to disputes and mistakes.
- **No history.** Neither employees nor HR can easily see who took what leave and when.

Without a system, the problem grows with headcount: more disputes, more HR time spent reconciling, and reduced trust in leave records. The impact is wasted administrative time and employee frustration.

## 3. Goals and Success Metrics

| Goal | Metric | Target |
|------|--------|--------|
| Digitize the leave workflow | Share of leave requests submitted through the system (vs. email/spreadsheet) | 100% within 2 weeks of launch |
| Make balances trustworthy | Discrepancies between system balance and a manual audit | 0 discrepancies |
| Make requests easy | Time for an employee to submit a valid request unaided | < 2 minutes |
| Make approvals timely | Median time from request submission to manager decision | Visible in-app; baseline established, trending down |
| Make HR administration efficient | Time for HR to create an employee and set their policy | < 5 minutes per employee |
| Ship on time | MVP live and usable by all staff | Within 4 weeks |

## 4. User Personas

- **Employee (all staff, including managers & HR)**
  - **Needs:** Apply for leave quickly, see remaining balance before applying, track request status, view leave history.
  - **Context:** Uses the system occasionally (a few times a month), often from a phone browser at short notice.

- **Manager**
  - **Needs:** See leave requests from their assigned reports, approve or reject with confidence, see the requester's remaining balance and dates.
  - **Context:** Reviews requests reactively; also an employee who applies for their own leave (routed to *their* manager).

- **HR administrator**
  - **Needs:** Create and manage employee accounts, assign each employee a manager, set per-employee annual quotas per leave type, and view leave records across the company.
  - **Context:** Sets up the company once, then makes occasional changes (new hires, quota adjustments).

## 5. User Stories

Prioritized: **P0** must-have for MVP, **P1** should-have if time permits, **P2** nice-to-have (likely post-MVP).

**Authentication**
- (P0) As a user, I want to log in with my email and password so that only authorized people access the system.
- (P0) As a user, I want to log out so that my session is secure on shared devices.

**Employee — applying & tracking**
- (P0) As an employee, I want to see my remaining balance per leave type so that I know what I can request.
- (P0) As an employee, I want to submit a leave request (type, start date, end date, optional half-day, reason) so that my manager can review it.
- (P0) As an employee, I want the system to count only working days so that weekends aren't deducted.
- (P0) As an employee, I want to view the status and history of my requests so that I know where each one stands.
- (P0) As an employee, I want to cancel a pending request so that I can correct mistakes.
- (P1) As an employee, I want to withdraw an approved, future-dated request so that my balance is restored if plans change.
- (P0) As an employee, I want to be prevented from requesting more balance-backed leave than I have so that I don't go negative.

**Manager — approvals**
- (P0) As a manager, I want to see pending requests from my assigned reports so that I can act on them.
- (P0) As a manager, I want to approve or reject a request (with an optional note on rejection) so that the employee gets a clear decision.
- (P0) As a manager, I want to see the requester's dates and remaining balance while deciding so that I can decide with context.

**HR — administration**
- (P0) As HR, I want to create, edit, and deactivate employee accounts so that only current staff have access.
- (P0) As HR, I want to assign each employee a manager so that requests route correctly.
- (P0) As HR, I want to set each employee's annual quota per leave type so that entitlements reflect their contract.
- (P1) As HR, I want to view leave records across all employees so that I can answer questions and audit balances.

## 6. Functional Requirements

### 6.1 Authentication & Authorization
- FR-1: The system shall authenticate users via email and password (ASP.NET Core Identity), with passwords stored using a secure one-way hash.
- FR-2: The system shall support three roles — **Employee**, **Manager**, **HR** — and enforce role-based authorization on every page and action.
- FR-3: Every user is also an employee: users with the Manager or HR role have their own leave balances and may submit their own requests, which route to *their* assigned manager.
- FR-4: The system shall provide login and logout, and maintain an authenticated session.

### 6.2 Leave Types & Balances
- FR-5: The system shall support three leave types: **Annual**, **Medical/Sick**, and **Unpaid**.
- FR-6: HR shall set a fixed **annual quota** per employee for **Annual** and **Medical/Sick** leave, effective for the current calendar year.
- FR-7: **Unpaid** leave has no quota (uncapped) and does not decrement any balance, but still follows the approval workflow.
- FR-8: There is **no carry-over** and **no accrual** in v1 — quotas are fixed values set by HR.
- FR-9: An employee's remaining balance = assigned quota − (approved + pending) balance-backed days for that type in the period. Pending requests shall reserve balance so an employee cannot double-book beyond their quota.

### 6.3 Leave Application
- FR-10: An employee shall submit a request specifying leave type, start date, end date, half-day option (for the start and/or end date), and an optional reason.
- FR-11: The system shall compute the number of leave days as **working days only** (Monday–Friday), excluding weekends; a half-day counts as 0.5. *(Public holidays are out of scope for v1 — see §9.)*
- FR-12: The system shall reject a request that would exceed the employee's remaining balance for a balance-backed type (Annual, Medical/Sick). Unpaid is exempt.
- FR-13: The system shall validate that the end date is on or after the start date and that dates are not in the past.
- FR-14: A submitted request enters the **Pending** state and reserves the corresponding balance.

### 6.4 Approval Workflow
- FR-15: A request routes to the requester's single **assigned manager**.
- FR-16: The assigned manager shall be able to **Approve** or **Reject** a pending request; rejection allows an optional note.
- FR-17: On **Approve**, the request becomes **Approved** and the reserved balance is confirmed as deducted (no change to available balance, which already reflected the reservation).
- FR-18: On **Reject**, the request becomes **Rejected** and any reserved balance is released.
- FR-19: A manager shall see, for each pending request, the requester's name, dates, computed day count, leave type, and remaining balance for that type.
- FR-20: Only the assigned manager (and, if enabled, HR — see §11) may decide on a given request.

### 6.5 Cancellation & Withdrawal
- FR-21: An employee shall be able to **cancel** their own **Pending** request; this releases reserved balance and sets the state to **Cancelled**.
- FR-22: An employee shall be able to **withdraw** their own **Approved** request whose leave dates are entirely in the future; this **restores** the deducted balance and sets the state to **Withdrawn**.
- FR-23: Approved requests whose leave has started or passed cannot be withdrawn in v1.

### 6.6 Balances & History Views
- FR-24: An employee shall view their current remaining balance for each leave type.
- FR-25: An employee shall view a history of their own requests with state (Pending, Approved, Rejected, Cancelled, Withdrawn), dates, day count, and type.
- FR-26: (P1) HR shall view leave records and balances across all employees.

### 6.7 HR Administration
- FR-27: HR shall create, edit, and deactivate employee accounts (name, email, role).
- FR-28: HR shall assign exactly one manager to each employee.
- FR-29: HR shall set and edit each employee's annual quota per balance-backed leave type.
- FR-30: Deactivated accounts shall not be able to log in and shall not appear as assignable managers.

### 6.8 Request State Model
Valid states and transitions:
- `Pending` → `Approved` (manager) · `Rejected` (manager) · `Cancelled` (employee)
- `Approved` → `Withdrawn` (employee, future-dated only)
- `Rejected`, `Cancelled`, `Withdrawn` are terminal.
Balance effects: reserve on `Pending`; release on `Rejected`/`Cancelled`; keep reserved-then-deducted on `Approved`; restore on `Withdrawn`.

## 7. Non-Functional Requirements

- **Performance:** For the expected load (< 50 users, low concurrency), common page loads should complete in under ~2 seconds on typical broadband, and standard actions (submit, approve) in under ~1 second.
- **Compatibility:** Must work on current versions of major desktop and mobile browsers (Chrome, Safari, Edge, Firefox). Responsive layout — no native mobile app.
- **Security:** Passwords hashed via ASP.NET Core Identity; all traffic over HTTPS; CSRF protection on state-changing forms (built into Razor/MVC); server-side authorization checks on every action (never trust client-side role hints); balance mutations executed within database transactions to prevent race conditions and double-spend.
- **Data integrity:** Balance reservation/deduction/restoration must be atomic and consistent with the request state at all times.
- **Availability:** Single-server deployment is acceptable for MVP; targeted for use during business hours.
- **Auditability:** Each request retains its creation time, decision time, decider, and current state for basic traceability.
- **Accessibility:** Forms use proper labels and are keyboard-operable; layout usable on small screens.

## 8. Technical Considerations

Greenfield project — no existing code beyond `docs/` (see `docs/idea.md`).

- **Framework:** ASP.NET Core (MVC / Razor Pages), server-rendered for simplicity and mobile-browser friendliness.
- **Language:** C# (.NET).
- **Database:** PostgreSQL, accessed via Entity Framework Core (Npgsql provider); EF Core migrations for schema management.
- **Auth:** ASP.NET Core Identity for email/password auth and role management.
- **Core domain entities (indicative):** `User`/Employee (with role, manager reference, active flag), `LeavePolicy`/quota (employee × leave type × year × days), `LeaveRequest` (requester, type, start/end, half-day flags, computed days, state, decision metadata), and a derived or computed `Balance` per employee × type × year.
- **Working-day calculation:** Server-side utility counting Mon–Fri between dates, applying half-day adjustments; no public-holiday calendar in v1.
- **Concurrency:** Balance checks and state transitions wrapped in transactions; validate remaining balance at approval time as well as submission time to avoid stale-read overspend.
- **Deployment:** Single web app + PostgreSQL instance is sufficient for < 50 users.

## 9. Scope and Milestones

### In Scope (v1 / MVP)
- Web-based, responsive UI (desktop + mobile browsers).
- Email/password authentication with Employee / Manager / HR roles.
- Leave application with type, dates, half-day, working-day counting.
- Manager approval/rejection workflow (single assigned manager per employee).
- Cancellation of pending requests; withdrawal of approved future-dated requests with balance restoration.
- Employee balance and history views.
- HR administration: create/manage accounts, assign managers, set per-employee annual quotas per leave type.
- Three leave types: Annual, Medical/Sick, Unpaid.

### Out of Scope (future consideration)
- Any form of notifications (email, in-app, push).
- A separate admin panel beyond the HR screens described here.
- Integrations with third-party systems (payroll, calendar, HRIS, SSO).
- Native mobile apps.
- Advanced reporting, dashboards, and analytics.
- Leave accrual, carry-over/rollover, and pro-rating.
- Public-holiday calendars and region-specific working-week rules.
- Document/attachment uploads (e.g., medical certificates).
- Multi-level or delegated approval chains.

### Milestones (4-week MVP)
| Milestone | Description | Key Deliverables |
|-----------|-------------|------------------|
| M1 — Foundation (Week 1) | Project scaffolding, auth, data model | ASP.NET Core app + PostgreSQL wired up; Identity login/logout; roles; entity schema + migrations |
| M2 — Core workflow (Week 2) | Apply → approve/reject | Leave request form with working-day calc & validation; manager approval queue and decisions; balance reservation/deduction |
| M3 — Balances, history, HR admin (Week 3) | Self-service + administration | Balance & history views; cancel/withdraw with balance restore; HR account management, manager assignment, quota setup |
| M4 — Hardening & launch (Week 4) | Polish, test, deploy | End-to-end testing, edge-case validation, security checks, responsive polish, seed data, deployment |

## 10. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Balance race conditions (concurrent requests/approvals overspend quota) | Medium | High | Transactional balance updates; re-validate balance at approval time; unique/consistency constraints |
| Working-day / half-day logic errors cause wrong deductions | Medium | High | Centralize the calculation in one tested utility; unit tests for boundary cases (weekend spans, half-days) |
| 4-week timeline slips due to scope creep | Medium | High | Enforce the Out-of-Scope list strictly; defer any P1/P2 that threatens P0 delivery |
| Top-of-org users have no assigned manager (can't get approvals) | Medium | Medium | Resolve in §11 before build (e.g., HR approves manager-less users or self-approval for HR) |
| Withdrawal/restore edge cases (year boundary, partial-past ranges) | Low | Medium | Restrict withdrawal to fully-future approved requests in v1; document year-boundary handling |
| Security gaps (authz bypass, weak session handling) | Low | High | Rely on ASP.NET Core Identity defaults; server-side authorization on every action; HTTPS; security review in M4 |

## 11. Open Questions

1. **Manager-less users:** Who approves leave for the top of the org (e.g., the HR admin or owner) when no manager is assigned? Options: HR approves manager-less users, or such users self-approve. *Needs a decision before M2.*
2. **Year boundary:** When does the annual quota reset, and how are requests that span 31 Dec → 1 Jan counted? Assumed calendar-year quotas with no carry-over; cross-year requests may be disallowed or split. *Confirm rule.*
3. **Password reset:** With notifications out of scope, how do users recover a forgotten password? Likely HR-assisted reset in v1. *Confirm.*
4. **Overlapping requests within a team:** Should managers see calendar conflicts among their reports? Assumed no team calendar in v1.
5. **Medical/Sick evidence:** No attachments in v1 — confirm HR is comfortable approving sick leave without uploaded certificates for the MVP.
6. **Seed/first admin:** How is the initial HR account created (seed script vs. manual)? *Decide in M1.*
