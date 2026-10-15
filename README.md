# 🔐 SecuCode

SecuCode runs **proctored coding exams**. An ASP.NET Core 8 API (`server/Secucode.Api`) holds exams, attempts and
evidence; the Windows desktop app (WPF, .NET 8, `clients/Secucode.Desktop`) is its client for staff and students.
Staff create exams with coding questions; students take them in a locked-down full-screen window while their webcam,
microphone and window focus are monitored.

---

## 📌 Features

- **Staff** (faculty, HOD, super admin): list exams by date range; create and edit exams (local start/end time, duration, target department/year/batch, desktop-only or also web, ordered questions); activate/deactivate; delete exams nobody has attempted; view each exam's attempts with the student's code and proctoring events.
- **Students**: sign in → exams open now or coming up → reference face capture + PIN → exam.
- **Exam window**: one random question, a code editor, a runner for **Python, JavaScript, Java and C++** (runs on the API: 5 s run limit, 10 runs a minute), and a countdown kept in sync with the server that auto-submits at zero.
- **Proctoring** (students only):
  - Face check every 3 s with OpenCV: no face, more than one face, or a face that doesn't match the reference (LBPH) is a strike, and 3 strikes in a row auto-submit.
  - Leaving the window 3 times or minimizing it auto-submits (the focus check is off in Debug builds).
  - Alt+Tab, Alt/Ctrl+Esc, the Win keys and Alt+F4 are blocked, as are copy/cut/paste and drag-drop.
  - Microphone audio in 60 s WAV chunks, webcam frames of failed checks and a final screenshot are uploaded as evidence.
- **Offline-tolerant**: code is auto-saved every 30 s; while the connection is down, saves, evidence and the submission wait on disk and are sent in order when it returns.

## 🛠️ Tech

API: ASP.NET Core 8 · EF Core · MySQL 8 · SignalR · Docker (code sandbox). Desktop: C# / WPF (.NET 8) · Emgu CV (OpenCV) · NAudio.

---

## 🚀 Getting started

### Prerequisites

- Windows 10/11 for the desktop app, [.NET 8 SDK](https://dotnet.microsoft.com/download), Visual Studio 2022 (optional)
- For the API: MySQL 8.x on localhost (local run) or Docker Desktop (Docker Compose run)
- Students need a webcam to take an exam. A microphone is optional; a missing one is logged.
- For the API's local code runner (each is optional): `python`, `node`, `java` (JDK 11+), `g++` on `PATH`. The Docker run sandboxes code in containers instead.

---

## 🌐 Platform API (in progress)

SecuCode is becoming a client-server platform: an ASP.NET Core 8 API (`server/Secucode.Api`) that the Windows app and a
new web app will both use. Design: `docs/superpowers/specs/2026-10-06-secucode-platform-backend-design.md`.

**Run locally** (needs MySQL on localhost with root/root, and the .NET 8 SDK):

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"; $env:ASPNETCORE_URLS="http://localhost:8080"   # dev settings, desktop app's default address
dotnet run --project server/Secucode.Api -- seed   # creates database secucode_api + demo data, then exits
dotnet run --project server/Secucode.Api           # API + Swagger UI at the URL it prints, /swagger
```

Demo logins: `superadmin / Admin@123`, `hod.cse / Hod@123`, `faculty.cse / Faculty@123`, `student1 / Student@123` (PIN `1234`).

**Run with Docker** (code runs sandboxed in containers): copy `.env.example` to `.env`, set `JWT_KEY`, then
`docker compose up --build`, and seed once with `docker compose exec api dotnet Secucode.Api.dll seed`.

> **Warning:** in Production, seeding refuses to run unless `Seed__AdminPassword` (the `superadmin` password) is set in `.env`. The other demo accounts (`hod.cse`, `faculty.cse`, `student1`, `student2`) still use the published demo passwords, and there is no change-password endpoint yet: have the Super Admin reset them via `reset-password`, or deactivate them, before real use.

**Live client contract:** SignalR runs `OnConnectedAsync` (joining the user's group) after the client's `start` returns, and pushes sent while a client is disconnected are lost. After every `connected` or `reconnected`, clients must re-read `GET /attempts/{id}` (it returns `DeadlineAt` and `ServerTime`); clients should also make refresh single-flight.

**Tests** (Docker Desktop must be running; `python` and `node` on PATH):

```powershell
dotnet test server/Secucode.Api.Tests --filter "Category!=Docker"   # fast suite
dotnet test server/Secucode.Api.Tests --filter "Category=Docker"    # sandbox tests, pulls images on first run
```

**Run the desktop app** (Windows): start the API first (above), then

```powershell
dotnet run --project clients/Secucode.Desktop        # Debug: focus-loss checks are off
dotnet run --project clients/Secucode.Desktop -c Release
```

Sign in with a demo account. The server address (default `http://localhost:8080`) is under **Server settings** on the sign-in screen and is saved in `%LocalAppData%\Secucode\settings.json`. During an exam, unsent work is kept in `%LocalAppData%\Secucode\outbox\` and sent when the connection returns.

**Desktop tests** (Docker Desktop running; `python` on PATH): `dotnet test clients/Secucode.Desktop.Tests`. Hardware checks (webcam, microphone, screenshot): `dotnet run --project clients/Secucode.Desktop.Checks`.

---

## 🗂️ Project layout

```
clients/Secucode.Desktop/        WPF app, a client of the API
  Api/                           ApiClient (tokens, refresh, problem+json), LiveConnection (SignalR)
  Outbox/                        AttemptOutbox: disk-backed, ordered exam writes
  Theme/                         hand-built Fluent-style theme (tokens, controls, motion, InfoBar)
  Shell/                         main window: title bar, sidebar navigation
  Proctoring/                    webcam, microphone, keyboard lockdown, screenshots
  Features/Login, Settings, Student (exams, start, ExamWindow), Staff (exams, editor, attempts)
clients/Secucode.Desktop.Tests/  client tests against the real API
clients/Secucode.Desktop.Checks/ hardware smoke checks
clients/Secucode_installer/      Visual Studio Installer project (.vdproj)
project diagrams/, project ss/
```

---

## ⚠️ Known limitations

- The API's local runner (`Runner:Mode = Local`, the default outside Docker) runs student code **unsandboxed** on the server. Use the Docker Compose run (`Runner__Mode: Docker`) for real exams.
- The face-match threshold (`MatchMaxDistance` in `clients/Secucode.Desktop/Proctoring/FaceMonitor.cs`) needs calibrating on real webcams and lighting.
- Ctrl+Alt+Del can't be blocked by any app. Leaving the window that way is caught by the focus check instead.

## 💡 Future improvements

- Automatic grading with test cases
- Web app for students and staff
- User management UI

## 👨‍🏫 Author

Pratham Mewada – [LinkedIn](https://www.linkedin.com/in/pratham-mewada/)
