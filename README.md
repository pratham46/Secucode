# 🔐 SecuCode

SecuCode is a Windows desktop app (WPF, .NET 8) for running **proctored coding exams**.
Teachers create exams with coding questions; students take them in a locked-down full-screen window
while their webcam, microphone and window focus are monitored.

---

## 📌 Features

- **Teacher dashboard**: create, edit, delete and activate/deactivate exams for a branch/class/batch; preview an exam; open the submission evidence folder.
- **Student flow**: login → upcoming exams → PIN check → reference face capture → exam.
- **Exam window**: one random question, a code editor, a built-in runner for **Python, JavaScript, Java and C++** (5 s run limit), and a countdown that auto-submits at zero.
- **Proctoring** (students only):
  - Face check every 3 s with OpenCV: no face, more than one face, or a face that doesn't match the reference (LBPH) is a strike, and 3 strikes in a row auto-submit.
  - Leaving the window 3 times or minimizing it auto-submits.
  - Alt+Tab, Alt/Ctrl+Esc, the Win keys and Alt+F4 are blocked, as are copy/cut/paste and drag-drop.
  - Microphone audio is recorded in 3-minute WAV chunks, and code is auto-saved every 30 s.
- **Submissions**: stored in the `submissions` table (one per student per exam, so no retakes). Evidence files go to disk (see below).

## 🛠️ Tech

C# / WPF (.NET 8) · MySQL 8 · Emgu CV (OpenCV) · NAudio · BCrypt.Net

---

## 🚀 Getting started

### Prerequisites

- Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download), Visual Studio 2022 (optional)
- MySQL 8.x running locally
- A webcam (required to take an exam as a student). A microphone is optional; a missing one is logged.
- Language runtimes on `PATH` for the code runner (each is optional, and a missing one shows a clear message):
  `python`, `node`, `java` (JDK 11+), `g++` (e.g. MinGW-w64)

---

## 🌐 Platform API (in progress)

SecuCode is becoming a client-server platform: an ASP.NET Core 8 API (`server/Secucode.Api`) that the Windows app and a
new web app will both use. Design: `docs/superpowers/specs/2026-10-06-secucode-platform-backend-design.md`.

**Run locally** (needs MySQL on localhost with root/root, and the .NET 8 SDK):

```powershell
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

- Student code runs **unsandboxed** with the app's privileges. Isolate it before using this for high-stakes exams.
- The face-match threshold (`FaceMatchMaxDistance` in `TestPage.xaml.cs`) needs calibrating on real webcams and lighting.
- Evidence files (images, audio) stay on the exam PC. Only the code and the reason go to the database.
- Ctrl+Alt+Del can't be blocked by any app. Leaving the window that way is caught by the focus check instead.

## 💡 Future improvements

- Sandboxed code execution, automatic grading with test cases
- Teacher view of submissions from the database; uploading evidence to central storage
- User management UI

## 👨‍🏫 Author

Pratham Mewada – [LinkedIn](https://www.linkedin.com/in/pratham-mewada/)
