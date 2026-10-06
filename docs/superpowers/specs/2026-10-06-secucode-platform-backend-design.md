# SecuCode Platform — Sub-project 0: Backend API + Auth

**Date:** 2026-10-06
**Status:** Draft for review

## 1. Goal and context

SecuCode is being turned from a single WPF app that talks directly to MySQL into a **client-server platform** that can be demoed to colleges as a full exam-management system:

- one **backend API** owns all data, authentication, permissions, submissions, evidence and management;
- a **Windows app (WPF)** and a **web app (React)** are both thin clients of that API;
- the same accounts work in both apps.

**Success for the overall program:** an impressive, smooth demo covering administration, an Indian-college staff hierarchy, scheduling, proctored coding exams (strict desktop mode and web mode), grading, and analytics, runnable on one laptop and deployable online later with Docker.

**Success for this sub-project:** the API reaches parity with today's features; adds auth with **configurable roles and capabilities**, multi-role staff and scoped permissions; moves exam timing and submission to the server; supports audited staff corrections; stores evidence centrally; and runs student code in sandboxed containers — all covered by integration tests. No new UI is built here.

### Decisions already made (with the user)

| Decision | Choice |
|---|---|
| Architecture | API server; WPF and web are API clients only (no direct DB access) |
| Stack | **ASP.NET Core 8 API** (C#) + **React + TypeScript** web app; WPF stays for desktop |
| Hosting | Local first, packaged with **Docker Compose** so it can go online later |
| Web exams | **Per-exam mode**: `DesktopOnly` (strict proctoring) or `WebAllowed` |
| Roles | **Configurable roles** seeded with an Indian-college hierarchy (Super Admin, Principal, Exam Controller, HOD, Sub-HOD, Class Coordinator, Faculty, Student); admins can edit them and add custom roles |
| Capabilities | A fixed list in code; **which roles get which capabilities is editable data** |
| Multiple roles | A person can hold **several roles at once**, each with its own scope and optional dates (e.g. Faculty + HOD in-charge) |
| Staff corrections | **Reset/reopen attempts**, **extend time**, **edit questions after attempts**, each behind a toggle |
| Toggles | **Global switches + per-exam overrides** (an exam can only turn a switch off) |
| Accountability | Every staff change is **recorded in an audit log** |

## 2. Program roadmap (each item gets its own spec → plan → build)

| # | Sub-project | Summary |
|---|---|---|
| **0** | **Backend API + auth** | *This document.* |
| 1 | Windows app on the API + new UI foundation | Fluent-style theme, sidebar shell, page transitions, light/dark; proctoring kept local, all data via API |
| 2 | Web app foundation | React + Vite + TypeScript, design system matching desktop, auth, capability-based navigation, animations |
| 3 | Admin & masters | Users (incl. CSV bulk import), role/capability grid, role assignments, branches, classes, batches, subjects, rooms |
| 4 | Scheduling & timetable | Timeline/calendar of all exams, filters (staff, department, class, subject, room), clash detection, rescheduling |
| 5 | Staff tools | Question bank, grading with test cases, evidence viewer, corrections and audit screens, results publishing |
| 6 | Student experience | Dashboard with countdowns, results/history, notifications |
| 7 | Analytics & demo data | Charts (results, malpractice trends), live exam monitor, realistic demo-college seed |

Sub-projects 3–7 extend the API through new EF Core migrations; nothing in this design blocks them.

## 3. Architecture

```
  Windows app (WPF)            Web app (React)
  proctoring, lockdown         admin / staff / student
          │   HTTPS + JSON (REST)  ·  SignalR (live)  │
          └──────────────────┬───────────────────────┘
                     Secucode.Api (ASP.NET Core 8)
                     auth · roles & capabilities · exams ·
                     attempts · corrections · audit ·
                     evidence storage · live hub      ──► Code runner (Docker container per run)
                             │
                           MySQL 8
```

### Repository layout

```
server/Secucode.Api            API, EF Core (Pomelo MySQL provider), auth, permissions, SignalR hub, background jobs
server/Secucode.Contracts      request/response DTOs shared with the WPF client
server/Secucode.Api.Tests      xUnit integration + runner tests
clients/Secucode.Desktop       current WPF app, moved here (rebuilt on the API in sub-project 1)
clients/web                    React app (created in sub-project 2)
docker-compose.yml             mysql + api (+ web later); language runner images
```

During sub-project 0 the existing WPF app is moved to `clients/Secucode.Desktop` unchanged and keeps working against its own DB until sub-project 1 switches it to the API. `Database/setup.sql` and `add-user.ps1` are retired at the end of sub-project 1.

### Cross-cutting choices

- **Persistence:** EF Core code-first with migrations; MySQL via Pomelo. Migrations replace `setup.sql`.
- **Seeding:** `dotnet run --project server/Secucode.Api -- seed` creates the default roles and capability grants (section 5) and a minimal dataset: one Super Admin, one Computer-dept HOD who is also Faculty, one Faculty, two students, one department/class/batch, one exam open now. The rich demo college is sub-project 7.
- **API description:** OpenAPI (Swashbuckle) at `/swagger`; the web client is generated from it in sub-project 2.
- **Configuration:** `appsettings.json` + environment variables (connection string, JWT signing key, runner mode, evidence path). No secrets in source; dev defaults in `appsettings.Development.json`.
- **Time:** all timestamps stored and transmitted in **UTC**; clients convert for display. Server clock is authoritative for exam timing.
- **Terminology:** a *branch* is a **department** (e.g. Computer Engineering); a *class* is a year (e.g. TE); a *batch* is a group within it (e.g. A1). The existing tables keep their names.

## 4. Data model

| Table | Columns | Constraints / notes |
|---|---|---|
| `users` | id, username, password_hash, pin_hash (nullable), full_name, email (nullable), is_active, created_at | username unique. A user's roles come from `role_assignments`; there is no role column. |
| `roles` | id, name, description, rank, is_system, is_student, allowed_scope_levels, created_at | name unique; rank 1–1000 (higher = more senior); `is_system` marks Super Admin (protected); exactly one role has `is_student` (the Student role); `allowed_scope_levels` ⊆ {Institution, Department, Year, Batch} |
| `role_capabilities` | role_id, capability, reach | primary key (role_id, capability); reach ∈ {InScope, OwnOnly} |
| `role_assignments` | id, user_id, role_id, branch_id, class_id, batch_id (all nullable), valid_from, valid_to (nullable dates), created_by, created_at | scope level is derived (see 5.2) and must be in the role's `allowed_scope_levels`; Student assignments must be at Batch level |
| `refresh_tokens` | id, user_id, client, token_hash, expires_at, created_at, revoked_at, replaced_by_id | rotation: each refresh revokes the old token |
| `branches`, `classes`, `batches` | id, name | name unique |
| `exams` | id, title, starts_at, ends_at, duration_minutes, mode, is_active, branch_id, class_id (nullable), batch_id (nullable), created_by, created_at, allow_question_edit_after_attempts, allow_attempt_reset, allow_attempt_reopen, allow_time_extension | target = department (branch), year (+class) or batch (+batch); `ends_at > starts_at`; `duration_minutes > 0`; mode ∈ {DesktopOnly, WebAllowed}; the four flags default to true and are ANDed with the global settings |
| `questions` | id, exam_id, text, sort_order, is_deleted | a question already given to an attempt is soft-deleted, never removed |
| `attempts` | id, exam_id, student_id, attempt_number, question_id, question_text (snapshot), client, started_at, deadline_at, submitted_at, status, submit_reason, language, code, strikes, last_saved_at, voided_at, voided_by, void_reason, allowance_minutes (nullable) | unique (exam_id, student_id, attempt_number); at most one non-voided attempt per student per exam (enforced in a transaction); status ∈ {InProgress, Submitted, AutoSubmitted, Voided}; client ∈ {Desktop, Web} |
| `attempt_events` | id, attempt_id, type, occurred_at, detail, evidence_id (nullable) | type ∈ {FaceMissing, MultipleFaces, FaceMismatch, FocusLost, Minimized, TabHidden, FullscreenExited, AudioUnavailable} |
| `evidence_files` | id, attempt_id, kind, storage_path, content_type, size_bytes, created_at | kind ∈ {ReferenceFace, Webcam, Screenshot, Audio}; stored under `{evidenceRoot}/{examId}/{attemptId}/` |
| `system_settings` | single row: allow_question_edit_after_attempts, allow_attempt_reset, allow_attempt_reopen, allow_time_extension, updated_at, updated_by | global switches; all default to true |
| `audit_log` | id, actor_user_id, action, entity_type, entity_id, exam_id (nullable), branch_id (nullable), reason (nullable), before_json, after_json, client, ip, occurred_at | append-only; indexed by (entity_type, entity_id), exam_id, branch_id, occurred_at |

Deleting an exam is refused if it has attempts (409); deactivate instead. This protects evidence.

## 5. Roles, capabilities and permissions

### 5.1 Capabilities (fixed list in code)

| Capability | Guards |
|---|---|
| `ManageSettings` | global switches |
| `ManageMasters` | departments, classes, batches |
| `ManageRoles` | roles and the role → capability grid |
| `AssignRoles` | giving/removing role assignments |
| `ManageUsers` | creating, editing, deactivating users; password/PIN resets |
| `CreateExams` | creating exams |
| `ManageExams` | editing, activating, deleting exams and their per-exam flags |
| `ResetAttempts` / `ReopenAttempts` / `ExtendTime` | the corrections in section 6 |
| `EditQuestionsAfterAttempts` | changing questions once attempts exist |
| `ViewResults` | attempts, code and live monitoring |
| `ViewEvidence` | webcam images, screenshots, audio |
| `ViewAudit` | the audit log |

Each grant has a **reach**:
- **InScope**: applies to everything inside the holder's assignment scope.
- **OwnOnly**: applies only to exams the holder created. It's meaningful for exam-related capabilities; for others it's treated as no grant.

### 5.2 Scope

An assignment's scope is given by which of `branch_id`, `class_id`, `batch_id` are set:

| Set fields | Level | Covers |
|---|---|---|
| none | Institution | everything |
| branch | Department | that department |
| branch + class | Year | that year of that department |
| branch + class + batch | Batch | that batch |

An exam or a student (whose own scope is their Student assignment) is **inside** a scope when every field the scope sets matches. A user is inside a scope if any of their Student assignments is inside it. For staff users (e.g. managing a teacher), "inside" means one of their staff assignments has a scope at or below the manager's scope.

Assignments are **active** when `valid_from ≤ today ≤ valid_to` (null bounds are open). Inactive assignments grant nothing but are kept for history.

### 5.3 How a permission is decided

For a request needing capability **C** on a target **T** (an exam, attempt, user or audit entry):

1. Load the caller's active assignments and their roles' grants (one query per request, so changes apply immediately).
2. Allowed if any assignment's role grants **C** and either:
   - reach is `InScope` and **T** is inside that assignment's scope; or
   - reach is `OwnOnly` and **T** belongs to an exam the caller created.
3. The **creator of an exam** always has `ManageExams` and `ViewResults` on that exam, so nobody is locked out of their own exam. Everything else (corrections, evidence, audit, editing questions after attempts) comes only from the grid, so admins can switch those off for creators too. The default Faculty grants give them on own exams (`OwnOnly`).
4. For corrections, the global and per-exam switches (section 6) must also be on.

Results: **403** with a reason when denied; **404** when the caller couldn't see the target at all.

`GET /auth/me` returns the caller's active assignments and their **effective capabilities with scopes**, so the clients can show or hide UI. The API still checks every request.

### 5.4 Default roles (seeded, all editable)

| Role | Rank | Allowed scopes | Default grants |
|---|---|---|---|
| **Super Admin** | 1000 | Institution | every capability, InScope (locked) |
| Principal | 900 | Institution | everything except `ManageSettings`, `ManageMasters`, `ManageRoles` |
| Exam Controller | 800 | Institution, Department | `CreateExams`, `ManageExams`, corrections, `EditQuestionsAfterAttempts`, `ViewResults`, `ViewEvidence`, `ViewAudit` |
| HOD | 700 | Department | `ManageUsers`, `AssignRoles`, `CreateExams`, `ManageExams`, corrections, `EditQuestionsAfterAttempts`, `ViewResults`, `ViewEvidence`, `ViewAudit` |
| Sub-HOD | 600 | Department | as HOD without `AssignRoles` |
| Class Coordinator | 400 | Year, Batch | `ManageUsers`, `CreateExams`, corrections, `ViewResults`, `ViewEvidence` |
| Faculty | 300 | Department, Year, Batch | `CreateExams`; corrections, `EditQuestionsAfterAttempts`, `ViewResults`, `ViewEvidence`, `ViewAudit` as **OwnOnly** |
| Student | 0 | Batch | none (students act through the attempt endpoints) |

All grants are InScope unless marked OwnOnly. "Corrections" means `ResetAttempts`, `ReopenAttempts` and `ExtendTime`.

### 5.5 Rules for editing roles and assignments

- **Ranks set the hierarchy.** With `AssignRoles` you may give or remove only roles with a **lower rank than your highest active role**, and only at scopes inside your own. With `ManageUsers` you may manage only users who hold no role at or above your rank. The one exception: **Super Admins may assign, remove and manage the Super Admin role and its holders.**
- **Student assignments** are given and removed with `ManageUsers` (so Class Coordinators can enroll students); all other roles need `AssignRoles`.
- **Nobody can change their own account or role assignments** through the API.
- **With `ManageRoles`:**
  - you may create, rename and edit roles only **below your own rank**;
  - you can't grant a capability (or a wider reach) that you don't hold yourself at Institution scope;
  - custom roles can be deleted only when no assignment references them.
- **Super Admin is protected.** The role can't be edited, deleted or have grants changed, and Super Admin assignments can't have an end date. Because only Super Admins can manage Super Admins and nobody can change their own account, at least one active Super Admin always remains.
- **The Student role can't be deleted.** A user who holds a Student assignment can't also hold staff assignments, and vice versa.
- All role, grant and assignment changes are audited with before/after values.

## 6. Staff corrections and toggles

**Effective switch** for a correction on an exam = `system_settings.<flag> AND exam.<flag>`. If it's off, the endpoint returns 403 naming the switch (e.g. "Time extensions are disabled for this exam"). `ManageSettings` changes `system_settings`; `ManageExams` (or being the creator) changes the exam flags. The caller also needs the matching capability (section 5).

| Action | Capability | Effect | Rules |
|---|---|---|---|
| **Edit questions after attempts** | `EditQuestionsAfterAttempts` | questions can be added, edited and removed even when attempts exist | attempts keep their `question_text` snapshot, so in-progress and past attempts never change; removed questions that attempts used are soft-deleted |
| **Reset attempt** | `ResetAttempts` | the attempt becomes `Voided` (kept with its code, events and evidence); the student may start a new attempt (`attempt_number + 1`) | reason required; not allowed on an already-voided attempt. If the exam window has closed, the request must include `extraMinutes`, stored as `allowance_minutes` on the voided attempt; the next attempt may then start outside the window with `deadline_at = start + allowance` |
| **Reopen attempt** | `ReopenAttempts` | `Submitted`/`AutoSubmitted` → `InProgress` with `deadline_at = now + extraMinutes`; code is kept | reason and `extraMinutes` (1–240) required |
| **Extend time (one student)** | `ExtendTime` | `deadline_at += minutes` on an `InProgress` attempt | reason required; 1–240 minutes |
| **Extend time (whole exam)** | `ExtendTime` | adds minutes to every `InProgress` attempt; with `extendWindow`, also moves `ends_at` | reason required; 1–240 minutes |

Corrections are pushed live: `DeadlineChanged` goes to the affected student's client (their timer updates without a restart) and to the staff group; `AttemptReopened` and `AttemptVoided` go to both.

## 7. Audit log

Every state-changing staff request writes one `audit_log` row **in the same database transaction** as the change, so a change can't happen unrecorded and a failed change leaves no row. Each row stores the actor, action, entity, the exam and department it concerns (for scoped viewing), before/after JSON of the changed fields, reason, client and IP.

**Audited:** users (create, update, deactivate, password/PIN reset); roles, grants and assignments; masters; exams (create, update, activate/deactivate, delete, flags); questions; system settings; attempt reset/reopen/extend; logins (success and failure).

Student exam activity (start, save, submit, proctoring events) is already recorded in `attempts` and `attempt_events`, so it isn't duplicated.

**Viewing** follows `ViewAudit`:
- InScope at Institution sees everything;
- InScope at Department/Year/Batch sees entries whose exam or department is inside that scope;
- OwnOnly sees entries for exams the viewer created.

## 8. Exam lifecycle (server-authoritative)

1. **Start** — `POST /exams/{id}/attempts` with `{ pin }`. The API checks, in order:
   - the caller holds an active Student assignment and is active; the PIN matches;
   - the exam is active, the student's batch is inside the exam's target (a department, a year or a batch), and now ∈ [starts_at, ends_at) — or a reset allowance exists (section 6);
   - no non-voided attempt exists (409; if it's `InProgress`, the client resumes it via `GET /attempts/{id}`);
   - the client in the token is `Desktop`, or the exam mode is `WebAllowed` (else 403 with reason).

   It then picks a random non-deleted question, snapshots its text into the attempt, and sets `deadline_at = min(now + duration, ends_at)`, or `now + allowance` after a reset outside the window. Response: attempt id, question text, `deadline_at`, server time.
2. **During** — the client auto-saves (`PUT /attempts/{id}/code`, every 30 s), posts proctoring events and uploads evidence. All are rejected (409) once the attempt isn't `InProgress`.
3. **Submit** — `POST /attempts/{id}/submit` with final code, language and reason; accepted up to `deadline_at + 30 s`, later calls get 409.
4. **Auto-close** — a background service runs every 15 s and marks attempts past `deadline_at + 30 s` as `AutoSubmitted` with the last saved code and reason "Deadline passed". A crash or network loss can't leave an attempt open.
5. **Live** — every transition and event is pushed to the exam's SignalR group.
6. **Corrections** — staff may reset, reopen or extend attempts (section 6); auto-close always uses the current `deadline_at`.

Strike counting and the 3-strike auto-submit decision stay in the clients (sub-projects 1–2). The server records `strikes` from events, so the evidence trail is independent of client honesty.

**Known limit:** the server can't prove a request comes from the genuine desktop app; a modified client could claim `Desktop`. Client attestation and code signing are out of scope for the demo.

## 9. API

Base path `/api`. Every endpoint needs a bearer token unless marked 🔓. "Who" names the capability required, checked against the target as in 5.3.

### Auth
| Method & path | Body → Response | Notes |
|---|---|---|
| `POST /auth/login` 🔓 | `{username, password, client}` → `{accessToken, refreshToken, expiresAt, user}` | `client` ∈ {Desktop, Web}, embedded as a claim |
| `POST /auth/refresh` 🔓 | `{refreshToken}` → same as login | rotates the token; reuse of a revoked token revokes all of that user's refresh tokens (treated as theft) |
| `POST /auth/logout` | `{refreshToken}` → 204 | |
| `GET /auth/me` | → user, active assignments, effective capabilities with scopes | |

Access token: JWT, 15 minutes, containing user id and client only (permissions are loaded per request). Refresh token: random 256-bit value, 7 days, stored as SHA-256 hash.

### Roles and assignments
| Method & path | Who |
|---|---|
| `GET /capabilities` | any staff |
| `GET /roles` | any staff |
| `POST /roles` · `PUT /roles/{id}` · `DELETE /roles/{id}` | `ManageRoles` (5.5 rules) |
| `PUT /roles/{id}/capabilities` `[{capability, reach}]` | `ManageRoles` |
| `GET /users/{id}/assignments` | `ManageUsers` or `AssignRoles` on that user |
| `POST /users/{id}/assignments` `{roleId, branchId?, classId?, batchId?, validFrom?, validTo?}` | `AssignRoles` (Student role: `ManageUsers`) |
| `PUT /assignments/{id}` (dates only) · `DELETE /assignments/{id}` | `AssignRoles` (Student role: `ManageUsers`) |

### Users
`GET /users?roleId=&branchId=&search=&page=` (results limited to users inside the caller's scope) · `POST /users` (with initial assignments) · `GET /users/{id}` · `PUT /users/{id}` · `DELETE /users/{id}` (soft: `is_active = false`) · `POST /users/{id}/reset-password` · `POST /users/{id}/reset-pin` — all `ManageUsers` (5.5 rules). Students created by a Class Coordinator must be inside the coordinator's scope.

### Masters
`GET /branches`, `/classes`, `/batches` (any signed-in user) · `POST/PUT/DELETE` (`ManageMasters`; delete returns 409 when in use)

### Exams
| Method & path | Who | Behavior |
|---|---|---|
| `GET /exams?from=&to=&branchId=` | any | staff: exams they created or can `ViewResults`/`ManageExams` on · students: eligible, active, not yet attempted |
| `GET /exams/{id}` | creator, `ViewResults`/`ManageExams`, eligible student | students don't get the question list |
| `POST /exams` | `CreateExams`, with the exam's target inside scope | with questions and flags; `created_by` = caller |
| `PUT /exams/{id}` | creator or `ManageExams` | once attempts exist, question changes also need `EditQuestionsAfterAttempts` and its switches (else 403) |
| `PATCH /exams/{id}/active` | creator or `ManageExams` | |
| `DELETE /exams/{id}` | creator or `ManageExams` | 409 if attempts exist |

### Attempts
| Method & path | Who |
|---|---|
| `POST /exams/{id}/attempts` | student (section 8) |
| `GET /attempts/{id}` | own student, or `ViewResults` on the exam |
| `PUT /attempts/{id}/code` · `POST /attempts/{id}/events` · `POST /attempts/{id}/submit` | own student |
| `POST /attempts/{id}/evidence` (multipart, ≤ 10 MB; jpeg/png/wav) | own student |
| `GET /exams/{id}/attempts` | `ViewResults` on the exam |
| `GET /evidence/{id}` | `ViewEvidence` on the exam (and the student, for their own reference face) |

### Settings, corrections and audit
| Method & path | Who | Notes |
|---|---|---|
| `GET /settings` | any staff | global switches |
| `PUT /settings` | `ManageSettings` | |
| `POST /attempts/{id}/reset` `{reason, extraMinutes?}` | `ResetAttempts` | + switches |
| `POST /attempts/{id}/reopen` `{reason, extraMinutes}` | `ReopenAttempts` | + switches |
| `POST /attempts/{id}/extend` `{minutes, reason}` | `ExtendTime` | + switches |
| `POST /exams/{id}/extend` `{minutes, reason, extendWindow}` | `ExtendTime` | + switches |
| `GET /audit?examId=&entityType=&entityId=&actorId=&from=&to=&page=` | `ViewAudit` | filtered as in section 7 |

### Code runner
`POST /run` `{language, code, stdin}` → `{stdout, stderr, exitCode, timedOut, durationMs}`. Any signed-in user; 10 runs/minute per user.

### Live hub
`/hubs/exams`. Staff call `JoinExam(examId)`, authorized like `GET /exams/{id}/attempts`. Server messages: `AttemptStarted`, `EventRecorded`, `CodeSaved`, `AttemptSubmitted`, `DeadlineChanged`, `AttemptReopened`, `AttemptVoided`. Each student is automatically joined to a private group for their own attempts and receives `DeadlineChanged`, `AttemptReopened` and `AttemptVoided`.

## 10. Code runner

`ICodeRunner` with two implementations, selected by `Runner:Mode`:

- **Docker** (default in Compose): one container per run, created through the Docker Engine API (`Docker.DotNet`).
  - Images: `python:3.12-slim`, `node:22-slim`, `eclipse-temurin:21-jdk`, `gcc:14`.
  - Limits: no network, 256 MB memory, 0.5 CPU, pids limit 64, read-only root filesystem with a writable tmpfs work dir, non-root user.
  - Timeouts: 10 s compile, 5 s run (container killed on expiry). Output capped at 64 KB per stream.
  - Languages and commands live in configuration, so adding a language is config-only.
- **Local** (dev without Docker): today's process-based `CodeExecutor`, same timeouts and output caps. Not for production.

## 11. Security

- BCrypt for passwords and PINs; existing bcrypt hashes carry over.
- Login rate limiting: 5 failures per username → locked for 5 minutes.
- JWT signing key from configuration (Development has a fixed dev key; Compose reads it from env).
- Permissions evaluated per request from the database (section 5.3), so revoking a role takes effect immediately.
- CORS: only the configured web origin.
- Upload validation: size limit, content-type allow-list, server-generated file names (no user-controlled paths).
- Evidence served only through authorized endpoints, never as static files.
- HTTPS in deployment (terminated by a reverse proxy); plain HTTP allowed locally.

## 12. Error handling

- All errors use RFC 7807 `application/problem+json`, with `errors` (field → messages) for validation failures (400).
- Status codes: 400 validation · 401 unauthenticated/expired · 403 forbidden with a human-readable `detail` (e.g. "This exam can only be taken in the SecuCode desktop app", "You can only assign roles below your own") · 404 · 409 state conflicts (attempt exists or closed, master or role in use) · 429 rate limited.
- Unhandled exceptions: logged with a correlation id, returned as a 500 problem without internals.

## 13. Testing

- **Integration tests** (xUnit + `WebApplicationFactory` + Testcontainers MySQL; Docker must be running):
  - auth: login, refresh rotation, revoked-token reuse, lockout;
  - permissions: a capability matrix test (each seeded role × each capability against targets inside and outside its scope); OwnOnly vs InScope; a multi-role user getting the union; expired and future-dated assignments granting nothing; revocation applying on the next request;
  - role administration: can't assign or edit at or above own rank, can't grant capabilities you lack, Super Admin protections (role locked, no end dates, only Super Admins manage Super Admins), nobody changing their own account or roles, Student/staff exclusivity, custom role delete blocked while in use;
  - attempt lifecycle: eligibility via student scope, PIN check, duplicate attempt, mode rule (Web client vs DesktopOnly), deadline capped by `ends_at`, submit grace, background auto-submit, writes rejected after close;
  - evidence upload validation and authorized download;
  - masters/exams 409 rules;
  - corrections: capability and switches (global off vs exam off); reset voids with data intact and allows a new attempt (incl. after the window closes); reopen; single and whole-exam extension (incl. `extendWindow`); auto-close respecting extended deadlines; question edits not altering snapshots;
  - audit: every audited endpoint writes exactly one row with correct before/after, a failed change writes none, and scoped viewers see only their entries.
- **Runner tests:** output, stdin, compile error, timeout, memory limit, no network (Docker mode); the existing `Secucode.Checks` cases move here for Local mode.
- The seed command is exercised by a test against an empty database, including the default role grid.

## 14. Out of scope for sub-project 0

New desktop and web UIs (including the role/capability grid, corrections and audit screens); CSV import; subjects and rooms; timetable and clash detection; question bank; grading/test cases; notifications; analytics; demo-college seed; cloud deployment; client attestation.
