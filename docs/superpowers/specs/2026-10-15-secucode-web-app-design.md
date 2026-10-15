# SecuCode Platform — Sub-project 2: Web App Foundation + Web Exams

**Date:** 2026-10-15
**Status:** Draft for review
**Builds on:** sub-project 0 (`2026-10-06-secucode-platform-backend-design.md`, merged in PR #1) and sub-project 1 (`2026-10-13-secucode-desktop-client-design.md`, branch `feature/desktop-client`, not yet merged). This work branches from `feature/desktop-client` because it uses `AssignmentDto.IsStudent`, added there.

## 1. Goal and context

The API has no browser client. Sub-project 2 creates `clients/web`: a React app with a design system matching the desktop app, sign-in, permission-based navigation, the staff exam pages the desktop already has, and **student web exams** for exams in `WebAllowed` mode.

**Success:** in a desktop browser, a staff member can sign in, list, create, edit, activate and delete exams and view attempts with their evidence. A student can sign in, start or resume a `WebAllowed` exam in fullscreen with webcam face detection, focus strikes and audio evidence, survive a brief network drop or a page reload without losing code, and submit. Both look polished in light and dark and match the desktop app.

### Decisions made with the user

| Decision | Choice |
|---|---|
| Scope | Shell + staff exam pages + student web exams |
| Web proctoring | Fullscreen and focus strikes **plus** webcam face detection (no face / several faces) and webcam + microphone evidence. No face matching. |
| UI foundation | Hand-built CSS design system from the desktop tokens; no component library |
| Hosting | The API serves the built app (same origin, no CORS, no extra container) |
| API types | Hand-written TypeScript types mirroring `Secucode.Contracts` (the Swagger document has no response schemas) |
| Refresh token | `sessionStorage` (survives reload, cleared when the tab closes) |

## 2. Architecture

```
clients/web/                     Vite + React 18 + TypeScript (strict)
  index.html, vite.config.ts     dev server :5173 proxies /api and /hubs to :8080; build → server/Secucode.Api/wwwroot
  src/
    main.tsx, App.tsx            router, query client, theme, auth provider
    theme/
      tokens.css                 colour tokens (light, dark), type ramp, radii, motion durations/easing
      controls.css               control styles (hover/pressed/focus/disabled)
      theme.ts                   Light / Dark / System; System follows prefers-color-scheme
    ui/                          Button (Primary/Standard/Subtle), TextField, PasswordField, Select, Checkbox,
                                 Toggle, Card, InfoBar, Dialog, DataTable, Spinner
    shell/                       AppShell (collapsible sidebar, content host), nav from capabilities, page transition
    api/
      client.ts                  fetch wrapper: bearer token, single shared refresh, problem+json → ApiError
      types.ts                   DTOs and enums mirroring server/Secucode.Contracts
      live.ts                    SignalR connection to /hubs/exams
    auth/                        AuthProvider (tokens, me, isStudent, capabilities), RequireAuth route guard
    features/
      login/                     sign-in page
      student/                   Dashboard, ExamStart, ExamPage
      staff/                     ExamList, ExamEditor, AttemptList, AttemptDetail
      settings/                  theme choice, sign out
    proctoring/
      faceMonitor.ts             MediaPipe face detector on the webcam stream
      audioRecorder.ts           MediaRecorder, 60 s chunks
      focusGuard.ts              tab hidden / window blur / fullscreen exit → one violation per trip away
      strikes.ts                 strike counting (pure, unit-tested)
      attemptQueue.ts            in-memory ordered sender with backoff (pure core, unit-tested)
  e2e/                           Playwright tests
```

- **Packages:** `react`, `react-dom`, `react-router-dom`, `@tanstack/react-query`, `@microsoft/signalr`, CodeMirror 6 (`codemirror`, `@codemirror/lang-python`, `-javascript`, `-java`, `-cpp`), `@mediapipe/tasks-vision`. Dev: `vite`, `typescript`, `vitest`, `@testing-library/react`, `@playwright/test`. Nothing else.
- **Types:** `api/types.ts` mirrors the Contracts records and enums used by the app (enums as string unions, dates as ISO strings). Drift is caught by the Playwright tests against the real API.
- **MediaPipe model and WASM** files are served from `public/mediapipe/` (copied from the package at build), not a CDN, so exams don't depend on a third-party host.

### Hosting and API changes

1. `npm run build` writes to `server/Secucode.Api/wwwroot` (git-ignored). The API adds `UseDefaultFiles` + `UseStaticFiles` and `MapFallbackToFile("index.html")` for any path not under `/api`, `/hubs` or `/swagger`. With no `wwwroot`, the API behaves as today.
2. Responses for the web app carry a **Content-Security-Policy**: `default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; worker-src 'self' blob:; frame-ancestors 'none'`. It is set only on static-file and fallback responses, not on `/api` responses.
3. Evidence content types gain `["audio/webm"] = ".webm"` (browsers record Opus in WebM).
4. The `Dockerfile` gains a Node stage that runs `npm ci && npm run build` and copies the output into the API image's `wwwroot`.

## 3. Sign-in, session and navigation

- **One sign-in page.** `POST /auth/login` with `client = "Web"`, then `GET /auth/me`. **Student** = any active assignment with `isStudent`.
- **Tokens:** access token in memory only; refresh token in `sessionStorage`. On page load, if a refresh token exists, the app refreshes and loads `me` before rendering routes. Sign-out calls `POST /auth/logout`, clears both and returns to sign-in.
- **ApiClient:** adds the bearer token; on 401 refreshes once and retries; concurrent 401s share one in-flight refresh. A refresh refused with 400/401/403 ends the session (`SessionExpired` → sign-in page with "Your session expired. Please sign in again." and a return path). Other refresh failures throw and keep the session (as in sub-project 1). Non-success responses parse problem+json into `ApiError { status, title, detail }`; network failures become `ApiError { status: 0, title: "Can't reach the server" }`.
- **Navigation** (same rules as the desktop):
  - Students: *Exams*, *Settings*.
  - Staff: *Exams* (always), *New exam* button with `CreateExams`; edit / activate / delete with `ManageExams` or as creator; *Attempts* with `ViewResults` or as creator; *Settings*.
  - The UI only hides what the user can't do; the API decides. A 403 shows the server's reason.
- **Routes:** `/login`, `/exams`, `/exams/new`, `/exams/:id/edit`, `/exams/:id/attempts`, `/attempts/:id`, `/exams/:id/start` (student), `/attempt/:id` (student exam page), `/settings`. A route a user can't use redirects to `/exams`.

## 4. Student web exam

### Dashboard
`GET /exams` → `StudentExamDto` list (title, local start/end, duration). `WebAllowed` exams show **Resume** when `inProgressAttemptId` is set, else **Start** (enabled within 5 minutes of the window, as on the desktop; the server decides). `DesktopOnly` exams show "Desktop app only" and no button. A refresh button; the list also refreshes every 30 s.

### Start page (`/exams/:id/start`)
1. **Browser check:** `navigator.mediaDevices.getUserMedia` and `document.documentElement.requestFullscreen` must exist; otherwise a refusal names what's missing.
2. **Permissions:** request camera and microphone (`getUserMedia({ video, audio })`; if audio fails, retry video-only). Camera refused or missing → can't start, with instructions to allow camera access. Microphone refused or missing → continue; the exam page posts `AudioUnavailable` once.
3. **Preview:** live webcam preview with the face monitor running; shows "Face detected" / "No face" / "More than one face". Start is enabled only while exactly one face is detected.
4. **Start:** PIN field + **Start**. The click calls `requestFullscreen()` first (needs the user gesture), then `POST /exams/{id}/attempts { pin }`. On success, upload one frame as `ReferenceFace` and navigate to `/attempt/{id}`. On an error, exit fullscreen and show the server's `detail` inline (wrong PIN, not open, already submitted). A 409 with an in-progress attempt behaves as Resume.
5. **Resume:** same page without the PIN; **Resume** enters fullscreen, uploads a fresh `ReferenceFace`, and navigates to `/attempt/{id}`.

The webcam/microphone stream is kept in a module-level holder so the exam page reuses it without a second permission prompt. Opening `/attempt/:id` directly (e.g. after a reload) without a stream redirects to the start page in Resume mode.

### Exam page (`/attempt/:id`)
Fullscreen, no shell. Layout: question (left), editor with language picker (Python, JavaScript, Java, C++) and **Run** / **Submit** (right), output panel below, timer and strike indicators in the top bar.

- **Load:** `GET /attempts/{id}`. Code = the local copy (`localStorage` key `secucode.code.{attemptId}`, with its save time) if it is newer than the server's last save, else the server's code.
- **Timer:** `remaining = deadlineAt − (Date.now() + offset)`, `offset = serverTime − Date.now()` measured on each fetch. At zero → auto-submit with reason "Time up".
- **Auto-save:** every 30 s if changed, `PUT /attempts/{id}/code` through the queue (only the latest unsent save is kept). Every edit also writes the local copy (debounced 1 s).
- **Run:** `POST /run`; output shown. 429 → "Too many runs — wait a moment".
- **Live:** `live.ts` joins the attempt's exam group. `DeadlineChanged` updates the timer; `AttemptVoided` stops proctoring and shows the reason. After every connect/reconnect the page re-reads `GET /attempts/{id}` (the live contract from sub-project 0).
- **Face monitor:** every 3 s, MediaPipe face detector (short-range model) on the current video frame. 0 faces → `FaceMissing`; ≥ 2 → `MultipleFaces`. Each violation queues a webcam JPEG (canvas, quality 0.7) as `Webcam` evidence followed by the event linked to it. **3 consecutive failures** → auto-submit "Face check failed 3 times". A good check resets the run. The video track ending (camera unplugged) → one `FaceMissing` per check, as for no face.
- **Leaving the exam:** `focusGuard` listens to `visibilitychange` (hidden → `TabHidden`), window `blur` (→ `FocusLost`) and `fullscreenchange` (exit → `FullscreenExited`). A **trip away** starts on the first of these and ends when the page is visible, focused and fullscreen again; each trip posts one event (the first signal's type) and counts **one strike**. The **3rd trip** auto-submits "Left the exam 3 times". Strike counts survive a reload via `sessionStorage` (`secucode.strikes.{attemptId}`).
- **Fullscreen overlay:** while not fullscreen, an overlay covers the editor: "Return to fullscreen to continue" + button (`requestFullscreen()`).
- **Blocked:** copy, cut, paste, drop and the context menu in the editor and question (event handlers + CodeMirror config). Browser shortcuts can't be blocked; leaving is caught by the focus guard instead.
- **Audio:** `MediaRecorder` on the audio track (`audio/webm;codecs=opus`) with a 60 s timeslice; each chunk is a self-contained recording (recorder restarted every 60 s) queued as `Audio` evidence.

### Attempt queue and network loss
`attemptQueue` sends items **in order** through one sender: code saves, events, evidence uploads (an evidence upload's returned id is filled into the following event), submit. On network failure (status 0 or 5xx) it retries with backoff (1 s doubling to 30 s) and shows the banner "Offline — your work is kept in this tab and will be sent when the connection returns." Submit cuts the backoff short.
- **Submit** is retried until `deadlineAt + 30 s` (server time). After that, the page says the server has auto-submitted the last saved code.
- **409** on any item → the attempt is closed: stop proctoring, discard the queue, read `GET /attempts/{id}` and say why (submitted, auto-submitted, voided).
- **400/403/413/415** on an item → drop that item and continue (logged to the console).
- **Limits:** the queue lives in memory. Closing the tab while offline loses queued events and evidence; the code survives through the server's auto-close plus the local copy shown on Resume.

### Submit
From the button (with a confirm dialog), the timer or strikes: stop the monitors, queue a final `Webcam` frame, then `POST /attempts/{id}/submit { code, language, reason }`. On success: clear the local copy and strike count, exit fullscreen, stop the media tracks, show "Submitted", back to the dashboard. No `Screenshot` evidence on the web (browsers need a screen-share prompt).

If the session expires during the exam, the queue pauses; a sign-in dialog over the exam page (not a navigation) lets the student sign in again, and the queue resumes.

## 5. Staff pages

Same features as the desktop, same endpoints:
- **Exam list** — `GET /exams?from=&to=`, date-range filter, table (title, window, duration, mode, active, created by), row actions per permissions. Delete asks for confirmation; a 409 suggests deactivating.
- **Exam editor** — title; start/end in local time (`<input type="datetime-local">`, sent as UTC); duration; mode; target department / optional year / optional batch from `GET /branches`, `/classes`, `/batches`; the four per-exam switches; active; questions with add, remove, move up/down. A 403 when questions are locked after attempts shows the reason. Unsaved-changes prompt on leave.
- **Attempts** — `GET /exams/{id}/attempts` table (student, status, client, started, submitted, strikes).
- **Attempt detail** — `GET /attempts/{id}` (returns `AttemptDetailDto` for everyone; the student exam page uses its `attempt`): question, code (read-only CodeMirror), submit reason, event list. **Evidence:** thumbnails for images and `<audio>` players for audio, loaded via `GET /evidence/{id}` with the bearer token (fetched as blobs → object URLs). The list comes from `AttemptDetailDto.Evidence`; events link to their evidence through `evidenceId`.

## 6. Theme and motion

- **Tokens** (`tokens.css`): the desktop colour keys as CSS variables — `--surface`, `--surface-raised`, `--surface-sunken`, `--border`, `--border-strong`, `--text-primary`, `--text-secondary`, `--text-disabled`, `--accent`, `--accent-hover`, `--accent-pressed`, `--accent-soft`, `--on-accent`, `--danger(-soft)`, `--warning(-soft)`, `--success(-soft)`, `--focus`, `--subtle-hover`, `--subtle-pressed` — with the exact values from `Colors.Light.xaml` / `Colors.Dark.xaml`. Light on `:root`; dark under `@media (prefers-color-scheme: dark)` for `:root:not([data-theme=light])` and under `:root[data-theme=dark]`.
- **Theme setting:** Light / Dark / System in `localStorage` (`secucode.theme`); applied to `<html data-theme>` before first paint (inline script in `index.html`).
- **Type:** `"Segoe UI Variable", "Segoe UI", system-ui, sans-serif`: Caption 12, Body 14, Subtitle 20/600, Title 28/600, Display 40/600. Code: `"Cascadia Mono", Consolas, ui-monospace, monospace` 14.
- **Shape:** radius 8 (controls), 12 (cards); 2 px `--focus` ring on `:focus-visible`.
- **Motion:** page enter opacity 0→1 + translateY 12→0, 200 ms `cubic-bezier(0.1, 0.9, 0.2, 1)`; sidebar selection indicator slides 200 ms; hover colour 100 ms; dialog and InfoBar fade + scale 0.96→1, 150 ms. All disabled under `prefers-reduced-motion: reduce`.
- **Accessibility:** semantic elements (`button`, `label`, `table`), InfoBar errors use `role="alert"`, dialogs trap focus and close on Escape, visible focus everywhere, contrast as the desktop tokens.
- **Viewport:** designed for ≥ 1280 px. Below 900 px the sidebar collapses; student exam routes show "Web exams need a computer" on touch-only devices (`(pointer: coarse)` with no fine pointer).

## 7. Error handling

- Pages show `ApiError` in an InfoBar (title + detail); status 0 adds **Retry**.
- `SessionExpired` outside an exam → sign-in page with the message and a return path; during an exam → sign-in dialog (section 4).
- Unexpected render errors → an error boundary per page ("Something went wrong" + Reload).

## 8. Testing

- **Vitest** (`clients/web`, jsdom):
  - `client.ts` against a fake `fetch`: bearer header, 401 → refresh → retry, concurrent 401s share one refresh, refresh 401 → `SessionExpired`, refresh 500 keeps the session, problem+json → `ApiError`, network error → status 0.
  - `strikes.ts`: one strike per trip regardless of how many signals; 3rd trip → auto-submit; face failures in a row → auto-submit, reset on a good check.
  - `attemptQueue.ts` with a fake sender: order, evidence id linked to the next event, only the latest code save kept, backoff, submit cuts backoff, 409 stops and clears, 400 drops the item.
  - Timer maths with a server offset; navigation items per capability set; student detection.
- **Playwright** (Chromium with `--use-fake-device-for-media-stream --use-fake-ui-for-media-stream`) against the API started on a test database (MySQL container via Docker, seeded by the `seed` command). The fake camera shows no real face, so tests set a test-only switch (`window.__secucodeFakeFaces`, honoured only in `import.meta.env.DEV` builds) to control the face count:
  1. Staff signs in, creates a `WebAllowed` exam, sees it in the list; edits and deactivates it.
  2. Student signs in, `DesktopOnly` exam shows no Start; starts a `WebAllowed` exam with the PIN, runs Python code, submits; staff opens the attempt and sees the code, events and `ReferenceFace`/`Webcam` evidence.
  3. Reload mid-exam → Resume restores the code.
  4. Leaving fullscreen 3 times auto-submits.
  5. Light and dark screenshots of sign-in, dashboard, exam page, exam list, editor, attempt detail (saved as artifacts, checked by eye).
- **API tests:** `audio/webm` evidence accepted; static fallback serves `index.html` for `/exams` but not for `/api/unknown` (404 JSON); CSP header present on `/` and absent on `/api/health`.
- **Manual:** one run in Chrome and Edge with a real webcam and microphone (face detection, permission prompts, audio playback in attempt detail).

## 9. Out of scope

Admin screens, corrections, audit view, full evidence viewer, live exam monitor (sub-projects 3–5); face matching on the web; screen capture evidence; mobile/tablet layouts; PWA/offline install; persisting the attempt queue across tab close; Firefox/Safari support (Chromium browsers only for exams; staff pages should work in any modern browser).
