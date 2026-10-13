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

### 1. Database

```powershell
# Drops and recreates the `secucode` database with schema + sample data
Get-Content Database\setup.sql -Raw | & "C:\Program Files\MySQL\MySQL Server 8.4\bin\mysql.exe" -u root -p
```

The app connects with `Server=localhost;Database=secucode;User Id=root;Password=root;`.
To use something else, set the `SECUCODE_DB` environment variable to a full MySQL connection string.

**Sample accounts** (from `setup.sql`):

| Role    | Username | Password   | Batch | PIN  |
|---------|----------|------------|-------|------|
| Teacher | teacher1 | teacher123 |       |      |
| Student | student1 | student123 | A1    | 1234 |

The sample exam is dated the day you run `setup.sql`, so it can be taken right away.

**Adding users.** Passwords and PINs are bcrypt hashes, so use the script rather than raw SQL. It needs `dotnet build` to have run once so that BCrypt is in the NuGet cache.

```powershell
.\Database\add-user.ps1 -Username alice -Password secret -Role Student -Pin 4321 -Branch "Computer Engineering" -Class TE -Batch A1
.\Database\add-user.ps1 -Username bob -Password secret -Role Teacher
```

### 2. Build & run

```powershell
dotnet run --project clients/Secucode.Desktop
```

or open `Secucode.sln` in Visual Studio and run the `Secucode` project.

> In **Debug** builds the "switched away from the window" check is disabled, so you can use the debugger.
> The keyboard lockdown is still active while an exam window is open as a student.

### 3. Smoke checks

```powershell
dotnet run --project clients/Secucode.Desktop.Checks
```

This checks the code runner (output, stdin, timeouts, errors), the native face-recognition module, and the database schema.

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

---

## 🗂️ Project layout

```
Database/
  setup.sql                   schema + seed data
  add-user.ps1                create users with hashed password/PIN
clients/Secucode.Desktop/      the WPF app
  Db.cs                       connection string (SECUCODE_DB override)
  CodeExecutor.cs             runs student code as a child process in a temp folder
  MainWindow                  choose Teacher / Student
  TeacherLoginWindow → TeacherMainWindow → TeacherTestsPage → TeacherExamFormWindow / TestDetailsPage
  StudentLoginWindow → StudentDashboard → PinVerificationWindow → StudentTestDetailsPage → TestPage
  TestPage                    exam window: runner, timer, proctoring, submission
  Exam.cs / Test.cs / User.cs models
Secucode.Checks/              console smoke checks
Secucode_installer/           Visual Studio Installer project (.vdproj)
project diagrams/, project ss/
```

### Database

`branches`, `classes`, `batches` · `users` (role Student/Teacher, bcrypt password + PIN, branch/class/batch) ·
`examinations` (date, time limit, target branch/class/batch, `created_by` → users.id, `is_active`) ·
`questions` · `submissions` (code, language, reason, question; unique per exam+student).

### Files written at runtime (next to the exe)

```
CapturedFaces/student_{id}.png                 reference face
TempCodes/temp_{student}_{exam}.txt            30 s auto-save
SubmittedCodes/Exam_{id}_{name}/Student_{id}_{name}/
  Submissions/   code file, reason.txt, final_face.jpg, desktop_screenshot.jpg, audio_*.wav
  Malpractice/   missing_face_*.jpg, multiple_faces_*.jpg, unauthorized_face_*.jpg
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
