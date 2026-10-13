# SecuCode Platform — Sub-project 1: Windows App on the API + UI Foundation

**Date:** 2026-10-13
**Status:** Draft for review
**Builds on:** `docs/superpowers/specs/2026-10-06-secucode-platform-backend-design.md` (sub-project 0, merged in PR #1)

## 1. Goal and context

The WPF app (`clients/Secucode.Desktop`) still talks to its own MySQL database. Sub-project 1 rebuilds it as a **thin client of the SecuCode API** and gives it a **new, hand-built Fluent-style UI foundation** (theme tokens, sidebar shell, page transitions, light/dark/system).

**Success:** a student can log in, see eligible exams, start or resume a proctored exam, survive a network drop or app crash without losing work, and submit — all through the API. A staff member can log in, list exams, create/edit/activate exams with questions, and view attempts. Both look polished in light and dark. The app no longer references MySQL.

### Decisions made with the user

| Decision | Choice |
|---|---|
| Who the app serves | **Students + staff at today's feature level.** Admin, corrections, audit, evidence viewer and live monitor are web-first (sub-projects 3–5). |
| UI foundation | **Hand-built theme** (own ResourceDictionaries and control templates); no UI library. |
| Framework | Stays on **.NET 8** (`net8.0-windows`), matching the server. |
| MVVM | Plain `INotifyPropertyChanged` view models + one `RelayCommand`; no MVVM framework. |
| Proctoring | Stays local (webcam, audio, lockdown, screenshot); evidence and events go to the API. |
| Network loss | Local **outbox** with in-order retry; proctoring continues offline. |

## 2. Architecture

```
clients/Secucode.Desktop/
  App.xaml(.cs)            merges Theme/*, loads settings, opens LoginWindow
  Theme/
    Colors.Light.xaml      colour tokens (brushes) — light
    Colors.Dark.xaml       colour tokens (brushes) — dark
    Typography.xaml        type ramp
    Controls.xaml          control styles and templates
    Motion.xaml            durations, easing, page transition storyboards
    ThemeManager.cs        swaps the colour dictionary; follows Windows when "System"
  Shell/                   ShellWindow (custom title bar, sidebar, content host), navigation
  Api/                     ApiClient, TokenStore, ApiError, LiveConnection (SignalR)
  Features/
    Login/                 LoginWindow + view model (one login for everyone)
    Student/               Dashboard, ExamDetails (PIN + face reference), ExamWindow
    Staff/                 ExamList, ExamEditor, AttemptList
    Settings/              theme choice, API address
  Proctoring/              FaceMonitor, AudioRecorder, Lockdown, ScreenCapture (no UI, no network)
  Outbox/                  AttemptOutbox (disk-backed queue for one attempt)
  AppSettings.cs           %LocalAppData%\Secucode\settings.json (theme, API base URL)
```

- The desktop project **references `server/Secucode.Contracts`** and uses its DTOs and enums directly. No duplicated request/response types.
- Packages: **add** `Microsoft.AspNetCore.SignalR.Client`; **remove** `MySql.Data` and `BCrypt.Net-Next`. Emgu.CV and NAudio stay.
- **Deleted:** `Db.cs`, `User.cs`, `Exam.cs`, `Test.cs`, `CodeExecutor.cs` (code runs via `POST /run`), the two login windows/view models, `MainWindow`, `TeacherMainWindow`, and `Database/setup.sql` + `Database/add-user.ps1` (retired, as the sub-project 0 spec says). Their behavior moves into the structure above.
- `Secucode.Desktop.Checks` keeps only the hardware checks (webcam, face detection, microphone, screenshot); its local code-runner and MySQL checks are removed.

### One API change

`AssignmentDto` gains `bool IsStudent` (from `Role.IsStudent`), so the client can tell a student from staff after `GET /auth/me` (students can't call `GET /roles`). Mapped in `UserMapping.ToDto`; one API test asserts it.

## 3. Login and navigation

- **One login screen** (username, password). `POST /auth/login` with `client = Desktop`, then `GET /auth/me`.
- **Student** = any active assignment with `IsStudent`. Students get the shell with: *Exams*, *Settings*. Everyone else gets staff navigation built from effective capabilities:
  - *Exams* — always (the API scopes the list);
  - *New exam* button — only with `CreateExams`;
  - edit / activate / delete — only with `ManageExams` or as creator; *Attempts* — only with `ViewResults` or as creator;
  - *Settings* — always.
- The UI only hides what the user can't do; the API stays the authority. A 403 is shown with the server's reason.
- **Logout** calls `POST /auth/logout` and returns to login. Tokens live only in memory (no persisted login).

## 4. ApiClient

- One `HttpClient` with the base URL from settings. JSON options shared with the API (enum as string, UTC).
- Adds the bearer token. On **401**, refreshes once (`POST /auth/refresh`) and retries; if refresh fails, raises `SessionExpired`, and the app returns to login.
- Concurrent 401s share one refresh (a single in-flight refresh task).
- Non-success responses are parsed as problem+json into `ApiError { Status, Title, Detail }`. Network failures become `ApiError { Status = 0, Title = "Can't reach the server" }`.
- `LiveConnection` wraps the SignalR hub `/hubs/exams` with the same token provider and automatic reconnect.

## 5. Student flow

1. **Dashboard** — `GET /exams` → `StudentExamDto` list (title, start/end, duration). Exams with `InProgressAttemptId` show **Resume**; others show **Start**. Refresh button; no live updates.
2. **Exam details** (Start) — the student enters the PIN and captures a face reference (webcam crop, as today). `POST /exams/{id}/attempts { pin }`. On success, upload the reference with `POST /attempts/{id}/evidence` (kind `ReferenceFace`) and open the exam window. Errors (wrong PIN, not open yet, already submitted) are shown inline from the server's `detail`.
   **Resume** skips the PIN: the student captures a fresh face reference (uploaded as another `ReferenceFace`), then `GET /attempts/{id}` and the exam window opens with the saved code. The face recognizer always trains on the reference captured on this screen.
3. **Exam window** — borderless, full screen, top-most (lockdown as today: copy/paste blocked, focus loss and minimize counted).
   - **Timer:** `remaining = DeadlineAt − (localNow + offset)`, where `offset = ServerTime − localNow` measured when the attempt is fetched.
   - **Auto-save** every 30 s: `PUT /attempts/{id}/code` (via the outbox; only the latest unsent save is kept).
   - **Run:** `POST /run`; output shown as today. 429 shows "Too many runs — wait a moment".
   - **Strikes:** counted locally exactly as today (3 consecutive face failures; 3 focus misses). Each violation queues an event (`FaceMissing`, `MultipleFaces`, `FaceMismatch`, `FocusLost`, `Minimized`, `AudioUnavailable`) with its webcam frame as `Webcam` evidence.
   - **Audio:** recorded in 60 s WAV chunks; each finished chunk is queued as `Audio` evidence (chunks keep uploads under 10 MB).
   - **Live:** subscribes to its attempt via `LiveConnection`. `DeadlineChanged` updates the timer. `AttemptVoided` stops proctoring and closes with the reason.
4. **Submit** (button, timer, or strikes) — queue a final `Webcam` frame and a `Screenshot`, then `POST /attempts/{id}/submit { code, language, reason }`. The window shows "Submitted" and returns to the dashboard.

## 6. Network loss and the outbox

`AttemptOutbox` is a disk-backed FIFO under `%LocalAppData%\Secucode\outbox\{attemptId}\`:

- Each item is a file: `{seq}.json` for events/code saves/submit, plus a sibling file for evidence bytes. Evidence events are linked by uploading the evidence first and filling `evidenceId` on the following event.
- A single sender sends items in order. On network failure it retries with backoff (1 s doubling to 30 s) and keeps the queue on disk. Items are deleted once the server accepts them.
- **Offline banner** while the sender is failing: "Offline — your work is saved on this PC and will be sent when the connection returns."
- **Submit** is the last item. It is retried until `DeadlineAt + 30 s` (server time). After that, the server has auto-submitted the last saved code; the window says so and closes. The API rejects writes to a closed attempt, so anything still queued (normally only evidence captured while offline) gets a 409 and is discarded — items are sent in order, so everything queued before the outage has already gone.
- **409** on any item means the attempt is no longer in progress: proctoring stops, the queue is discarded, and the window explains why (submitted, auto-submitted, or voided — read from `GET /attempts/{id}`).
- **Crash or restart:** on resume (section 5), any leftover outbox for that attempt is sent before new items.
- A local copy of the code is also written on every auto-save, as today.

## 7. Staff flow

- **Exam list** — `GET /exams` → `ExamDto` grid (title, window, duration, mode, active). Filter by date range.
- **Exam editor** — create (`POST /exams`) or edit (`PUT /exams/{id}`): title, start/end (local time, sent as UTC), duration, mode (`DesktopOnly`/`WebAllowed`), target (department, optional year, optional batch — from `GET /branches`, `/classes`, `/batches`), the four per-exam switches, active, questions (add/remove/reorder). There is no exam PIN: the PIN is each student's own (set by staff in user management). If questions are locked after attempts, the 403 reason is shown.
- **Activate/deactivate** (`PATCH /exams/{id}/active`) and **delete** (`DELETE`; a 409 suggests deactivating).
- **Attempts** — `GET /exams/{id}/attempts` → `AttemptSummaryDto` grid (student, status, client, start, submitted, strikes). Selecting one shows `GET /attempts/{id}`: question, code, submit reason, event list. Evidence viewing, corrections and the live monitor are out of scope (section 11).

## 8. Theme and motion

**Tokens.** Every control references brushes by name with `DynamicResource`:
`Surface`, `SurfaceRaised`, `SurfaceSunken`, `Border`, `BorderStrong`, `TextPrimary`, `TextSecondary`, `TextDisabled`, `Accent`, `AccentHover`, `AccentPressed`, `OnAccent`, `Danger`, `Warning`, `Success`, `Focus`. `Colors.Light.xaml` and `Colors.Dark.xaml` define the same keys.

**ThemeManager.** Setting ∈ {Light, Dark, System}. System reads `HKCU\…\Themes\Personalize\AppsUseLightTheme` and listens to `SystemEvents.UserPreferenceChanged`. Switching replaces the colour dictionary in `Application.Resources.MergedDictionaries`.

**Type.** Segoe UI Variable (fallback Segoe UI): Caption 12, Body 14, Subtitle 20 semibold, Title 28 semibold, Display 40 semibold. Code editor: Cascadia Mono (fallback Consolas) 14.

**Controls.** Styled with hover/pressed/focus/disabled states: Button (Primary, Standard, Subtle), TextBox, PasswordBox, ComboBox, CheckBox, ToggleSwitch (CheckBox template), DataGrid, ScrollBar, Card (Border style), InfoBar (info/success/warning/error), dialog overlay. Corner radius 8 (controls), 12 (cards). Focus shows a 2 px `Focus` ring.

**Shell window.** `WindowChrome` custom title bar (app name, minimize/maximize/close), collapsible sidebar (icons from Segoe Fluent Icons), content host.

**Motion** (`Motion.xaml`):
- page change: opacity 0→1 + translate Y 12→0, 200 ms, cubic ease-out;
- sidebar selection indicator slides to the selected item, 200 ms;
- hover colour transitions 100 ms; InfoBar and dialog fade + scale 0.96→1, 150 ms;
- all animations are skipped when `SystemParameters.ClientAreaAnimation` is false.

The exam window uses the same theme but no shell.

## 9. Error handling

- Screens show `ApiError` in an InfoBar (`Title` + `Detail`). Status 0 shows **Retry**.
- `SessionExpired` returns to login with "Your session expired. Please sign in again." (During an exam, the outbox keeps queuing; after re-login the student resumes the attempt.)
- Proctoring start failure (no webcam / cascade missing) refuses to start the exam before calling the API, as today.

## 10. Testing

- **`clients/Secucode.Desktop.Tests`** (xUnit, `net8.0-windows`):
  - **ApiClient vs the real API** via the sub-project 0 `ApiFactory` (in-memory test server, MySQL container; the test project references `Secucode.Api.Tests` to reuse its fixtures): login, `me` with `IsStudent`, 401 → refresh → retry, concurrent 401s share one refresh, failed refresh → `SessionExpired`, problem+json → `ApiError`; start → save → submit; resume via `InProgressAttemptId`; 409 after submit.
  - **AttemptOutbox** with a fake sender: persists across a new instance (restart), sends in order, backs off on failure, links evidence id to the next event, stops and clears on 409, keeps only the latest code save.
  - **View models:** staff navigation/actions per capability set; student vs staff detection; timer maths with a server clock offset.
- **API:** one assertion that `AssignmentDto.IsStudent` is set.
- **`Secucode.Desktop.Checks`:** manual hardware smoke checks.
- **UI:** verified by running the app against the seeded API and taking screenshots of login, dashboard, exam window, exam list, editor and attempts — light and dark. No UI automation.

## 11. Out of scope for sub-project 1

Admin screens (users, role/capability grid, assignments, masters, settings switches), staff corrections, audit view, evidence viewer, live exam monitor (sub-projects 3–5); syntax-highlighting editor; persisted login; installer rebuild and auto-update; web app (sub-project 2).
