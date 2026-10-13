# SecuCode Desktop Client on the API (Sub-project 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the WPF app (`clients/Secucode.Desktop`) as a thin client of the SecuCode API with a hand-built Fluent-style UI foundation (theme tokens, sidebar shell, transitions, light/dark/system), keeping proctoring local and surviving network loss through a disk-backed outbox.

**Architecture:** The WPF app references `server/Secucode.Contracts` for every request/response type. One `ApiClient` (HttpClient + in-memory tokens + single-flight refresh) is the only way to the server; `LiveConnection` wraps the SignalR hub. Exam writes (code saves, events, evidence, submit) go through `AttemptOutbox`, a per-attempt FIFO persisted under `%LocalAppData%\Secucode\outbox`. Exam rules (strikes, submit-once, live deadline changes) live in a UI-free `ExamSession`; proctoring hardware (webcam, mic, keyboard hook, screenshots) lives in UI-free classes under `Proctoring/`. Screens are UserControls hosted in a `ShellWindow`; view models are plain classes (pages set `DataContext` once after loading, so no change notification is needed). A new `Secucode.Desktop.Tests` project tests the client against the real API using sub-project 0's `ApiFixture`.

**Tech Stack:** .NET 8 WPF (`net8.0-windows`), `Microsoft.AspNetCore.SignalR.Client 8.0.11`, Emgu.CV 4.10.0.5680, NAudio 2.2.1, xUnit 2.9.2, `Microsoft.AspNetCore.Mvc.Testing 8.0.11`, Testcontainers (via `Secucode.Api.Tests`).

**Spec:** `docs/superpowers/specs/2026-10-13-secucode-desktop-client-design.md`. Read it before starting; "spec §N" below refers to it. The API it talks to is described in `docs/superpowers/specs/2026-10-06-secucode-platform-backend-design.md` §8–9.

## Before Task 1

Work happens on branch `feature/desktop-client` (already created; it holds the spec commits). Start Docker Desktop before running any tests (the API test fixture starts a MySQL container). `python` must be on PATH (the `/run` test uses the API's Local runner).

## Global Constraints

- Desktop and its tests target `net8.0-windows` with `UseWPF`; server projects stay `net8.0`. No .NET SDK upgrade.
- Package versions: `Microsoft.AspNetCore.SignalR.Client 8.0.11`, `Microsoft.AspNetCore.Mvc.Testing 8.0.11`, `Microsoft.NET.Test.Sdk 17.11.1`, `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`. Keep `Emgu.CV*` `4.10.0.5680` and `NAudio 2.2.1`. **No UI library** (hand-built theme — user decision).
- New desktop code uses namespaces under `Secucode.Desktop` (e.g. `Secucode.Desktop.Api`). The assembly name stays `Secucode` (pack URIs are `/Secucode;component/...`).
- JSON always uses `Secucode.Contracts.Json.Options`. All API timestamps are UTC; convert to local only for display and from local only in the exam editor.
- The client sends `client = Desktop` at login. Tokens live only in memory (no persisted login).
- Default API address `http://localhost:8080/` (Docker Compose port); configurable on the sign-in screen and saved in `%LocalAppData%\Secucode\settings.json`.
- Limits copied from the specs: auto-save every 30 s; face check every 3 s; 3 consecutive face failures or 3 focus misses auto-submit; minimizing auto-submits; audio in 60 s WAV chunks (16 kHz mono); evidence ≤ 10 MB (jpeg/png/wav); submit accepted until `deadline + 30 s`; outbox backoff 1 s doubling to 30 s; `/run` limited to 10 runs/min (429).
- Languages offered: `Python`, `JavaScript`, `Java`, `C++` (the API runner's keys).
- Brushes are referenced only through theme tokens with `DynamicResource`; durations and easing only from `Theme/Motion.xaml`. Animations are skipped when `SystemParameters.ClientAreaAnimation` is false.
- UI text: sentence case, plain words, errors show the server's `detail`.
- Commits: one per task (plus fix commits), prefix `feat(desktop):` / `test(desktop):` / `chore(desktop):` / `feat(api):`. End every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Desktop tests that need the API use `[Collection(DesktopApiCollection.Name)]` and create uniquely named data with `World`, exactly like the API tests.

## Review Focus

1. **The student's PC clock is several minutes wrong.** The countdown must follow server time (`ServerTime` offset), not the local clock. Pinned in Task 5 (`ExamClockTests.A_fast_pc_clock_does_not_shorten_the_exam`).
2. **The app is killed while offline with writes still queued.** On restart/resume the leftover queue must be sent first, in order, nothing lost or duplicated. Pinned in Task 6 (`Queue_survives_a_restart_and_sends_in_order`, `Evidence_is_uploaded_once_when_the_event_post_fails`).
3. **Hindi/Marathi text and emoji in student code** (`print('नमस्ते 🙂')`) must survive the outbox's disk files and the API byte-exact. Pinned in Task 6 (restart test uses it) and Task 4 (`Code_round_trips_unicode`).
4. **Staff type exam times in local time (IST).** The editor must send the equivalent UTC instant and show it back in local time. Pinned in Task 13 (`Local_times_become_utc_and_back`).
5. **A student's session is revoked mid-exam** (password reset by staff). The exam must not be auto-submitted; the attempt stays resumable after signing in again. Pinned in Task 10 (`Abandon_never_submits`) and Task 3 (`A_revoked_session_raises_SessionExpired_and_signs_out`).

## File Map

```
Secucode.sln                                         + clients/Secucode.Desktop.Tests
server/Secucode.Contracts/AuthContracts.cs           AssignmentDto.IsStudent (Task 1)
server/Secucode.Api/Features/Users/UserMapping.cs    maps IsStudent (Task 1)
server/Secucode.Api.Tests/AuthTests.cs               + IsStudent test (Task 1)
clients/Secucode.Desktop/
  Secucode.csproj                                    + Contracts ref, SignalR client; − MySql.Data, BCrypt (Task 14)
  AssemblyInfo.cs                                    + InternalsVisibleTo("Secucode.Desktop.Tests")
  App.xaml / App.xaml.cs                             theme merge, startup, sign-in/out (Task 8)
  AppSettings.cs                                     settings.json (Task 2)
  Session.cs                                         who is signed in + capability checks (Task 5)
  Api/ApiError.cs  Api/ApiClient.cs  Api/ApiClient.Endpoints.cs  Api/LiveConnection.cs   (Tasks 2–4)
  Outbox/AttemptOutbox.cs  Outbox/OutboxItem.cs  Outbox/ApiAttemptSender.cs                (Task 6)
  Theme/Colors.Light.xaml  Theme/Colors.Dark.xaml  Theme/Typography.xaml  Theme/Motion.xaml
  Theme/Controls.xaml  Theme/ThemeManager.cs  Theme/Transitions.cs  Theme/InfoBar.xaml(.cs) (Task 7)
  Shell/TitleBar.xaml(.cs)  Shell/ShellWindow.xaml(.cs)  Shell/NavItem.cs                (Task 8)
  Features/Login/LoginWindow.xaml(.cs)                                                    (Task 8)
  Features/Settings/SettingsPage.xaml(.cs)                                                (Task 8)
  Proctoring/FaceMonitor.cs  Proctoring/FaceReference.cs  Proctoring/AudioRecorder.cs
  Proctoring/Lockdown.cs  Proctoring/ScreenCapture.cs  Proctoring/Images.cs              (Task 9)
  Features/Student/ExamClock.cs (Task 5)  ExamSession.cs  ExamWindow.xaml(.cs)            (Task 10)
  Features/Student/StudentExamRow.cs  StudentExamsPage.xaml(.cs)  ExamStartViewModel.cs  ExamStartPage.xaml(.cs) (Task 11)
  Features/Staff/StaffExamsViewModel.cs  StaffExamsPage.xaml(.cs)  AttemptsViewModel.cs  AttemptsPage.xaml(.cs) (Task 12)
  Features/Staff/ExamEditorViewModel.cs  ExamEditorPage.xaml(.cs)                        (Task 13)
  (deleted in Task 8: MainWindow, Student*/Teacher* windows and pages, PinVerificationWindow, TestPage,
   TestDetailsPage, Db.cs, Exam.cs, Test.cs, User.cs, CodeExecutor.cs, converters, login view models)
clients/Secucode.Desktop.Checks/Program.cs           hardware-only checks (Task 9)
clients/Secucode.Desktop.Tests/
  Secucode.Desktop.Tests.csproj  DesktopApiCollection.cs  HarnessTests.cs (Task 1)
  AppSettingsTests.cs  ApiClientAuthTests.cs (Task 2)  ApiClientRefreshTests.cs  CountingHandler.cs (Task 3)
  ApiClientExamTests.cs  LiveConnectionTests.cs (Task 4)  SessionTests.cs  ExamClockTests.cs (Task 5)
  AttemptOutboxTests.cs  FakeSender.cs (Task 6)  ThemeTests.cs  Sta.cs (Task 7)
  ExamSessionTests.cs (Task 10)  StudentExamRowTests.cs  ExamStartViewModelTests.cs (Task 11)
  StaffExamsViewModelTests.cs  AttemptsViewModelTests.cs (Task 12)  ExamEditorViewModelTests.cs (Task 13)
Database/                                            deleted (Task 14)
README.md                                            desktop section rewritten (Task 14)
```

---

### Task 1: `IsStudent` on assignments, desktop wiring, desktop test harness

**Files:**
- Modify: `server/Secucode.Contracts/AuthContracts.cs` (record `AssignmentDto`)
- Modify: `server/Secucode.Api/Features/Users/UserMapping.cs`
- Modify: `server/Secucode.Api.Tests/AuthTests.cs` (add one test)
- Modify: `clients/Secucode.Desktop/Secucode.csproj`, `clients/Secucode.Desktop/AssemblyInfo.cs`
- Create: `clients/Secucode.Desktop.Tests/Secucode.Desktop.Tests.csproj`, `DesktopApiCollection.cs`, `HarnessTests.cs`
- Modify: `Secucode.sln`

**Interfaces:**
- Produces: `AssignmentDto(int Id, int RoleId, string RoleName, int? BranchId, int? ClassId, int? BatchId, DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive, bool IsStudent)`; xUnit collection `DesktopApiCollection.Name` (`"desktop-api"`) sharing one `ApiFixture`.

- [ ] **Step 1: Write the failing API test** — append to `server/Secucode.Api.Tests/AuthTests.cs` inside the class:

```csharp
    [Fact]
    public async Task Me_marks_student_assignments()
    {
        var s = await _world.SectionAsync();
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var faculty = await _world.UserAsync(RoleNames.Faculty, s.Department);

        var studentMe = await (await (await _world.LoginAsync(student)).GetAsync("/api/auth/me")).ReadAsync<MeResponse>();
        var facultyMe = await (await (await _world.LoginAsync(faculty)).GetAsync("/api/auth/me")).ReadAsync<MeResponse>();

        Assert.True(Assert.Single(studentMe.User.Assignments).IsStudent);
        Assert.False(Assert.Single(facultyMe.User.Assignments).IsStudent);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test server/Secucode.Api.Tests --filter "FullyQualifiedName~Me_marks_student_assignments"`
Expected: build error — `AssignmentDto` does not contain a definition for `IsStudent`.

- [ ] **Step 3: Add the field and map it**

`server/Secucode.Contracts/AuthContracts.cs` — replace the `AssignmentDto` record:

```csharp
public sealed record AssignmentDto(int Id, int RoleId, string RoleName, int? BranchId, int? ClassId, int? BatchId,
    DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive, bool IsStudent);
```

`server/Secucode.Api/Features/Users/UserMapping.cs` — replace the first `ToDto`:

```csharp
    public static AssignmentDto ToDto(this RoleAssignment a, DateOnly today) =>
        new(a.Id, a.RoleId, a.Role.Name, a.BranchId, a.ClassId, a.BatchId, a.ValidFrom, a.ValidTo, a.IsActiveOn(today), a.Role.IsStudent);
```

- [ ] **Step 4: Run the whole API suite** (the record is positional; any other construction site would fail to compile)

Run: `dotnet test server/Secucode.Api.Tests --filter "Category!=Docker"`
Expected: PASS, including `Me_marks_student_assignments`.

- [ ] **Step 5: Wire the desktop project to Contracts and SignalR**

In `clients/Secucode.Desktop/Secucode.csproj`, add to the package `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.11" />
```

and add a new `ItemGroup`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\server\Secucode.Contracts\Secucode.Contracts.csproj" />
  </ItemGroup>
```

Append to `clients/Secucode.Desktop/AssemblyInfo.cs`:

```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Secucode.Desktop.Tests")]
```

- [ ] **Step 6: Create the test project**

`clients/Secucode.Desktop.Tests/Secucode.Desktop.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <!-- Secucode.Api is referenced directly so Mvc.Testing generates the content-root attribute WebApplicationFactory needs. -->
    <ProjectReference Include="..\Secucode.Desktop\Secucode.csproj" />
    <ProjectReference Include="..\..\server\Secucode.Api\Secucode.Api.csproj" />
    <ProjectReference Include="..\..\server\Secucode.Api.Tests\Secucode.Api.Tests.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

`clients/Secucode.Desktop.Tests/DesktopApiCollection.cs`:

```csharp
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Desktop.Tests;

// xUnit only discovers collection definitions in the test assembly itself, so the API fixture is re-declared here.
[CollectionDefinition(Name)]
public sealed class DesktopApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "desktop-api";
}
```

`clients/Secucode.Desktop.Tests/HarnessTests.cs`:

```csharp
using System.Net;
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class HarnessTests(ApiFixture api)
{
    [Fact]
    public async Task The_api_boots_for_desktop_tests()
    {
        var body = await (await api.Factory.CreateClient().GetAsync("/api/health")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }
}
```

Add it to the solution: `dotnet sln Secucode.sln add clients/Secucode.Desktop.Tests/Secucode.Desktop.Tests.csproj`

- [ ] **Step 7: Run the harness test**

Run: `dotnet test clients/Secucode.Desktop.Tests`
Expected: PASS (1 test). If it fails with "content root ... could not be found", confirm the direct `Secucode.Api` project reference is present.

- [ ] **Step 8: Commit**

```bash
git add server clients/Secucode.Desktop/Secucode.csproj clients/Secucode.Desktop/AssemblyInfo.cs clients/Secucode.Desktop.Tests Secucode.sln
git commit -m "feat(api): mark student assignments; desktop test harness on the real API

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Settings, `ApiError` and the `ApiClient` core (login, me, logout)

**Files:**
- Create: `clients/Secucode.Desktop/AppSettings.cs`, `clients/Secucode.Desktop/Api/ApiError.cs`, `clients/Secucode.Desktop/Api/ApiClient.cs`
- Test: `clients/Secucode.Desktop.Tests/AppSettingsTests.cs`, `clients/Secucode.Desktop.Tests/ApiClientAuthTests.cs`

**Interfaces:**
- Consumes: Contracts `LoginRequest`, `LoginResponse`, `RefreshRequest`, `MeResponse`, `ClientKind`, `Json.Options`.
- Produces:
  - `enum ThemeChoice { System, Light, Dark }`; `class AppSettings { string ApiBaseUrl; ThemeChoice Theme; Uri BaseUri; static string DataDir {get;set;}; static AppSettings Load(); void Save(); }` (namespace `Secucode.Desktop`).
  - `class ApiError : Exception { int Status; string Title; string? Detail; IReadOnlyDictionary<string,string[]> Errors; bool IsNetwork; string Display; static ApiError Network(Exception); static Task<ApiError> FromResponseAsync(HttpResponseMessage); }`
  - `partial class ApiClient(HttpClient http) { HttpClient Http; MeResponse? Me; bool IsSignedIn; Task<MeResponse> LoginAsync(string, string, CancellationToken = default); Task LogoutAsync(); internal Task<T> SendAsync<T>(HttpMethod, string path, object? body, CancellationToken, bool authenticated = true); internal Task<T> SendAsync<T>(Func<HttpRequestMessage>, CancellationToken); internal Task<HttpResponseMessage> SendAsync(HttpMethod, string, object?, CancellationToken, bool authenticated = true); internal Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage>, CancellationToken, bool authenticated = true); }` (namespace `Secucode.Desktop.Api`). Paths are relative without a leading slash (`"api/auth/me"`); `HttpClient.BaseAddress` must end with `/`.

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/AppSettingsTests.cs`:

```csharp
namespace Secucode.Desktop.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _original = AppSettings.DataDir;
    private readonly string _dir = Directory.CreateTempSubdirectory("secucode_settings_").FullName;

    public AppSettingsTests() => AppSettings.DataDir = _dir;

    public void Dispose()
    {
        AppSettings.DataDir = _original;
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_or_broken_file_gives_defaults()
    {
        Assert.Equal("http://localhost:8080/", AppSettings.Load().ApiBaseUrl);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        Assert.Equal(ThemeChoice.System, AppSettings.Load().Theme);
    }

    [Fact]
    public void Saved_settings_load_back_and_the_base_uri_ends_with_a_slash()
    {
        new AppSettings { ApiBaseUrl = "http://exam-server:9000", Theme = ThemeChoice.Dark }.Save();

        var loaded = AppSettings.Load();

        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.Equal(new Uri("http://exam-server:9000/"), loaded.BaseUri);
    }
}
```

`clients/Secucode.Desktop.Tests/ApiClientAuthTests.cs`:

```csharp
using System.Net;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class ApiClientAuthTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private ApiClient NewClient() => new(api.Factory.CreateClient());

    [Fact]
    public async Task Login_signs_in_as_desktop_and_loads_me()
    {
        var s = await _world.SectionAsync();
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var client = NewClient();

        var me = await client.LoginAsync(student.Username, World.Password);

        Assert.True(client.IsSignedIn);
        Assert.Equal(student.Id, me.User.Id);
        Assert.True(Assert.Single(me.User.Assignments).IsStudent);
        Assert.Same(me, client.Me);
    }

    [Fact]
    public async Task Wrong_password_is_an_ApiError_carrying_the_servers_problem()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var client = NewClient();

        var e = await Assert.ThrowsAsync<ApiError>(() => client.LoginAsync(user.Username, "wrong"));

        Assert.Equal(401, e.Status);
        Assert.Equal("Invalid username or password.", e.Detail);
        Assert.Equal("Invalid username or password.", e.Display);
        Assert.False(client.IsSignedIn);
    }

    [Fact]
    public async Task Unreachable_server_is_a_network_error()
    {
        var client = new ApiClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9/") });

        var e = await Assert.ThrowsAsync<ApiError>(() => client.LoginAsync("x", "y"));

        Assert.True(e.IsNetwork);
        Assert.Equal(0, e.Status);
        Assert.StartsWith("Can't reach the server", e.Display);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_signs_out()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var client = NewClient();
        await client.LoginAsync(user.Username, World.Password);
        var refresh = client.RefreshToken!;

        await client.LogoutAsync();

        Assert.False(client.IsSignedIn);
        Assert.Null(client.Me);
        var res = await api.Factory.CreateClient().PostJsonAsync("/api/auth/refresh", new RefreshRequest(refresh));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Validation_problems_expose_field_errors()
    {
        var client = NewClient();

        var e = await Assert.ThrowsAsync<ApiError>(() => client.LoginAsync("", ""));

        Assert.Equal(400, e.Status);
        Assert.NotEmpty(e.Errors);
        Assert.Equal(string.Join("\n", e.Errors.SelectMany(x => x.Value)), e.Display);
    }
}
```

`RefreshToken` is an `internal` property (visible to tests through `InternalsVisibleTo`).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~AppSettingsTests|FullyQualifiedName~ApiClientAuthTests"`
Expected: build errors — `AppSettings`, `ApiClient`, `ApiError` not found.

- [ ] **Step 3: Implement `AppSettings`**

`clients/Secucode.Desktop/AppSettings.cs`:

```csharp
using System.IO;
using System.Text.Json;
using Secucode.Contracts;

namespace Secucode.Desktop;

public enum ThemeChoice { System, Light, Dark }

// Per-user settings in %LocalAppData%\Secucode\settings.json. A missing or broken file means defaults.
public sealed class AppSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:8080/";
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    public Uri BaseUri => new(ApiBaseUrl.EndsWith('/') ? ApiBaseUrl : ApiBaseUrl + "/");

    // Settable so tests can point it at a temp folder.
    public static string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Secucode");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json.Options) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json.Options));
    }
}
```

- [ ] **Step 4: Implement `ApiError`**

`clients/Secucode.Desktop/Api/ApiError.cs`:

```csharp
using System.Net.Http;
using System.Text.Json;

namespace Secucode.Desktop.Api;

// Every failed API call surfaces as this: the server's problem+json, or Status 0 when the server can't be reached.
public sealed class ApiError(int status, string title, string? detail = null, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(detail ?? title)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
    public bool IsNetwork => Status == 0;

    // What a screen shows in its InfoBar.
    public string Display =>
        IsNetwork ? $"{Title}. Check the server address and that the SecuCode server is running."
        : Errors.Count > 0 ? string.Join("\n", Errors.SelectMany(e => e.Value))
        : Detail ?? Title;

    public static ApiError Network(Exception inner) => new(0, "Can't reach the server", inner.Message);

    public static async Task<ApiError> FromResponseAsync(HttpResponseMessage res)
    {
        var status = (int)res.StatusCode;
        var fallback = res.ReasonPhrase ?? $"HTTP {status}";
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            string? Str(string name) =>
                root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            Dictionary<string, string[]>? errors = null;
            if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                errors = e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Array
                    ? p.Value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray()
                    : [p.Value.ToString()]);

            return new ApiError(status, Str("title") ?? fallback, Str("detail"), errors);
        }
        catch (JsonException)
        {
            return new ApiError(status, fallback);
        }
    }
}
```

- [ ] **Step 5: Implement the `ApiClient` core**

`clients/Secucode.Desktop/Api/ApiClient.cs`:

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Secucode.Contracts;

namespace Secucode.Desktop.Api;

// The app's only door to the server. Tokens live in memory; every failure becomes an ApiError.
public sealed partial class ApiClient(HttpClient http)
{
    private readonly object _gate = new();
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _expiresAt;

    public HttpClient Http => http;
    public MeResponse? Me { get; private set; }
    public bool IsSignedIn { get { lock (_gate) return _accessToken is not null; } }

    internal string? AccessToken { get { lock (_gate) return _accessToken; } set { lock (_gate) _accessToken = value; } }
    internal string? RefreshToken { get { lock (_gate) return _refreshToken; } set { lock (_gate) _refreshToken = value; } }
    internal DateTime ExpiresAt { get { lock (_gate) return _expiresAt; } set { lock (_gate) _expiresAt = value; } }

    public async Task<MeResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var login = await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login",
            new LoginRequest(username, password, ClientKind.Desktop), ct, authenticated: false);
        SetTokens(login);
        Me = await SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, ct);
        return Me;
    }

    public async Task LogoutAsync()
    {
        var refresh = RefreshToken;
        try
        {
            if (refresh is not null)
                using (await SendAsync(HttpMethod.Post, "api/auth/logout", new RefreshRequest(refresh), default)) { }
        }
        catch (ApiError) { } // signing out locally matters more than telling the server
        finally { ClearTokens(); }
    }

    private void SetTokens(LoginResponse r)
    {
        lock (_gate) { _accessToken = r.AccessToken; _refreshToken = r.RefreshToken; _expiresAt = r.ExpiresAt; }
    }

    private void ClearTokens()
    {
        lock (_gate) { _accessToken = null; _refreshToken = null; }
        Me = null;
    }

    // ---- transport ----

    internal async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct, bool authenticated = true)
    {
        using var res = await SendAsync(method, path, body, ct, authenticated);
        return (await res.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    internal async Task<T> SendAsync<T>(Func<HttpRequestMessage> build, CancellationToken ct)
    {
        using var res = await SendAsync(build, ct);
        return (await res.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    internal Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct, bool authenticated = true) =>
        SendAsync(() =>
        {
            var req = new HttpRequestMessage(method, path);
            if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: Json.Options);
            return req;
        }, ct, authenticated);

    // `build` makes a fresh request each time: a request message can be sent only once, and a retry needs another.
    internal async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct, bool authenticated = true)
    {
        var res = await SendOnceAsync(build, authenticated, ct);
        if (!res.IsSuccessStatusCode)
        {
            using (res) throw await ApiError.FromResponseAsync(res);
        }
        return res;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> build, bool authenticated, CancellationToken ct)
    {
        using var req = build();
        if (authenticated && AccessToken is { } token)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            return await http.SendAsync(req, ct);
        }
        catch (HttpRequestException e) { throw ApiError.Network(e); }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested) { throw ApiError.Network(e); } // HttpClient timeout
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~AppSettingsTests|FullyQualifiedName~ApiClientAuthTests"`
Expected: PASS (7 tests). (Empty credentials are a 400 with `username`/`password` errors — see `AuthEndpoints` login validation.)

- [ ] **Step 7: Commit**

```bash
git add clients/Secucode.Desktop/AppSettings.cs clients/Secucode.Desktop/Api clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): settings file and API client core with problem+json errors

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Token refresh — retry on 401, single-flight refresh, session expiry

**Files:**
- Modify: `clients/Secucode.Desktop/Api/ApiClient.cs`
- Create: `clients/Secucode.Desktop.Tests/CountingHandler.cs`
- Test: `clients/Secucode.Desktop.Tests/ApiClientRefreshTests.cs`

**Interfaces:**
- Consumes: Task 2's `ApiClient` internals.
- Produces: `event EventHandler? SessionExpired` (raised once tokens are cleared because refresh was refused); `Task<string?> FreshAccessTokenAsync()` (refreshes when the token expires within 1 minute; used by SignalR). After `SessionExpired`, the failing call throws `ApiError(401, "Session expired", "Your session expired. Please sign in again.")`. Network failure during refresh propagates as a network `ApiError` and does **not** sign out.

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/CountingHandler.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net.Http;

namespace Secucode.Desktop.Tests;

// Wraps the test server's handler and records every request path.
public sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly ConcurrentQueue<string> _paths = new();

    public int Count(string path) => _paths.Count(p => p == path);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _paths.Enqueue(request.RequestUri!.AbsolutePath);
        return base.SendAsync(request, ct);
    }
}
```

`clients/Secucode.Desktop.Tests/ApiClientRefreshTests.cs`:

```csharp
using System.Net.Http;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class ApiClientRefreshTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private (ApiClient Client, CountingHandler Counter) NewClient()
    {
        var counter = new CountingHandler(api.Factory.Server.CreateHandler());
        return (new ApiClient(new HttpClient(counter) { BaseAddress = api.Factory.Server.BaseAddress }), counter);
    }

    private async Task<(ApiClient, CountingHandler, TestUser)> SignedInAsync()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var (client, counter) = NewClient();
        await client.LoginAsync(user.Username, World.Password);
        return (client, counter, user);
    }

    [Fact]
    public async Task Concurrent_401s_share_one_refresh_and_every_call_is_retried()
    {
        var (client, counter, user) = await SignedInAsync();
        client.AccessToken = "not-a-valid-token";

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, default)));

        Assert.All(results, r => Assert.Equal(user.Id, r.User.Id));
        Assert.Equal(1, counter.Count("/api/auth/refresh"));
        Assert.True(client.IsSignedIn);
    }

    [Fact]
    public async Task A_revoked_session_raises_SessionExpired_and_signs_out()
    {
        var (client, _, _) = await SignedInAsync();
        var expired = 0;
        client.SessionExpired += (_, _) => expired++;
        client.AccessToken = "not-a-valid-token";
        client.RefreshToken = "revoked-or-unknown";

        var e = await Assert.ThrowsAsync<ApiError>(() => client.SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, default));

        Assert.Equal(401, e.Status);
        Assert.Equal(1, expired);
        Assert.False(client.IsSignedIn);
    }

    [Fact]
    public async Task FreshAccessTokenAsync_refreshes_only_near_expiry()
    {
        var (client, counter, _) = await SignedInAsync();

        Assert.Equal(client.AccessToken, await client.FreshAccessTokenAsync());
        Assert.Equal(0, counter.Count("/api/auth/refresh"));

        client.ExpiresAt = DateTime.UtcNow.AddSeconds(30);
        var token = await client.FreshAccessTokenAsync();

        Assert.Equal(1, counter.Count("/api/auth/refresh"));
        Assert.Equal(client.AccessToken, token);
        Assert.True(client.ExpiresAt > DateTime.UtcNow.AddMinutes(5));
    }

    [Fact]
    public async Task Calls_without_a_session_are_not_refreshed()
    {
        var (client, counter) = NewClient();

        var e = await Assert.ThrowsAsync<ApiError>(() => client.SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, default));

        Assert.Equal(401, e.Status);
        Assert.Equal(0, counter.Count("/api/auth/refresh"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ApiClientRefreshTests"`
Expected: build errors — `SessionExpired` and `FreshAccessTokenAsync` not found.

- [ ] **Step 3: Implement refresh** — in `ApiClient.cs`, add fields/members and replace the `SendAsync(Func<HttpRequestMessage>, CancellationToken, bool)` method:

```csharp
    private Task<bool>? _refreshing;

    // Raised (on a background thread) when the server refused to refresh: the user must sign in again.
    public event EventHandler? SessionExpired;

    internal async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct, bool authenticated = true)
    {
        var usedToken = AccessToken;
        var res = await SendOnceAsync(build, authenticated, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && authenticated && usedToken is not null)
        {
            res.Dispose();
            if (!await RefreshAsync(usedToken))
            {
                ClearTokens();
                SessionExpired?.Invoke(this, EventArgs.Empty);
                throw new ApiError(401, "Session expired", "Your session expired. Please sign in again.");
            }
            res = await SendOnceAsync(build, authenticated, ct);
        }
        if (!res.IsSuccessStatusCode)
        {
            using (res) throw await ApiError.FromResponseAsync(res);
        }
        return res;
    }

    // For SignalR: a token that is valid for at least another minute. Offline, the old token is returned and the hub retries.
    public async Task<string?> FreshAccessTokenAsync()
    {
        string? token;
        DateTime expires;
        lock (_gate) { token = _accessToken; expires = _expiresAt; }
        if (token is null) return null;
        if (expires - DateTime.UtcNow > TimeSpan.FromMinutes(1)) return token;
        try { await RefreshAsync(token); }
        catch (ApiError) { }
        return AccessToken;
    }

    // Concurrent 401s share one refresh. A caller whose token was already replaced just retries with the new one.
    private Task<bool> RefreshAsync(string staleToken)
    {
        lock (_gate)
        {
            if (_accessToken != staleToken) return Task.FromResult(_accessToken is not null);
            if (_refreshToken is null) return Task.FromResult(false);
            return _refreshing ??= DoRefreshAsync(_refreshToken);
        }
    }

    private async Task<bool> DoRefreshAsync(string refreshToken)
    {
        await Task.Yield(); // makes sure _refreshing is assigned before the finally below clears it
        try
        {
            using var res = await SendOnceAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
            {
                Content = JsonContent.Create(new RefreshRequest(refreshToken), options: Json.Options),
            }, authenticated: false, default);
            if (!res.IsSuccessStatusCode) return false;
            SetTokens((await res.Content.ReadFromJsonAsync<LoginResponse>(Json.Options))!);
            return true;
        }
        finally
        {
            lock (_gate) _refreshing = null;
        }
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ApiClient"`
Expected: PASS (Task 2 and Task 3 tests).

- [ ] **Step 5: Commit**

```bash
git add clients/Secucode.Desktop/Api/ApiClient.cs clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): single-flight token refresh and session expiry

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Typed endpoints and the live connection

**Files:**
- Create: `clients/Secucode.Desktop/Api/ApiClient.Endpoints.cs`, `clients/Secucode.Desktop/Api/LiveConnection.cs`
- Test: `clients/Secucode.Desktop.Tests/ApiClientExamTests.cs`, `clients/Secucode.Desktop.Tests/LiveConnectionTests.cs`

**Interfaces:**
- Consumes: `ApiClient.SendAsync` overloads (Task 2/3); Contracts DTOs.
- Produces (all on `ApiClient`, all take `CancellationToken ct = default` last):
  - `Task<List<StudentExamDto>> StudentExamsAsync()`; `Task<List<ExamDto>> StaffExamsAsync(DateTime? fromUtc = null, DateTime? toUtc = null)`; `Task<ExamDto> ExamAsync(int id)`; `Task<ExamDto> CreateExamAsync(ExamRequest)`; `Task<ExamDto> UpdateExamAsync(int id, ExamRequest)`; `Task SetExamActiveAsync(int id, bool isActive)`; `Task DeleteExamAsync(int id)`; `Task<List<AttemptSummaryDto>> ExamAttemptsAsync(int examId)`
  - `Task<AttemptDto> StartAttemptAsync(int examId, string pin)`; `Task<AttemptDetailDto> AttemptAsync(int id)`; `Task SaveCodeAsync(int attemptId, SaveCodeRequest)`; `Task<AttemptEventDto> RecordEventAsync(int attemptId, AttemptEventRequest)`; `Task<EvidenceDto> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType)`; `Task<AttemptDto> SubmitAsync(int attemptId, SubmitRequest)`
  - `Task<RunResult> RunAsync(RunRequest)`; `Task<List<MasterDto>> MastersAsync(string kind)` with kind ∈ `"branches" | "classes" | "batches"`
  - `sealed class LiveConnection : IAsyncDisposable { LiveConnection(Uri apiBase, Func<Task<string?>> accessToken, Func<HttpMessageHandler>? handler = null, HttpTransportType transports = WebSockets|LongPolling); event Action<string, LiveAttemptMessage>? Received; event Func<Task>? Connected; Task StartAsync(CancellationToken = default); Task StartWithRetryAsync(CancellationToken); }` — `Connected` fires after the first start and after every automatic reconnect (the API README requires clients to re-read the attempt then).

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/ApiClientExamTests.cs`:

```csharp
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class ApiClientExamTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<ApiClient> SignInAsync(TestUser user)
    {
        var client = new ApiClient(api.Factory.CreateClient());
        await client.LoginAsync(user.Username, World.Password);
        return client;
    }

    private async Task<(Section S, TestUser Author, TestUser Student)> PeopleAsync()
    {
        var s = await _world.SectionAsync();
        return (s, await _world.UserAsync(RoleNames.Faculty, s.Department), await _world.UserAsync(RoleNames.Student, s.Batch));
    }

    [Fact]
    public async Task A_student_takes_a_desktop_only_exam_end_to_end()
    {
        var (s, author, studentUser) = await PeopleAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch, ExamMode.DesktopOnly);
        var student = await SignInAsync(studentUser);

        var attempt = await student.StartAttemptAsync(exam.Id, World.Pin);
        await student.SaveCodeAsync(attempt.Id, new SaveCodeRequest("print(1)", "Python"));
        var evidence = await student.UploadEvidenceAsync(attempt.Id, EvidenceKind.Webcam, [0xFF, 0xD8, 0xFF, 0xD9], "image/jpeg");
        await student.RecordEventAsync(attempt.Id, new AttemptEventRequest(AttemptEventType.FaceMissing, null, evidence.Id));
        var submitted = await student.SubmitAsync(attempt.Id, new SubmitRequest("print(2)", "Python", "Done"));

        Assert.Equal(AttemptStatus.Submitted, submitted.Status);
        var detail = await student.AttemptAsync(attempt.Id);
        Assert.Equal("print(2)", detail.Attempt.Code);
        Assert.Equal(evidence.Id, Assert.Single(detail.Events).EvidenceId);
        Assert.Equal(EvidenceKind.Webcam, Assert.Single(detail.Evidence).Kind);
    }

    [Fact]
    public async Task An_attempt_in_progress_is_offered_for_resume()
    {
        var (s, author, studentUser) = await PeopleAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var student = await SignInAsync(studentUser);

        var attempt = await student.StartAttemptAsync(exam.Id, World.Pin);

        var listed = Assert.Single(await student.StudentExamsAsync(), e => e.Id == exam.Id);
        Assert.Equal(attempt.Id, listed.InProgressAttemptId);
    }

    [Fact]
    public async Task Writes_after_submit_are_409()
    {
        var (s, author, studentUser) = await PeopleAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var student = await SignInAsync(studentUser);
        var attempt = await student.StartAttemptAsync(exam.Id, World.Pin);
        await student.SubmitAsync(attempt.Id, new SubmitRequest("x", "Python", null));

        var e = await Assert.ThrowsAsync<ApiError>(() => student.SaveCodeAsync(attempt.Id, new SaveCodeRequest("y", "Python")));

        Assert.Equal(409, e.Status);
    }

    [Fact]
    public async Task Code_round_trips_unicode()
    {
        var (s, author, studentUser) = await PeopleAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var student = await SignInAsync(studentUser);
        var attempt = await student.StartAttemptAsync(exam.Id, World.Pin);
        const string code = "print('नमस्ते 🙂')";

        await student.SaveCodeAsync(attempt.Id, new SaveCodeRequest(code, "Python"));

        Assert.Equal(code, (await student.AttemptAsync(attempt.Id)).Attempt.Code);
    }

    [Fact]
    public async Task Staff_create_list_toggle_and_see_attempts()
    {
        var (s, authorUser, studentUser) = await PeopleAsync();
        var author = await SignInAsync(authorUser);
        var start = DateTime.UtcNow.AddMinutes(-5);
        var request = new ExamRequest(World.Unique("Exam"), start, start.AddHours(2), 30, ExamMode.DesktopOnly, true,
            s.BranchId, s.ClassId, s.BatchId, true, true, true, true, [new QuestionInput(null, "Reverse a string")]);

        var created = await author.CreateExamAsync(request);
        await author.SetExamActiveAsync(created.Id, false);
        await author.SetExamActiveAsync(created.Id, true);
        var student = await SignInAsync(studentUser);
        await student.StartAttemptAsync(created.Id, World.Pin);

        Assert.Contains(await author.StaffExamsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)), e => e.Id == created.Id);
        Assert.Equal("Reverse a string", Assert.Single((await author.ExamAsync(created.Id)).Questions!).Text);
        Assert.Equal(studentUser.Id, Assert.Single(await author.ExamAttemptsAsync(created.Id)).StudentId);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiError>(() => author.DeleteExamAsync(created.Id))).Status);
    }

    [Fact]
    public async Task Invalid_exam_shows_field_errors()
    {
        var (s, authorUser, _) = await PeopleAsync();
        var author = await SignInAsync(authorUser);
        var bad = new ExamRequest("", DateTime.UtcNow, DateTime.UtcNow.AddHours(-1), 0, ExamMode.DesktopOnly, true,
            s.BranchId, null, null, true, true, true, true, []);

        var e = await Assert.ThrowsAsync<ApiError>(() => author.CreateExamAsync(bad));

        Assert.Equal(400, e.Status);
        Assert.NotEmpty(e.Errors);
    }

    [Fact]
    public async Task Run_and_masters()
    {
        var (s, authorUser, _) = await PeopleAsync();
        var author = await SignInAsync(authorUser);

        var run = await author.RunAsync(new RunRequest("Python", "print('hi')", null));

        Assert.Equal("hi", run.Stdout.Trim());
        Assert.Contains(await author.MastersAsync("branches"), b => b.Id == s.BranchId);
        Assert.Contains(await author.MastersAsync("batches"), b => b.Id == s.BatchId);
    }
}
```

`clients/Secucode.Desktop.Tests/LiveConnectionTests.cs`:

```csharp
using Microsoft.AspNetCore.Http.Connections;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class LiveConnectionTests(ApiFixture api)
{
    private readonly World _world = new(api);

    [Fact]
    public async Task A_student_hears_their_deadline_change()
    {
        await _world.SetSwitchesAsync(true);
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var studentUser = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var attempt = await _world.AttemptAsync(exam.Id, studentUser.Id);
        var student = new ApiClient(api.Factory.CreateClient());
        await student.LoginAsync(studentUser.Username, World.Password);
        var server = api.Factory.Server;
        await using var live = new LiveConnection(server.BaseAddress, student.FreshAccessTokenAsync,
            () => server.CreateHandler(), HttpTransportType.LongPolling); // TestServer has no WebSockets
        var heard = new TaskCompletionSource<LiveAttemptMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = 0;
        live.Received += (method, m) => { if (method == LiveMessages.DeadlineChanged) heard.TrySetResult(m); };
        live.Connected += () => { connected++; return Task.CompletedTask; };

        await live.StartAsync();
        await (await _world.LoginAsync(author)).PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(10, "Power cut"));

        var message = await heard.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(attempt.Id, message.AttemptId);
        Assert.Equal(attempt.DeadlineAt.AddMinutes(10), message.DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal(1, connected);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ApiClientExamTests|FullyQualifiedName~LiveConnectionTests"`
Expected: build errors — endpoint methods and `LiveConnection` not found.

- [ ] **Step 3: Implement the endpoints**

`clients/Secucode.Desktop/Api/ApiClient.Endpoints.cs`:

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using Secucode.Contracts;

namespace Secucode.Desktop.Api;

public sealed partial class ApiClient
{
    // ---- exams ----
    public Task<List<StudentExamDto>> StudentExamsAsync(CancellationToken ct = default) =>
        SendAsync<List<StudentExamDto>>(HttpMethod.Get, "api/exams", null, ct);

    public Task<List<ExamDto>> StaffExamsAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (fromUtc is { } from) query.Add("from=" + Uri.EscapeDataString(from.ToUniversalTime().ToString("O")));
        if (toUtc is { } to) query.Add("to=" + Uri.EscapeDataString(to.ToUniversalTime().ToString("O")));
        return SendAsync<List<ExamDto>>(HttpMethod.Get, "api/exams" + (query.Count > 0 ? "?" + string.Join("&", query) : ""), null, ct);
    }

    public Task<ExamDto> ExamAsync(int id, CancellationToken ct = default) =>
        SendAsync<ExamDto>(HttpMethod.Get, $"api/exams/{id}", null, ct);

    public Task<ExamDto> CreateExamAsync(ExamRequest request, CancellationToken ct = default) =>
        SendAsync<ExamDto>(HttpMethod.Post, "api/exams", request, ct);

    public Task<ExamDto> UpdateExamAsync(int id, ExamRequest request, CancellationToken ct = default) =>
        SendAsync<ExamDto>(HttpMethod.Put, $"api/exams/{id}", request, ct);

    public async Task SetExamActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Patch, $"api/exams/{id}/active", new SetActiveRequest(isActive), ct);
    }

    public async Task DeleteExamAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"api/exams/{id}", null, ct);
    }

    public Task<List<AttemptSummaryDto>> ExamAttemptsAsync(int examId, CancellationToken ct = default) =>
        SendAsync<List<AttemptSummaryDto>>(HttpMethod.Get, $"api/exams/{examId}/attempts", null, ct);

    // ---- attempts ----
    public Task<AttemptDto> StartAttemptAsync(int examId, string pin, CancellationToken ct = default) =>
        SendAsync<AttemptDto>(HttpMethod.Post, $"api/exams/{examId}/attempts", new StartAttemptRequest(pin), ct);

    public Task<AttemptDetailDto> AttemptAsync(int id, CancellationToken ct = default) =>
        SendAsync<AttemptDetailDto>(HttpMethod.Get, $"api/attempts/{id}", null, ct);

    public async Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Put, $"api/attempts/{attemptId}/code", request, ct);
    }

    public Task<AttemptEventDto> RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct = default) =>
        SendAsync<AttemptEventDto>(HttpMethod.Post, $"api/attempts/{attemptId}/events", request, ct);

    public Task<EvidenceDto> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct = default) =>
        SendAsync<EvidenceDto>(() =>
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            var extension = contentType switch { "image/png" => ".png", "audio/wav" => ".wav", _ => ".jpg" };
            var form = new MultipartFormDataContent
            {
                { new StringContent(kind.ToString()), "kind" },
                { file, "file", "evidence" + extension },
            };
            return new HttpRequestMessage(HttpMethod.Post, $"api/attempts/{attemptId}/evidence") { Content = form };
        }, ct);

    public Task<AttemptDto> SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct = default) =>
        SendAsync<AttemptDto>(HttpMethod.Post, $"api/attempts/{attemptId}/submit", request, ct);

    // ---- other ----
    public Task<RunResult> RunAsync(RunRequest request, CancellationToken ct = default) =>
        SendAsync<RunResult>(HttpMethod.Post, "api/run", request, ct);

    public Task<List<MasterDto>> MastersAsync(string kind, CancellationToken ct = default) =>
        SendAsync<List<MasterDto>>(HttpMethod.Get, $"api/{kind}", null, ct);
}
```

- [ ] **Step 4: Implement `LiveConnection`**

`clients/Secucode.Desktop/Api/LiveConnection.cs`:

```csharp
using System.Net.Http;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Secucode.Contracts;

namespace Secucode.Desktop.Api;

// The /hubs/exams connection. Students are joined to their private group by the server automatically.
public sealed class LiveConnection : IAsyncDisposable
{
    private readonly HubConnection _hub;

    public event Action<string, LiveAttemptMessage>? Received;

    // Fires after the first connect and after every reconnect: pushes sent while disconnected are lost,
    // so listeners must re-read the attempt.
    public event Func<Task>? Connected;

    public LiveConnection(Uri apiBase, Func<Task<string?>> accessToken, Func<HttpMessageHandler>? handler = null,
        HttpTransportType transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(apiBase, "hubs/exams"), o =>
            {
                o.AccessTokenProvider = accessToken;
                o.Transports = transports;
                if (handler is not null) o.HttpMessageHandlerFactory = _ => handler();
            })
            .WithAutomaticReconnect()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        foreach (var method in LiveMessages.All)
            _hub.On<LiveAttemptMessage>(method, m => Received?.Invoke(method, m));
        _hub.Reconnected += async _ => await RaiseConnectedAsync();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _hub.StartAsync(ct);
        // The server joins our private group after StartAsync returns; a round-trip guarantees it has happened.
        await _hub.InvokeAsync("LeaveExam", 0, ct);
        await RaiseConnectedAsync();
    }

    // The exam must not depend on the hub: keep trying in the background until connected or cancelled.
    public async Task StartWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await StartAsync(ct);
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task RaiseConnectedAsync()
    {
        if (Connected is { } handler)
            foreach (var h in handler.GetInvocationList().Cast<Func<Task>>())
                await h();
    }

    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests`
Expected: PASS (all tests so far). (`Invalid_exam_shows_field_errors` breaks the API's title, end-after-start, duration and questions rules.)

- [ ] **Step 6: Commit**

```bash
git add clients/Secucode.Desktop/Api clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): typed exam, attempt, run and master endpoints plus live connection

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: `Session` (who is signed in) and `ExamClock` (server-time countdown)

**Files:**
- Create: `clients/Secucode.Desktop/Session.cs`, `clients/Secucode.Desktop/Features/Student/ExamClock.cs`
- Test: `clients/Secucode.Desktop.Tests/SessionTests.cs`, `clients/Secucode.Desktop.Tests/ExamClockTests.cs`

**Interfaces:**
- Consumes: Contracts `MeResponse`, `UserDto`, `AssignmentDto` (with `IsStudent`, Task 1), `EffectiveCapabilityDto`, `ScopeDto`, `ExamDto`.
- Produces:
  - `sealed class Session(MeResponse me) { UserDto User; bool IsStudent; bool CanCreateExams; string RoleNames; bool CanManage(ExamDto); bool CanViewResults(ExamDto); }` (namespace `Secucode.Desktop`)
  - `sealed class ExamClock(DateTime deadlineUtc, DateTime serverTimeUtc, DateTime localUtcAtFetch) { DateTime DeadlineUtc {get;set;}; DateTime ServerNow(DateTime localUtcNow); TimeSpan Remaining(DateTime localUtcNow); bool PastSubmitGrace(DateTime localUtcNow); static TimeSpan SubmitGrace = 30 s; }` (namespace `Secucode.Desktop.Features.Student`)

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/SessionTests.cs`:

```csharp
using Secucode.Contracts;

namespace Secucode.Desktop.Tests;

public sealed class SessionTests
{
    private const int Me = 5;

    private static Session For(bool student = false, bool activeAssignment = true, params EffectiveCapabilityDto[] caps) =>
        new(new MeResponse(new UserDto(Me, "u", "User", null, true, student,
            [new AssignmentDto(1, 1, student ? "Student" : "Faculty", 1, null, null, null, null, activeAssignment, student)]), caps));

    private static EffectiveCapabilityDto Grant(Capability c, Reach reach = Reach.InScope, int? branch = 1, int? cls = null, int? batch = null) =>
        new(c, reach, new ScopeDto(branch, cls, batch), "Role");

    private static ExamDto Exam(int createdBy, int branch = 1, int? cls = null, int? batch = null) =>
        new(10, "Exam", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 30, ExamMode.DesktopOnly, true, branch, cls, batch,
            createdBy, "Creator", true, true, true, true, null);

    [Fact]
    public void Students_are_recognised_by_an_active_student_assignment()
    {
        Assert.True(For(student: true).IsStudent);
        Assert.False(For(student: true, activeAssignment: false).IsStudent);
        Assert.False(For().IsStudent);
    }

    [Fact]
    public void Creating_exams_needs_the_capability()
    {
        Assert.False(For().CanCreateExams);
        Assert.True(For(caps: Grant(Capability.CreateExams)).CanCreateExams);
    }

    [Fact]
    public void Creators_always_manage_and_view_their_own_exams()
    {
        var session = For();
        Assert.True(session.CanManage(Exam(createdBy: Me)));
        Assert.True(session.CanViewResults(Exam(createdBy: Me)));
        Assert.False(session.CanManage(Exam(createdBy: 99)));
    }

    [Fact]
    public void In_scope_grants_cover_exams_inside_the_scope_only()
    {
        var hod = For(caps: Grant(Capability.ManageExams, branch: 1));
        Assert.True(hod.CanManage(Exam(createdBy: 99, branch: 1, cls: 3, batch: 7)));
        Assert.False(hod.CanManage(Exam(createdBy: 99, branch: 2)));

        var yearCoordinator = For(caps: Grant(Capability.ViewResults, branch: 1, cls: 3));
        Assert.True(yearCoordinator.CanViewResults(Exam(createdBy: 99, branch: 1, cls: 3)));
        Assert.False(yearCoordinator.CanViewResults(Exam(createdBy: 99, branch: 1))); // a department-wide exam is wider than the year
    }

    [Fact]
    public void Own_only_grants_apply_to_own_exams_only()
    {
        var faculty = For(caps: Grant(Capability.ViewResults, Reach.OwnOnly));
        Assert.False(faculty.CanViewResults(Exam(createdBy: 99)));
    }

    [Fact]
    public void Role_names_list_active_roles_once()
    {
        Assert.Equal("Faculty", For().RoleNames);
    }
}
```

`clients/Secucode.Desktop.Tests/ExamClockTests.cs`:

```csharp
using Secucode.Desktop.Features.Student;

namespace Secucode.Desktop.Tests;

public sealed class ExamClockTests
{
    private static readonly DateTime Server = new(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_fast_pc_clock_does_not_shorten_the_exam()
    {
        var local = Server.AddMinutes(10); // the PC is 10 minutes fast
        var clock = new ExamClock(Server.AddMinutes(30), Server, local);

        Assert.Equal(TimeSpan.FromMinutes(30), clock.Remaining(local));
        Assert.Equal(TimeSpan.FromMinutes(20), clock.Remaining(local.AddMinutes(10)));
    }

    [Fact]
    public void Remaining_never_goes_negative()
    {
        var clock = new ExamClock(Server.AddMinutes(1), Server, Server);
        Assert.Equal(TimeSpan.Zero, clock.Remaining(Server.AddMinutes(5)));
    }

    [Fact]
    public void Submit_grace_is_thirty_seconds_after_the_deadline_in_server_time()
    {
        var local = Server.AddMinutes(-3); // the PC is 3 minutes slow
        var clock = new ExamClock(Server.AddMinutes(30), Server, local);

        Assert.False(clock.PastSubmitGrace(local.AddMinutes(30).AddSeconds(29)));
        Assert.True(clock.PastSubmitGrace(local.AddMinutes(30).AddSeconds(31)));
    }

    [Fact]
    public void A_moved_deadline_changes_the_countdown()
    {
        var clock = new ExamClock(Server.AddMinutes(30), Server, Server);
        clock.DeadlineUtc = Server.AddMinutes(40);
        Assert.Equal(TimeSpan.FromMinutes(40), clock.Remaining(Server));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~SessionTests|FullyQualifiedName~ExamClockTests"`
Expected: build errors — `Session`, `ExamClock` not found.

- [ ] **Step 3: Implement**

`clients/Secucode.Desktop/Session.cs`:

```csharp
using Secucode.Contracts;

namespace Secucode.Desktop;

// Who is signed in and what the UI should offer them. The API stays the authority: this only hides
// actions it would refuse, and a 403 is still shown with the server's reason.
public sealed class Session(MeResponse me)
{
    public UserDto User => me.User;
    public bool IsStudent => me.User.Assignments.Any(a => a.IsActive && a.IsStudent);
    public bool CanCreateExams => Has(Capability.CreateExams);
    public string RoleNames => string.Join(", ", me.User.Assignments.Where(a => a.IsActive).Select(a => a.RoleName).Distinct());

    public bool CanManage(ExamDto exam) => IsCreator(exam) || Grants(Capability.ManageExams, exam);
    public bool CanViewResults(ExamDto exam) => IsCreator(exam) || Grants(Capability.ViewResults, exam);

    private bool Has(Capability c) => me.Capabilities.Any(x => x.Capability == c);
    private bool IsCreator(ExamDto exam) => exam.CreatedBy == me.User.Id;

    // ponytail: scope containment by ids only; the API's checks (backend spec §5.3) cover anything this misses.
    private bool Grants(Capability c, ExamDto exam) => me.Capabilities.Any(x => x.Capability == c &&
        (x.Reach == Reach.OwnOnly ? IsCreator(exam) : Covers(x.Scope, exam)));

    private static bool Covers(ScopeDto s, ExamDto e) =>
        (s.BranchId is null || s.BranchId == e.BranchId) &&
        (s.ClassId is null || s.ClassId == e.ClassId) &&
        (s.BatchId is null || s.BatchId == e.BatchId);
}
```

`clients/Secucode.Desktop/Features/Student/ExamClock.cs`:

```csharp
namespace Secucode.Desktop.Features.Student;

// The server's clock is authoritative: the offset between server and PC is measured when the attempt is
// fetched, and every countdown uses server time.
public sealed class ExamClock(DateTime deadlineUtc, DateTime serverTimeUtc, DateTime localUtcAtFetch)
{
    public static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _offset = serverTimeUtc - localUtcAtFetch;

    public DateTime DeadlineUtc { get; set; } = deadlineUtc;

    public DateTime ServerNow(DateTime localUtcNow) => localUtcNow + _offset;

    public TimeSpan Remaining(DateTime localUtcNow)
    {
        var left = DeadlineUtc - ServerNow(localUtcNow);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    public bool PastSubmitGrace(DateTime localUtcNow) => ServerNow(localUtcNow) > DeadlineUtc + SubmitGrace;
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~SessionTests|FullyQualifiedName~ExamClockTests"`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add clients/Secucode.Desktop/Session.cs clients/Secucode.Desktop/Features clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): session capability checks and server-time exam clock

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `AttemptOutbox` — disk-backed, ordered, retrying exam writes

**Files:**
- Create: `clients/Secucode.Desktop/Outbox/OutboxItem.cs`, `clients/Secucode.Desktop/Outbox/AttemptOutbox.cs`, `clients/Secucode.Desktop/Outbox/ApiAttemptSender.cs`
- Test: `clients/Secucode.Desktop.Tests/FakeSender.cs`, `clients/Secucode.Desktop.Tests/AttemptOutboxTests.cs`

**Interfaces:**
- Consumes: `ApiClient` endpoints and `ApiError` (Tasks 2–4); `AppSettings.DataDir` (Task 2).
- Produces:
  - `interface IAttemptSender { Task SaveCodeAsync(int, SaveCodeRequest, CancellationToken); Task<long> UploadEvidenceAsync(int, EvidenceKind, byte[], string contentType, CancellationToken); Task RecordEventAsync(int, AttemptEventRequest, CancellationToken); Task SubmitAsync(int, SubmitRequest, CancellationToken); }`
  - `sealed class ApiAttemptSender(ApiClient api) : IAttemptSender`
  - `sealed class AttemptOutbox(string rootDir, int attemptId, IAttemptSender sender, Func<TimeSpan, CancellationToken, Task>? delay = null) : IAsyncDisposable` with `static string DefaultRoot`, `bool IsOnline`, `bool Closed`, `int Pending`, `IReadOnlyList<OutboxItem> Snapshot()`, `void SaveCode(string code, string language)`, `void Evidence(EvidenceKind, byte[], string contentType)`, `void Event(AttemptEventType, string? detail, byte[]? webcamJpeg = null)`, `void Submit(SubmitRequest)`, `void Start()`, `Task<bool> WaitUntilEmptyAsync(TimeSpan)`, `void DeleteIfEmpty()`, events `Action<bool>? OnlineChanged` and `Action? AttemptClosed` (both raised on a background thread).
  - `record OutboxItem(long Seq, OutboxKind Kind)` with init properties `Code`, `Submit`, `EventType`, `Detail`, `Evidence`, `ContentType`, `EvidenceId`; `enum OutboxKind { SaveCode, Evidence, Event, Submit }`.
- Rules (spec §6): items go in order, one at a time. Network errors / 5xx / 401 → retry the same item after 1, 2, 4 … 30 s. 404/409 → attempt closed: drop everything, refuse new items, raise `AttemptClosed`. 400/403/413/415 → drop that item only. Only the newest unsent code save is kept.

- [ ] **Step 1: Write the test sender and failing tests**

`clients/Secucode.Desktop.Tests/FakeSender.cs`:

```csharp
using Secucode.Contracts;
using Secucode.Desktop.Outbox;

namespace Secucode.Desktop.Tests;

// Records calls as short strings; FailNext makes the next call starting with a prefix throw.
public sealed class FakeSender : IAttemptSender
{
    private readonly List<(string Prefix, Exception Error)> _failures = [];
    private long _nextEvidenceId = 100;

    public List<string> Calls { get; } = [];

    public void FailNext(string prefix, Exception error)
    {
        lock (Calls) _failures.Add((prefix, error));
    }

    private void Hit(string call)
    {
        lock (Calls)
        {
            var i = _failures.FindIndex(f => call.StartsWith(f.Prefix, StringComparison.Ordinal));
            if (i >= 0)
            {
                var error = _failures[i].Error;
                _failures.RemoveAt(i);
                throw error;
            }
            Calls.Add(call);
        }
    }

    public Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct)
    {
        Hit($"code:{request.Code}");
        return Task.CompletedTask;
    }

    public Task<long> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct)
    {
        Hit($"evidence:{kind}:{bytes.Length}");
        return Task.FromResult(Interlocked.Increment(ref _nextEvidenceId));
    }

    public Task RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct)
    {
        Hit($"event:{request.Type}:{request.EvidenceId}");
        return Task.CompletedTask;
    }

    public Task SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct)
    {
        Hit($"submit:{request.Code}");
        return Task.CompletedTask;
    }
}
```

`clients/Secucode.Desktop.Tests/AttemptOutboxTests.cs`:

```csharp
using System.Net.Http;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Outbox;

namespace Secucode.Desktop.Tests;

public sealed class AttemptOutboxTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly string _root = Directory.CreateTempSubdirectory("secucode_outbox_").FullName;
    private readonly FakeSender _sender = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly List<AttemptOutbox> _open = [];

    private AttemptOutbox New()
    {
        var outbox = new AttemptOutbox(_root, 7, _sender, (t, _) => { lock (_delays) _delays.Add(t); return Task.CompletedTask; });
        _open.Add(outbox);
        return outbox;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var outbox in _open) await outbox.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Sends_in_order_and_links_the_frame_to_its_event()
    {
        var outbox = New();
        outbox.Event(AttemptEventType.FaceMissing, null, [1, 2, 3]);
        outbox.SaveCode("a", "Python");
        outbox.Evidence(EvidenceKind.Screenshot, [9, 9], "image/jpeg");
        outbox.Submit(new SubmitRequest("a", "Python", "Done"));

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["evidence:Webcam:3", "event:FaceMissing:101", "code:a", "evidence:Screenshot:2", "submit:a"], _sender.Calls);
    }

    [Fact]
    public async Task Only_the_latest_unsent_code_save_is_kept()
    {
        var outbox = New();
        outbox.SaveCode("a", "Python");
        outbox.SaveCode("b", "Python");
        outbox.SaveCode("c", "Python");

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["code:c"], _sender.Calls);
    }

    [Fact]
    public async Task Queue_survives_a_restart_and_sends_in_order()
    {
        const string code = "print('नमस्ते 🙂')";
        var crashed = New();
        crashed.Event(AttemptEventType.FocusLost, "1/3");
        crashed.SaveCode(code, "Python");
        await crashed.DisposeAsync(); // never started: nothing was sent before the "crash"
        _open.Remove(crashed);

        var restarted = New();
        Assert.Equal(2, restarted.Pending);
        restarted.Submit(new SubmitRequest(code, "Python", null));
        restarted.Start();

        Assert.True(await restarted.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["event:FocusLost:", $"code:{code}", $"submit:{code}"], _sender.Calls);
    }

    [Fact]
    public async Task Backs_off_while_offline_and_reports_it()
    {
        var outbox = New();
        var states = new List<bool>();
        outbox.OnlineChanged += online => { lock (states) states.Add(online); };
        for (var i = 0; i < 6; i++)
            _sender.FailNext("code:", ApiError.Network(new HttpRequestException("down")));
        outbox.SaveCode("a", "Python");

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal<double>([1, 2, 4, 8, 16, 30], _delays.Select(d => d.TotalSeconds));
        Assert.Equal([false, true], states);
        Assert.Equal(["code:a"], _sender.Calls);
    }

    [Fact]
    public async Task Evidence_is_uploaded_once_when_the_event_post_fails()
    {
        var outbox = New();
        _sender.FailNext("event:", ApiError.Network(new HttpRequestException("down")));
        outbox.Event(AttemptEventType.MultipleFaces, null, [1, 2]);

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["evidence:Webcam:2", "event:MultipleFaces:101"], _sender.Calls);
    }

    [Fact]
    public async Task A_closed_attempt_drops_the_queue_and_refuses_more()
    {
        var outbox = New();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        outbox.AttemptClosed += () => closed.TrySetResult();
        _sender.FailNext("code:", new ApiError(409, "Conflict", "This attempt is no longer in progress."));
        outbox.SaveCode("a", "Python");
        outbox.Evidence(EvidenceKind.Audio, [1], "audio/wav");

        outbox.Start();

        await closed.Task.WaitAsync(Wait);
        Assert.True(outbox.Closed);
        Assert.Equal(0, outbox.Pending);
        Assert.Empty(_sender.Calls);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "7")));
        outbox.SaveCode("b", "Python");
        Assert.Equal(0, outbox.Pending);
    }

    [Fact]
    public async Task A_rejected_item_is_dropped_and_the_queue_moves_on()
    {
        var outbox = New();
        _sender.FailNext("evidence:", new ApiError(400, "Bad Request", "File must be between 1 byte and 10 MB."));
        outbox.Evidence(EvidenceKind.Audio, [1], "audio/wav");
        outbox.SaveCode("a", "Python");

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["code:a"], _sender.Calls);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~AttemptOutboxTests"`
Expected: build errors — `Secucode.Desktop.Outbox` types not found.

- [ ] **Step 3: Implement the item and sender**

`clients/Secucode.Desktop/Outbox/OutboxItem.cs`:

```csharp
using Secucode.Contracts;

namespace Secucode.Desktop.Outbox;

public enum OutboxKind { SaveCode, Evidence, Event, Submit }

// One queued write, stored as "{Seq}.json" plus "{Seq}.bin" for evidence bytes. For an Event with a webcam frame,
// EvidenceId is written back once the upload succeeds, so a retry (even after a crash) never uploads it twice.
public sealed record OutboxItem(long Seq, OutboxKind Kind)
{
    public SaveCodeRequest? Code { get; init; }
    public SubmitRequest? Submit { get; init; }
    public AttemptEventType? EventType { get; init; }
    public string? Detail { get; init; }
    public EvidenceKind? Evidence { get; init; }
    public string? ContentType { get; init; }
    public long? EvidenceId { get; init; }
}

public interface IAttemptSender
{
    Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct);
    Task<long> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct);
    Task RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct);
    Task SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct);
}
```

`clients/Secucode.Desktop/Outbox/ApiAttemptSender.cs`:

```csharp
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Outbox;

public sealed class ApiAttemptSender(ApiClient api) : IAttemptSender
{
    public Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct) =>
        api.SaveCodeAsync(attemptId, request, ct);

    public async Task<long> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct) =>
        (await api.UploadEvidenceAsync(attemptId, kind, bytes, contentType, ct)).Id;

    public Task RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct) =>
        api.RecordEventAsync(attemptId, request, ct);

    public Task SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct) =>
        api.SubmitAsync(attemptId, request, ct);
}
```

- [ ] **Step 4: Implement the outbox**

`clients/Secucode.Desktop/Outbox/AttemptOutbox.cs`:

```csharp
using System.IO;
using System.Text.Json;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Outbox;

// Disk-backed FIFO of one attempt's writes (spec §6). A single loop sends items in order. Network trouble:
// wait and retry the same item (1 s doubling to 30 s). 404/409: the attempt is closed, so the queue is dropped.
// Other 4xx: the server will never accept that item, so it is dropped and the queue moves on.
public sealed class AttemptOutbox : IAsyncDisposable
{
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly string _dir;
    private readonly int _attemptId;
    private readonly IAttemptSender _sender;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly List<OutboxItem> _items;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private long _nextSeq;
    private long? _inFlight;
    private volatile bool _online = true;
    private volatile bool _closed;

    public event Action<bool>? OnlineChanged;
    public event Action? AttemptClosed;

    public AttemptOutbox(string rootDir, int attemptId, IAttemptSender sender, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _dir = Path.Combine(rootDir, attemptId.ToString());
        Directory.CreateDirectory(_dir);
        _attemptId = attemptId;
        _sender = sender;
        _delay = delay ?? Task.Delay;
        _items = Directory.EnumerateFiles(_dir, "*.json")
            .Select(f => JsonSerializer.Deserialize<OutboxItem>(File.ReadAllText(f), Json.Options)!)
            .OrderBy(i => i.Seq)
            .ToList();
        _nextSeq = _items.Count == 0 ? 1 : _items[^1].Seq + 1;
    }

    public static string DefaultRoot => Path.Combine(AppSettings.DataDir, "outbox");

    public bool IsOnline => _online;
    public bool Closed => _closed;
    public int Pending { get { lock (_items) return _items.Count; } }
    public IReadOnlyList<OutboxItem> Snapshot() { lock (_items) return _items.ToList(); }

    // Only the newest unsent save matters, so older queued saves are dropped.
    public void SaveCode(string code, string language)
    {
        lock (_items)
        {
            foreach (var old in _items.Where(i => i.Kind == OutboxKind.SaveCode && i.Seq != _inFlight).ToList())
                RemoveLocked(old.Seq);
            AddLocked(seq => new OutboxItem(seq, OutboxKind.SaveCode) { Code = new SaveCodeRequest(code, language) }, null);
        }
    }

    public void Evidence(EvidenceKind kind, byte[] bytes, string contentType) =>
        Add(seq => new OutboxItem(seq, OutboxKind.Evidence) { Evidence = kind, ContentType = contentType }, bytes);

    public void Event(AttemptEventType type, string? detail, byte[]? webcamJpeg = null) =>
        Add(seq => new OutboxItem(seq, OutboxKind.Event)
        {
            EventType = type,
            Detail = detail,
            Evidence = webcamJpeg is null ? null : EvidenceKind.Webcam,
            ContentType = webcamJpeg is null ? null : "image/jpeg",
        }, webcamJpeg);

    public void Submit(SubmitRequest request) =>
        Add(seq => new OutboxItem(seq, OutboxKind.Submit) { Submit = request }, null);

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    // ponytail: polling wait; it runs at most once per exam.
    public async Task<bool> WaitUntilEmptyAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (Pending > 0 && DateTime.UtcNow < until) await Task.Delay(100);
        return Pending == 0;
    }

    public void DeleteIfEmpty()
    {
        if (Pending == 0 && Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null) await _loop;
    }

    private void Add(Func<long, OutboxItem> make, byte[]? bytes)
    {
        lock (_items) AddLocked(make, bytes);
    }

    private void AddLocked(Func<long, OutboxItem> make, byte[]? bytes)
    {
        if (_closed) return; // the server won't accept anything more for this attempt
        var item = make(_nextSeq++);
        if (bytes is not null) File.WriteAllBytes(BinPath(item.Seq), bytes);
        WriteJson(item); // written after the bytes: a loaded item always has them
        _items.Add(item);
        _wake.Release();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = FirstBackoff;
        while (!ct.IsCancellationRequested)
        {
            OutboxItem? item;
            lock (_items)
            {
                item = _items.FirstOrDefault();
                _inFlight = item?.Seq;
            }
            if (item is null)
            {
                try { await _wake.WaitAsync(ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await SendAsync(item, ct);
                lock (_items) { _inFlight = null; RemoveLocked(item.Seq); }
                SetOnline(true);
                backoff = FirstBackoff;
            }
            catch (ApiError e) when (e.Status is 404 or 409)
            {
                lock (_items)
                {
                    _closed = true;
                    _inFlight = null;
                    foreach (var i in _items.ToList()) RemoveLocked(i.Seq);
                }
                SetOnline(true);
                AttemptClosed?.Invoke();
                return;
            }
            catch (ApiError e) when (e.Status is 400 or 403 or 413 or 415)
            {
                lock (_items) { _inFlight = null; RemoveLocked(item.Seq); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                lock (_items) _inFlight = null;
                SetOnline(false);
                try { await _delay(backoff, ct); }
                catch (OperationCanceledException) { return; }
                backoff = backoff * 2 > MaxBackoff ? MaxBackoff : backoff * 2;
            }
        }
    }

    private async Task SendAsync(OutboxItem item, CancellationToken ct)
    {
        switch (item.Kind)
        {
            case OutboxKind.SaveCode:
                await _sender.SaveCodeAsync(_attemptId, item.Code!, ct);
                break;
            case OutboxKind.Evidence:
                await _sender.UploadEvidenceAsync(_attemptId, item.Evidence!.Value, File.ReadAllBytes(BinPath(item.Seq)), item.ContentType!, ct);
                break;
            case OutboxKind.Event:
                var evidenceId = item.EvidenceId;
                if (item.Evidence is { } kind && evidenceId is null)
                {
                    evidenceId = await _sender.UploadEvidenceAsync(_attemptId, kind, File.ReadAllBytes(BinPath(item.Seq)), item.ContentType!, ct);
                    lock (_items) ReplaceLocked(item with { EvidenceId = evidenceId });
                }
                await _sender.RecordEventAsync(_attemptId, new AttemptEventRequest(item.EventType!.Value, item.Detail, evidenceId), ct);
                break;
            case OutboxKind.Submit:
                await _sender.SubmitAsync(_attemptId, item.Submit!, ct);
                break;
        }
    }

    private string JsonPath(long seq) => Path.Combine(_dir, $"{seq:D10}.json");
    private string BinPath(long seq) => Path.Combine(_dir, $"{seq:D10}.bin");

    private void WriteJson(OutboxItem item)
    {
        var path = JsonPath(item.Seq);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(item, Json.Options));
        File.Move(path + ".tmp", path, overwrite: true); // atomic replace: a crash never leaves half an item
    }

    private void ReplaceLocked(OutboxItem item)
    {
        var i = _items.FindIndex(x => x.Seq == item.Seq);
        if (i < 0) return;
        _items[i] = item;
        WriteJson(item);
    }

    private void RemoveLocked(long seq)
    {
        _items.RemoveAll(x => x.Seq == seq);
        File.Delete(JsonPath(seq));
        File.Delete(BinPath(seq));
    }

    private void SetOnline(bool online)
    {
        if (_online == online) return;
        _online = online;
        OnlineChanged?.Invoke(online);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~AttemptOutboxTests"`
Expected: PASS (7 tests). Run it 3 times to make sure none is flaky.

- [ ] **Step 6: Commit**

```bash
git add clients/Secucode.Desktop/Outbox clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): disk-backed attempt outbox with ordered retry

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Hand-built theme — tokens, type, controls, motion, InfoBar, ThemeManager

**Files:**
- Create: `clients/Secucode.Desktop/Theme/Colors.Light.xaml`, `Colors.Dark.xaml`, `Typography.xaml`, `Motion.xaml`, `Controls.xaml`, `ThemeManager.cs`, `Transitions.cs`, `InfoBar.xaml`, `InfoBar.xaml.cs`
- Test: `clients/Secucode.Desktop.Tests/Sta.cs`, `clients/Secucode.Desktop.Tests/ThemeTests.cs`

**Interfaces:**
- Consumes: `ThemeChoice` (Task 2), `ApiError` (Task 2).
- Produces:
  - Brush keys (identical in both colour files, 22 keys): `Surface SurfaceRaised SurfaceSunken Border BorderStrong TextPrimary TextSecondary TextDisabled Accent AccentHover AccentPressed AccentSoft OnAccent Danger DangerSoft Warning WarningSoft Success SuccessSoft Focus SubtleHover SubtlePressed`.
  - In `Controls.xaml` (which merges `Typography.xaml` and `Motion.xaml`): fonts `UiFont DisplayFont CodeFont IconFont`; text styles `Caption Body Subtitle Title Display Icon`; radii `ControlRadius CardRadius`; durations `Motion.Fast Motion.Pop Motion.Page`; easing `Motion.EaseOut`; keyed styles `AppWindow FocusRing ButtonBase PrimaryButton SubtleButton IconButton CaptionButton CloseCaptionButton CodeEditor ToggleSwitch Card NavList`; implicit styles for `Button TextBox PasswordBox ComboBox ComboBoxItem CheckBox ToolTip DataGrid DataGridColumnHeader DataGridRow DataGridCell ScrollBar DatePicker DatePickerTextBox`.
  - `static class ThemeManager { ThemeChoice Current; bool IsDark; void Apply(ThemeChoice); void FollowSystem(); bool WindowsUsesDarkTheme(); }` (namespace `Secucode.Desktop.Theme`)
  - `static class Transitions { void PageIn(FrameworkElement); void PopIn(FrameworkElement); void SlideTo(TranslateTransform, double y); }`
  - `enum InfoSeverity { Info, Success, Warning, Error }`; `partial class InfoBar : UserControl { void Show(InfoSeverity, string message, string? actionLabel = null, Action? action = null); void ShowError(ApiError, Action? retry = null); void Hide(); }`
- Spec §8 lists 16 brush tokens; this plan adds six (`AccentSoft`, `DangerSoft`, `WarningSoft`, `SuccessSoft`, `SubtleHover`, `SubtlePressed`) that InfoBar backgrounds and hover overlays need.

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/Sta.cs`:

```csharp
using System.Runtime.ExceptionServices;

namespace Secucode.Desktop.Tests;

// WPF objects need an STA thread; xUnit runs tests on MTA threads.
public static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
```

`clients/Secucode.Desktop.Tests/ThemeTests.cs`:

```csharp
using System.Windows;
using System.Windows.Media;

namespace Secucode.Desktop.Tests;

public sealed class ThemeTests
{
    private static ResourceDictionary Load(string file)
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack; // registers the pack:// scheme outside a running app
        return (ResourceDictionary)Application.LoadComponent(new Uri($"/Secucode;component/Theme/{file}", UriKind.Relative));
    }

    [Fact]
    public void Light_and_dark_define_the_same_brush_tokens() => Sta.Run(() =>
    {
        var light = Load("Colors.Light.xaml");
        var dark = Load("Colors.Dark.xaml");
        var keys = light.Keys.Cast<string>().Order().ToList();

        Assert.Equal(keys, dark.Keys.Cast<string>().Order().ToList());
        Assert.All(keys, k =>
        {
            Assert.IsType<SolidColorBrush>(light[k]);
            Assert.IsType<SolidColorBrush>(dark[k]);
        });
        Assert.Equal(22, keys.Count);
    });

    [Fact]
    public void Controls_load_and_expose_the_named_resources() => Sta.Run(() =>
    {
        var controls = Load("Controls.xaml");
        foreach (var key in new[]
                 {
                     "AppWindow", "FocusRing", "PrimaryButton", "SubtleButton", "IconButton", "CaptionButton", "CloseCaptionButton",
                     "ToggleSwitch", "Card", "NavList", "CodeEditor", "Caption", "Body", "Subtitle", "Title", "Display", "Icon",
                     "UiFont", "CodeFont", "IconFont", "ControlRadius", "Motion.Fast", "Motion.Page", "Motion.EaseOut",
                 })
            Assert.True(controls.Contains(key) || controls.MergedDictionaries.Any(d => d.Contains(key)), $"missing {key}");
    });
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ThemeTests"`
Expected: FAIL — `IOException: Cannot locate resource 'theme/colors.light.xaml'`.

- [ ] **Step 3: Write the colour tokens**

`clients/Secucode.Desktop/Theme/Colors.Light.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Colour tokens. Colors.Dark.xaml defines exactly the same keys; controls use them only via DynamicResource. -->
    <SolidColorBrush x:Key="Surface" Color="#F6F6F8"/>
    <SolidColorBrush x:Key="SurfaceRaised" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="SurfaceSunken" Color="#EEEEF2"/>
    <SolidColorBrush x:Key="Border" Color="#E2E2E8"/>
    <SolidColorBrush x:Key="BorderStrong" Color="#C6C6D0"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#1A1A1F"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#5D5D69"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#A2A2AC"/>
    <SolidColorBrush x:Key="Accent" Color="#2B5FD9"/>
    <SolidColorBrush x:Key="AccentHover" Color="#2452C0"/>
    <SolidColorBrush x:Key="AccentPressed" Color="#1E45A3"/>
    <SolidColorBrush x:Key="AccentSoft" Color="#E6EDFC"/>
    <SolidColorBrush x:Key="OnAccent" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="Danger" Color="#C8322B"/>
    <SolidColorBrush x:Key="DangerSoft" Color="#FBE9E8"/>
    <SolidColorBrush x:Key="Warning" Color="#A65F0A"/>
    <SolidColorBrush x:Key="WarningSoft" Color="#FDF1DE"/>
    <SolidColorBrush x:Key="Success" Color="#1F8A4C"/>
    <SolidColorBrush x:Key="SuccessSoft" Color="#E3F4EA"/>
    <SolidColorBrush x:Key="Focus" Color="#2B5FD9"/>
    <SolidColorBrush x:Key="SubtleHover" Color="#0F000000"/>
    <SolidColorBrush x:Key="SubtlePressed" Color="#1C000000"/>
</ResourceDictionary>
```

`clients/Secucode.Desktop/Theme/Colors.Dark.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Surface" Color="#1B1B1F"/>
    <SolidColorBrush x:Key="SurfaceRaised" Color="#25252B"/>
    <SolidColorBrush x:Key="SurfaceSunken" Color="#151518"/>
    <SolidColorBrush x:Key="Border" Color="#33333B"/>
    <SolidColorBrush x:Key="BorderStrong" Color="#4A4A55"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#F1F1F4"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#A9A9B5"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#686872"/>
    <SolidColorBrush x:Key="Accent" Color="#6E9BFF"/>
    <SolidColorBrush x:Key="AccentHover" Color="#86ACFF"/>
    <SolidColorBrush x:Key="AccentPressed" Color="#5A86E8"/>
    <SolidColorBrush x:Key="AccentSoft" Color="#24304D"/>
    <SolidColorBrush x:Key="OnAccent" Color="#0D1220"/>
    <SolidColorBrush x:Key="Danger" Color="#FF7B72"/>
    <SolidColorBrush x:Key="DangerSoft" Color="#3D2423"/>
    <SolidColorBrush x:Key="Warning" Color="#F2B45A"/>
    <SolidColorBrush x:Key="WarningSoft" Color="#3B2F1C"/>
    <SolidColorBrush x:Key="Success" Color="#5CD08A"/>
    <SolidColorBrush x:Key="SuccessSoft" Color="#1E3527"/>
    <SolidColorBrush x:Key="Focus" Color="#86ACFF"/>
    <SolidColorBrush x:Key="SubtleHover" Color="#14FFFFFF"/>
    <SolidColorBrush x:Key="SubtlePressed" Color="#24FFFFFF"/>
</ResourceDictionary>
```

- [ ] **Step 4: Write type and motion**

`clients/Secucode.Desktop/Theme/Typography.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <FontFamily x:Key="UiFont">Segoe UI Variable Text, Segoe UI</FontFamily>
    <FontFamily x:Key="DisplayFont">Segoe UI Variable Display, Segoe UI</FontFamily>
    <FontFamily x:Key="CodeFont">Cascadia Mono, Consolas</FontFamily>
    <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>

    <!-- Type ramp: Caption 12, Body 14, Subtitle 20, Title 28, Display 40. Windows set UiFont 14 for everything else. -->
    <Style x:Key="Caption" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Foreground" Value="{DynamicResource TextSecondary}"/>
    </Style>
    <Style x:Key="Body" TargetType="TextBlock">
        <Setter Property="FontSize" Value="14"/>
        <Setter Property="TextWrapping" Value="Wrap"/>
    </Style>
    <Style x:Key="Subtitle" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource DisplayFont}"/>
        <Setter Property="FontSize" Value="20"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style x:Key="Title" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource DisplayFont}"/>
        <Setter Property="FontSize" Value="28"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style x:Key="Display" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource DisplayFont}"/>
        <Setter Property="FontSize" Value="40"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style x:Key="Icon" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource IconFont}"/>
        <Setter Property="FontSize" Value="16"/>
    </Style>
</ResourceDictionary>
```

`clients/Secucode.Desktop/Theme/Motion.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Every duration and easing in the app comes from here (spec §8). -->
    <Duration x:Key="Motion.Fast">0:0:0.1</Duration>
    <Duration x:Key="Motion.Pop">0:0:0.15</Duration>
    <Duration x:Key="Motion.Page">0:0:0.2</Duration>
    <CubicEase x:Key="Motion.EaseOut" EasingMode="EaseOut"/>
</ResourceDictionary>
```

- [ ] **Step 5: Write the control styles**

`clients/Secucode.Desktop/Theme/Controls.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Typography.xaml"/>
        <ResourceDictionary Source="Motion.xaml"/>
    </ResourceDictionary.MergedDictionaries>

    <CornerRadius x:Key="ControlRadius">8</CornerRadius>
    <CornerRadius x:Key="CardRadius">12</CornerRadius>

    <Style x:Key="AppWindow" TargetType="Window">
        <Setter Property="Background" Value="{DynamicResource Surface}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
        <Setter Property="FontSize" Value="14"/>
        <Setter Property="UseLayoutRounding" Value="True"/>
        <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
    </Style>

    <!-- Shown only for keyboard focus (that is how WPF uses FocusVisualStyle). -->
    <Style x:Key="FocusRing">
        <Setter Property="Control.Template">
            <Setter.Value>
                <ControlTemplate>
                    <Border Margin="-3" CornerRadius="10" BorderThickness="2" BorderBrush="{DynamicResource Focus}"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Buttons: one template; a hover overlay fades in over Motion.Fast and pressed deepens it. -->
    <Style x:Key="ButtonBase" TargetType="Button">
        <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
        <Setter Property="FontSize" Value="14"/>
        <Setter Property="Padding" Value="16,7"/>
        <Setter Property="MinHeight" Value="34"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Background" Value="{DynamicResource SurfaceRaised}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderStrong}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Grid>
                        <Border CornerRadius="{StaticResource ControlRadius}" Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"/>
                        <Border x:Name="Overlay" CornerRadius="{StaticResource ControlRadius}" Background="{DynamicResource SubtleHover}" Opacity="0"/>
                        <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"
                                          RecognizesAccessKey="True" TextElement.Foreground="{TemplateBinding Foreground}"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Trigger.EnterActions>
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="Overlay" Storyboard.TargetProperty="Opacity" To="1" Duration="{StaticResource Motion.Fast}"/>
                                    </Storyboard>
                                </BeginStoryboard>
                            </Trigger.EnterActions>
                            <Trigger.ExitActions>
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="Overlay" Storyboard.TargetProperty="Opacity" To="0" Duration="{StaticResource Motion.Fast}"/>
                                    </Storyboard>
                                </BeginStoryboard>
                            </Trigger.ExitActions>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Overlay" Property="Background" Value="{DynamicResource SubtlePressed}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                            <Setter Property="Cursor" Value="Arrow"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="Button" BasedOn="{StaticResource ButtonBase}"/>

    <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource ButtonBase}">
        <Setter Property="Background" Value="{DynamicResource Accent}"/>
        <Setter Property="Foreground" Value="{DynamicResource OnAccent}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>

    <Style x:Key="SubtleButton" TargetType="Button" BasedOn="{StaticResource ButtonBase}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="0"/>
    </Style>

    <Style x:Key="IconButton" TargetType="Button" BasedOn="{StaticResource SubtleButton}">
        <Setter Property="FontFamily" Value="{StaticResource IconFont}"/>
        <Setter Property="FontSize" Value="16"/>
        <Setter Property="Padding" Value="0"/>
        <Setter Property="Width" Value="36"/>
        <Setter Property="Height" Value="36"/>
        <Setter Property="MinHeight" Value="0"/>
    </Style>

    <Style x:Key="CaptionButton" TargetType="Button">
        <Setter Property="FontFamily" Value="{StaticResource IconFont}"/>
        <Setter Property="FontSize" Value="10"/>
        <Setter Property="Width" Value="46"/>
        <Setter Property="Height" Value="40"/>
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Focusable" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bg" Background="{TemplateBinding Background}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bg" Property="Background" Value="{DynamicResource SubtleHover}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Bg" Property="Background" Value="{DynamicResource SubtlePressed}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Windows' own close-button red, same in both themes. -->
    <Style x:Key="CloseCaptionButton" TargetType="Button" BasedOn="{StaticResource CaptionButton}">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bg" Background="Transparent">
                        <ContentPresenter x:Name="Glyph" HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bg" Property="Background" Value="#C42B1C"/>
                            <Setter TargetName="Glyph" Property="TextElement.Foreground" Value="White"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Text input: 8 px corners, accent bottom edge when focused. PasswordBox shares the look. -->
    <Style TargetType="TextBox">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Background" Value="{DynamicResource SurfaceRaised}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderStrong}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="10,7"/>
        <Setter Property="MinHeight" Value="34"/>
        <Setter Property="CaretBrush" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="SelectionBrush" Value="{DynamicResource Accent}"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="Bd" CornerRadius="{StaticResource ControlRadius}" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                        <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" Focusable="False"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                            <Setter TargetName="Bd" Property="BorderThickness" Value="1,1,1,2"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="CodeEditor" TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
        <Setter Property="FontFamily" Value="{StaticResource CodeFont}"/>
        <Setter Property="FontSize" Value="14"/>
        <Setter Property="AcceptsReturn" Value="True"/>
        <Setter Property="AcceptsTab" Value="True"/>
        <Setter Property="TextWrapping" Value="NoWrap"/>
        <Setter Property="VerticalContentAlignment" Value="Top"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Auto"/>
        <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/>
    </Style>

    <Style TargetType="PasswordBox">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Background" Value="{DynamicResource SurfaceRaised}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderStrong}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="10,7"/>
        <Setter Property="MinHeight" Value="34"/>
        <Setter Property="CaretBrush" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="SelectionBrush" Value="{DynamicResource Accent}"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="PasswordBox">
                    <Border x:Name="Bd" CornerRadius="{StaticResource ControlRadius}" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                        <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" Focusable="False"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                            <Setter TargetName="Bd" Property="BorderThickness" Value="1,1,1,2"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ComboBox -->
    <Style TargetType="ComboBoxItem">
        <Setter Property="Padding" Value="10,7"/>
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                    <Border x:Name="Bd" CornerRadius="6" Background="Transparent" Padding="{TemplateBinding Padding}">
                        <ContentPresenter/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource SubtleHover}"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentSoft}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="ComboBox">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Padding" Value="10,7,32,7"/>
        <Setter Property="MinHeight" Value="34"/>
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                    <Grid>
                        <ToggleButton Focusable="False" ClickMode="Press"
                                      IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                            <ToggleButton.Template>
                                <ControlTemplate TargetType="ToggleButton">
                                    <Border x:Name="Bd" CornerRadius="{StaticResource ControlRadius}" Background="{DynamicResource SurfaceRaised}"
                                            BorderBrush="{DynamicResource BorderStrong}" BorderThickness="1">
                                        <TextBlock Text="&#xE70D;" FontFamily="{StaticResource IconFont}" FontSize="10" Margin="0,0,12,0"
                                                   HorizontalAlignment="Right" VerticalAlignment="Center" Foreground="{DynamicResource TextSecondary}"/>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource TextSecondary}"/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </ToggleButton.Template>
                        </ToggleButton>
                        <ContentPresenter IsHitTestVisible="False" Margin="{TemplateBinding Padding}" VerticalAlignment="Center"
                                          Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                          ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
                        <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade"
                               IsOpen="{TemplateBinding IsDropDownOpen}">
                            <Border Margin="0,4,0,8" Padding="4" CornerRadius="{StaticResource ControlRadius}"
                                    MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                                    MaxHeight="{TemplateBinding MaxDropDownHeight}"
                                    Background="{DynamicResource SurfaceRaised}" BorderBrush="{DynamicResource Border}" BorderThickness="1">
                                <ScrollViewer>
                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- CheckBox, and a ToggleSwitch built on CheckBox -->
    <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <StackPanel Orientation="Horizontal" Background="Transparent">
                        <Border x:Name="Box" Width="18" Height="18" CornerRadius="4" BorderThickness="1"
                                BorderBrush="{DynamicResource BorderStrong}" Background="{DynamicResource SurfaceRaised}">
                            <TextBlock x:Name="Tick" Text="&#xE73E;" FontFamily="{StaticResource IconFont}" FontSize="12"
                                       HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="{DynamicResource OnAccent}" Visibility="Collapsed"/>
                        </Border>
                        <ContentPresenter Margin="8,0,0,0" VerticalAlignment="Center"/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Box" Property="Background" Value="{DynamicResource Accent}"/>
                            <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                            <Setter TargetName="Tick" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="ToggleSwitch" TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <StackPanel Orientation="Horizontal" Background="Transparent">
                        <Border x:Name="Track" Width="40" Height="20" CornerRadius="10" BorderThickness="1"
                                BorderBrush="{DynamicResource BorderStrong}" Background="{DynamicResource SurfaceRaised}">
                            <Ellipse x:Name="Knob" Width="12" Height="12" HorizontalAlignment="Left" Margin="4,0,0,0" Fill="{DynamicResource TextSecondary}">
                                <Ellipse.RenderTransform>
                                    <TranslateTransform x:Name="KnobMove"/>
                                </Ellipse.RenderTransform>
                            </Ellipse>
                        </Border>
                        <ContentPresenter Margin="10,0,0,0" VerticalAlignment="Center"/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Track" Property="Background" Value="{DynamicResource Accent}"/>
                            <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                            <Setter TargetName="Knob" Property="Fill" Value="{DynamicResource OnAccent}"/>
                            <Trigger.EnterActions>
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="KnobMove" Storyboard.TargetProperty="X" To="20"
                                                         Duration="{StaticResource Motion.Pop}" EasingFunction="{StaticResource Motion.EaseOut}"/>
                                    </Storyboard>
                                </BeginStoryboard>
                            </Trigger.EnterActions>
                            <Trigger.ExitActions>
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="KnobMove" Storyboard.TargetProperty="X" To="0"
                                                         Duration="{StaticResource Motion.Pop}" EasingFunction="{StaticResource Motion.EaseOut}"/>
                                    </Storyboard>
                                </BeginStoryboard>
                            </Trigger.ExitActions>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Surfaces -->
    <Style x:Key="Card" TargetType="Border">
        <Setter Property="Background" Value="{DynamicResource SurfaceRaised}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Border}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="CornerRadius" Value="{StaticResource CardRadius}"/>
        <Setter Property="Padding" Value="20"/>
    </Style>

    <Style TargetType="ToolTip">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToolTip">
                    <Border Padding="8,5" CornerRadius="6" Background="{DynamicResource SurfaceRaised}" BorderBrush="{DynamicResource Border}" BorderThickness="1">
                        <ContentPresenter TextElement.FontSize="12"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Sidebar navigation list (ShellWindow). Items are NavItem(Glyph, Label, Create). -->
    <Style x:Key="NavList" TargetType="ListBox">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="ItemContainerStyle">
            <Setter.Value>
                <Style TargetType="ListBoxItem">
                    <Setter Property="Height" Value="40"/>
                    <Setter Property="Margin" Value="0,0,0,2"/>
                    <Setter Property="Cursor" Value="Hand"/>
                    <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
                    <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ListBoxItem">
                                <Border x:Name="Bd" CornerRadius="6" Background="Transparent" Padding="14,0">
                                    <ContentPresenter VerticalAlignment="Center"/>
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource SubtleHover}"/>
                                    </Trigger>
                                    <Trigger Property="IsSelected" Value="True">
                                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource SubtleHover}"/>
                                        <Setter Property="FontWeight" Value="SemiBold"/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </Setter.Value>
        </Setter>
        <Setter Property="ItemTemplate">
            <Setter.Value>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding Glyph}" Style="{StaticResource Icon}" Width="20" VerticalAlignment="Center"/>
                        <TextBlock Text="{Binding Label}" Margin="14,0,0,0" VerticalAlignment="Center"/>
                    </StackPanel>
                </DataTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- DataGrid: flat rows, hairline separators, soft accent selection -->
    <Style TargetType="DataGrid">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="GridLinesVisibility" Value="Horizontal"/>
        <Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource Border}"/>
        <Setter Property="HeadersVisibility" Value="Column"/>
        <Setter Property="RowBackground" Value="Transparent"/>
        <Setter Property="AlternatingRowBackground" Value="Transparent"/>
        <Setter Property="AutoGenerateColumns" Value="False"/>
        <Setter Property="CanUserAddRows" Value="False"/>
        <Setter Property="CanUserDeleteRows" Value="False"/>
        <Setter Property="CanUserResizeRows" Value="False"/>
        <Setter Property="IsReadOnly" Value="True"/>
        <Setter Property="SelectionMode" Value="Single"/>
        <Setter Property="SelectionUnit" Value="FullRow"/>
        <Setter Property="RowHeight" Value="40"/>
    </Style>
    <Style TargetType="DataGridColumnHeader">
        <Setter Property="Foreground" Value="{DynamicResource TextSecondary}"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridColumnHeader">
                    <Border Padding="12,8" BorderBrush="{DynamicResource BorderStrong}" BorderThickness="0,0,0,1" Background="Transparent">
                        <ContentPresenter VerticalAlignment="Center"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="DataGridRow">
        <Setter Property="Background" Value="Transparent"/>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource SubtleHover}"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Background" Value="{DynamicResource AccentSoft}"/>
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style TargetType="DataGridCell">
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridCell">
                    <Border Padding="12,0" Background="Transparent">
                        <ContentPresenter VerticalAlignment="Center"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Slim scrollbars (no arrow buttons) -->
    <Style x:Key="ScrollThumb" TargetType="Thumb">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                    <Border CornerRadius="3" Background="{DynamicResource BorderStrong}"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="ScrollBar">
        <Setter Property="Width" Value="8"/>
        <Setter Property="MinWidth" Value="8"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Track x:Name="PART_Track" IsDirectionReversed="True" Margin="1">
                        <Track.Thumb>
                            <Thumb Style="{StaticResource ScrollThumb}"/>
                        </Track.Thumb>
                    </Track>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/>
                <Setter Property="MinWidth" Value="0"/>
                <Setter Property="Height" Value="8"/>
                <Setter Property="MinHeight" Value="8"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ScrollBar">
                            <Track x:Name="PART_Track" Margin="1">
                                <Track.Thumb>
                                    <Thumb Style="{StaticResource ScrollThumb}"/>
                                </Track.Thumb>
                            </Track>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- ponytail: only the DatePicker field is restyled; the calendar popup keeps WPF's default look. Restyle Calendar if it bothers the demo. -->
    <Style TargetType="DatePickerTextBox">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DatePickerTextBox">
                    <ScrollViewer x:Name="PART_ContentHost" Margin="0" VerticalAlignment="Center"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="DatePicker">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
        <Setter Property="Background" Value="{DynamicResource SurfaceRaised}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderStrong}"/>
        <Setter Property="Padding" Value="8,5"/>
        <Setter Property="MinHeight" Value="34"/>
    </Style>
</ResourceDictionary>
```

- [ ] **Step 6: Write `ThemeManager`, `Transitions` and `InfoBar`**

`clients/Secucode.Desktop/Theme/ThemeManager.cs`:

```csharp
using System.Windows;
using Microsoft.Win32;

namespace Secucode.Desktop.Theme;

// Swaps the colour dictionary at runtime. "System" follows Windows' app theme, including later changes.
public static class ThemeManager
{
    private static bool _following;

    public static ThemeChoice Current { get; private set; }
    public static bool IsDark { get; private set; }

    public static void Apply(ThemeChoice choice)
    {
        Current = choice;
        IsDark = choice == ThemeChoice.Dark || (choice == ThemeChoice.System && WindowsUsesDarkTheme());
        var fresh = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Secucode;component/Theme/Colors.{(IsDark ? "Dark" : "Light")}.xaml"),
        };
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Colors.") == true);
        if (old is null) dictionaries.Insert(0, fresh);
        else dictionaries[dictionaries.IndexOf(old)] = fresh;
    }

    public static void FollowSystem()
    {
        if (_following) return;
        _following = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (Current == ThemeChoice.System && e.Category == UserPreferenceCategory.General)
                Application.Current.Dispatcher.BeginInvoke(() => Apply(ThemeChoice.System));
        };
    }

    public static bool WindowsUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
```

`clients/Secucode.Desktop/Theme/Transitions.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Secucode.Desktop.Theme;

// Code-driven motion. Timings come from Motion.xaml; nothing animates when Windows animations are turned off.
public static class Transitions
{
    private static bool Enabled => SystemParameters.ClientAreaAnimation && Application.Current is not null;
    private static Duration Get(string key) => (Duration)Application.Current.FindResource(key);
    private static IEasingFunction Ease => (IEasingFunction)Application.Current.FindResource("Motion.EaseOut");

    // New page: fade in while sliding up 12 px.
    public static void PageIn(FrameworkElement page)
    {
        if (!Enabled) return;
        var move = new TranslateTransform(0, 12);
        page.RenderTransform = move;
        var d = Get("Motion.Page");
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = Ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, d) { EasingFunction = Ease });
    }

    // InfoBars and overlays: fade in while scaling up from 96 %.
    public static void PopIn(FrameworkElement element)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(0.96, 0.96);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var d = Get("Motion.Pop");
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = Ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, d) { EasingFunction = Ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, d) { EasingFunction = Ease });
    }

    // Sidebar selection indicator.
    public static void SlideTo(TranslateTransform transform, double y)
    {
        if (!Enabled)
        {
            transform.Y = y;
            return;
        }
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, Get("Motion.Page")) { EasingFunction = Ease });
    }
}
```

`clients/Secucode.Desktop/Theme/InfoBar.xaml`:

```xml
<UserControl x:Class="Secucode.Desktop.Theme.InfoBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Visibility="Collapsed">
    <Border x:Name="Root" CornerRadius="8" BorderThickness="1" Padding="12,10">
        <DockPanel>
            <TextBlock x:Name="Glyph" DockPanel.Dock="Left" Style="{StaticResource Icon}" Margin="0,2,10,0" VerticalAlignment="Top"/>
            <Button x:Name="ActionButton" DockPanel.Dock="Right" Style="{StaticResource SubtleButton}" Padding="10,4" MinHeight="0"
                    Margin="10,-4,0,-4" Visibility="Collapsed" Click="ActionButton_Click"/>
            <TextBlock x:Name="MessageText" Style="{StaticResource Body}" VerticalAlignment="Center"/>
        </DockPanel>
    </Border>
</UserControl>
```

`clients/Secucode.Desktop/Theme/InfoBar.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Theme;

public enum InfoSeverity { Info, Success, Warning, Error }

// The one way screens show status and errors: icon + message (+ optional action), coloured by severity.
public partial class InfoBar : UserControl
{
    private Action? _action;

    public InfoBar() => InitializeComponent();

    public void Show(InfoSeverity severity, string message, string? actionLabel = null, Action? action = null)
    {
        var (fill, edge, glyph) = severity switch
        {
            InfoSeverity.Success => ("SuccessSoft", "Success", ""),
            InfoSeverity.Warning => ("WarningSoft", "Warning", ""),
            InfoSeverity.Error => ("DangerSoft", "Danger", ""),
            _ => ("AccentSoft", "Accent", ""),
        };
        Root.SetResourceReference(Border.BackgroundProperty, fill);
        Root.SetResourceReference(Border.BorderBrushProperty, edge);
        Glyph.SetResourceReference(TextBlock.ForegroundProperty, edge);
        Glyph.Text = glyph;
        MessageText.Text = message;
        _action = action;
        ActionButton.Content = actionLabel;
        ActionButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;

        var wasHidden = Visibility != Visibility.Visible;
        Visibility = Visibility.Visible;
        if (wasHidden) Transitions.PopIn(Root);
    }

    // Network failures offer Retry; anything else shows the server's message.
    public void ShowError(ApiError error, Action? retry = null) =>
        Show(InfoSeverity.Error, error.Display, error.IsNetwork && retry is not null ? "Retry" : null, error.IsNetwork ? retry : null);

    public void Hide() => Visibility = Visibility.Collapsed;

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _action;
        Hide();
        action?.Invoke();
    }
}
```

- [ ] **Step 7: Run the tests and build**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ThemeTests"`
Expected: PASS (2 tests). Then `dotnet build clients/Secucode.Desktop` — expected: 0 errors (the old windows still compile; the theme is merged into `App.xaml` in Task 8).

- [ ] **Step 8: Commit**

```bash
git add clients/Secucode.Desktop/Theme clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): hand-built Fluent-style theme with light/dark tokens, controls and motion

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: App startup, shell window, sign-in, settings — and the old UI removed

**Files:**
- Modify: `clients/Secucode.Desktop/App.xaml`, `clients/Secucode.Desktop/App.xaml.cs`, `clients/Secucode.Desktop/Secucode.csproj`
- Create: `clients/Secucode.Desktop/Shell/NavItem.cs`, `Shell/TitleBar.xaml(.cs)`, `Shell/ShellWindow.xaml(.cs)`, `Features/Login/LoginWindow.xaml(.cs)`, `Features/Settings/SettingsPage.xaml(.cs)`
- Delete (old MySQL UI): `MainWindow.xaml(.cs)`, `StudentLoginWindow.xaml(.cs)`, `StudentLoginViewModel.cs`, `TeacherLoginWindow.xaml(.cs)`, `TeacherLoginViewModel.cs`, `StudentDashboard.xaml(.cs)`, `PinVerificationWindow.xaml(.cs)`, `StudentTestDetailsPage.xaml(.cs)`, `TestPage.xaml(.cs)`, `TeacherMainWindow.xaml(.cs)`, `TeacherTestsPage.xaml(.cs)`, `TeacherExamFormWindow.xaml(.cs)`, `TestDetailsPage.xaml(.cs)`, `BooleanToTextConverter.cs`, `BooleanToVisibilityConverter.cs`, `Db.cs`, `Exam.cs`, `Test.cs`, `User.cs`, `CodeExecutor.cs` (all in `clients/Secucode.Desktop/`)
- Modify: `clients/Secucode.Desktop.Checks/Secucode.Checks.csproj`, `clients/Secucode.Desktop.Checks/Program.cs`

**Interfaces:**
- Consumes: `AppSettings`, `ApiClient`, `ApiError` (Tasks 2–4), `Session` (Task 5), `ThemeManager`, `Transitions`, `InfoBar` (Task 7).
- Produces:
  - `partial class App { static AppSettings Settings; static ApiClient Api; static Session? Session; static void UseServer(string baseUrl); static void SignedIn(MeResponse); static void SignOut(string? message, bool callServer = true); }` (namespace `Secucode.Desktop`)
  - `record NavItem(string Glyph, string Label, Func<FrameworkElement> Create)`; `partial class ShellWindow(Session) { static ShellWindow? Current; void Navigate(FrameworkElement page); }` (namespace `Secucode.Desktop.Shell`)
  - `partial class TitleBar : UserControl` (minimize / maximize-restore / close; hides maximize when the window can't resize)
  - `partial class LoginWindow(string? message)` (namespace `Secucode.Desktop.Features.Login`); `partial class SettingsPage` (namespace `Secucode.Desktop.Features.Settings`)
- The **Exams** sidebar entry shows a temporary title until Tasks 11 and 12 replace it with the student and staff pages.

The old screens cannot work any more (they read MySQL directly), so they are deleted here instead of left to rot; their proctoring logic is re-created in Task 9 from the code in this plan.

- [ ] **Step 1: Delete the old UI and its packages**

```bash
cd clients/Secucode.Desktop
git rm MainWindow.xaml MainWindow.xaml.cs StudentLoginWindow.xaml StudentLoginWindow.xaml.cs StudentLoginViewModel.cs \
  TeacherLoginWindow.xaml TeacherLoginWindow.xaml.cs TeacherLoginViewModel.cs StudentDashboard.xaml StudentDashboard.xaml.cs \
  PinVerificationWindow.xaml PinVerificationWindow.xaml.cs StudentTestDetailsPage.xaml StudentTestDetailsPage.xaml.cs \
  TestPage.xaml TestPage.xaml.cs TeacherMainWindow.xaml TeacherMainWindow.xaml.cs TeacherTestsPage.xaml TeacherTestsPage.xaml.cs \
  TeacherExamFormWindow.xaml TeacherExamFormWindow.xaml.cs TestDetailsPage.xaml TestDetailsPage.xaml.cs \
  BooleanToTextConverter.cs BooleanToVisibilityConverter.cs Db.cs Exam.cs Test.cs User.cs CodeExecutor.cs
cd ../..
```

In `clients/Secucode.Desktop/Secucode.csproj`: delete the `MySql.Data` and `BCrypt.Net-Next` package references, and change `<Content Include="secucode.ico" />` to `<Resource Include="secucode.ico" />` (the title bar shows it through a pack URI).

- [ ] **Step 2: Strip the checks project of the deleted code**

`clients/Secucode.Desktop.Checks/Secucode.Checks.csproj` — remove the `ItemGroup` with the two `<Compile Include=...>` links and the `MySql.Data` package reference. Replace `clients/Secucode.Desktop.Checks/Program.cs` with:

```csharp
using Emgu.CV;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;

// Hardware and native-library smoke checks for the desktop app. Run: dotnet run --project clients/Secucode.Desktop.Checks
int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : "  -> " + detail)}");
    if (!ok) failures++;
}

// --- Face recognizer (native contrib module must be present) ---
try
{
    var rnd = new Random(1);
    Image<Gray, byte> Noise() { var i = new Image<Gray, byte>(100, 100); for (int y = 0; y < 100; y++) for (int x = 0; x < 100; x++) i.Data[y, x, 0] = (byte)rnd.Next(256); return i; }
    using var a = Noise();
    using var b = Noise();
    using var rec = new LBPHFaceRecognizer();
    using var imgs = new VectorOfMat(a.Mat);
    using var labels = new VectorOfInt(new[] { 1 });
    rec.Train(imgs, labels);
    double same = rec.Predict(a).Distance, other = rec.Predict(b).Distance;
    Check("LBPH same image closer than different", same < other, $"same={same:F1} other={other:F1}");
}
catch (Exception ex) { Check("LBPH loads", false, ex.Message); }

string cascade = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Secucode.Desktop", "haarcascade_frontalface_default.xml");
try { using var c = new CascadeClassifier(cascade); Check("haar cascade loads", true); }
catch (Exception ex) { Check("haar cascade loads", false, ex.Message); }

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
```

- [ ] **Step 3: Rewrite `App`**

`clients/Secucode.Desktop/App.xaml`:

```xml
<Application x:Class="Secucode.Desktop.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- ThemeManager swaps this first dictionary between Colors.Light and Colors.Dark. -->
                <ResourceDictionary Source="Theme/Colors.Light.xaml"/>
                <ResourceDictionary Source="Theme/Controls.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`clients/Secucode.Desktop/App.xaml.cs`:

```csharp
using System.Net.Http;
using System.Windows;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Login;
using Secucode.Desktop.Shell;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = null!;
    public static ApiClient Api { get; private set; } = null!;
    public static Session? Session { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "SecuCode", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        Settings = AppSettings.Load();
        ThemeManager.Apply(Settings.Theme);
        ThemeManager.FollowSystem();
        UseServer(Settings.ApiBaseUrl);
        new LoginWindow(null).Show();
    }

    // A fresh client for a (new) server address. Only called while nobody is signed in.
    public static void UseServer(string baseUrl)
    {
        Settings.ApiBaseUrl = baseUrl;
        Api = new ApiClient(new HttpClient { BaseAddress = Settings.BaseUri, Timeout = TimeSpan.FromSeconds(30) });
        Api.SessionExpired += (_, _) => Current.Dispatcher.BeginInvoke(() =>
            SignOut("Your session expired. Please sign in again.", callServer: false));
    }

    public static void SignedIn(MeResponse me)
    {
        Session = new Session(me);
        new ShellWindow(Session).Show();
    }

    // Shows the sign-in window and closes every other window.
    public static async void SignOut(string? message, bool callServer = true)
    {
        if (callServer) await Api.LogoutAsync();
        Session = null;
        if (Current.Windows.OfType<LoginWindow>().Any()) return;
        var login = new LoginWindow(message);
        login.Show();
        foreach (var window in Current.Windows.OfType<Window>().Where(w => w != login).ToList())
            window.Close();
    }
}
```

- [ ] **Step 4: Title bar and shell**

`clients/Secucode.Desktop/Shell/NavItem.cs`:

```csharp
using System.Windows;

namespace Secucode.Desktop.Shell;

// One sidebar entry. Create builds a fresh page each time it is selected.
public sealed record NavItem(string Glyph, string Label, Func<FrameworkElement> Create);
```

`clients/Secucode.Desktop/Shell/TitleBar.xaml`:

```xml
<UserControl x:Class="Secucode.Desktop.Shell.TitleBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Height="40">
    <DockPanel Background="Transparent">
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" WindowChrome.IsHitTestVisibleInChrome="True">
            <Button Style="{StaticResource CaptionButton}" Content="&#xE921;" Click="Minimize_Click" ToolTip="Minimize"/>
            <Button x:Name="MaximizeButton" Style="{StaticResource CaptionButton}" Content="&#xE922;" Click="Maximize_Click" ToolTip="Maximize"/>
            <Button Style="{StaticResource CloseCaptionButton}" Content="&#xE8BB;" Click="Close_Click" ToolTip="Close"/>
        </StackPanel>
        <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="14,0">
            <Image Source="/secucode.ico" Width="16" Height="16" Margin="0,0,10,0"/>
            <TextBlock Text="SecuCode" Style="{StaticResource Caption}"/>
        </StackPanel>
    </DockPanel>
</UserControl>
```

`clients/Secucode.Desktop/Shell/TitleBar.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace Secucode.Desktop.Shell;

// Custom caption for WindowChrome windows: min / max-restore / close.
public partial class TitleBar : UserControl
{
    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this)!;
            if (_window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
                MaximizeButton.Visibility = Visibility.Collapsed;
            _window.StateChanged += (_, _) => Sync();
            Sync();
        };
    }

    private void Sync()
    {
        var maximized = _window!.WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        // A maximised WindowChrome window overhangs the screen by its resize border; pad it back in.
        _window.BorderThickness = maximized ? new Thickness(7) : new Thickness(0);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(_window!);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (_window!.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(_window);
        else SystemCommands.MaximizeWindow(_window);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(_window!);
}
```

`clients/Secucode.Desktop/Shell/ShellWindow.xaml`:

```xml
<Window x:Class="Secucode.Desktop.Shell.ShellWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:Secucode.Desktop.Shell"
        Style="{StaticResource AppWindow}" Title="SecuCode" Width="1200" Height="780" MinWidth="960" MinHeight="620"
        WindowStartupLocation="CenterScreen">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="40" ResizeBorderThickness="6" GlassFrameThickness="0" CornerRadius="0" UseAeroCaptionButtons="False"/>
    </WindowChrome.WindowChrome>
    <DockPanel>
        <shell:TitleBar DockPanel.Dock="Top" Background="{DynamicResource SurfaceSunken}"/>
        <Border x:Name="Sidebar" DockPanel.Dock="Left" Width="232" Background="{DynamicResource SurfaceSunken}">
            <DockPanel Margin="8,4,8,12">
                <Button DockPanel.Dock="Top" Style="{StaticResource IconButton}" Content="&#xE700;" HorizontalAlignment="Left"
                        Margin="4,0,0,8" Click="ToggleSidebar_Click" ToolTip="Show or hide labels"/>
                <Border x:Name="UserCard" DockPanel.Dock="Bottom" Padding="12,10" CornerRadius="8" Background="{DynamicResource SubtleHover}">
                    <StackPanel>
                        <TextBlock x:Name="UserName" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                        <TextBlock x:Name="UserRoles" Style="{StaticResource Caption}" TextTrimming="CharacterEllipsis"/>
                    </StackPanel>
                </Border>
                <Grid>
                    <ListBox x:Name="Nav" Style="{StaticResource NavList}" SelectionChanged="Nav_SelectionChanged"/>
                    <!-- Selection indicator; slides to the selected item (Transitions.SlideTo). -->
                    <Border Width="3" Height="18" CornerRadius="2" Background="{DynamicResource Accent}" IsHitTestVisible="False"
                            HorizontalAlignment="Left" VerticalAlignment="Top" Margin="0,11,0,0">
                        <Border.RenderTransform>
                            <TranslateTransform x:Name="IndicatorMove"/>
                        </Border.RenderTransform>
                    </Border>
                </Grid>
            </DockPanel>
        </Border>
        <ContentControl x:Name="Host" Margin="32,24" Focusable="False"/>
    </DockPanel>
</Window>
```

`clients/Secucode.Desktop/Shell/ShellWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Features.Settings;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Shell;

// Main window after sign-in: sidebar navigation on the left, the current page on the right.
public partial class ShellWindow : Window
{
    public ShellWindow(Session session)
    {
        InitializeComponent();
        UserName.Text = session.User.FullName;
        UserRoles.Text = session.RoleNames;
        Nav.ItemsSource = new[]
        {
            new NavItem("", "Exams", () => new TextBlock { Text = "Exams", Style = (Style)FindResource("Title") }),
            new NavItem("", "Settings", () => new SettingsPage()),
        };
        Loaded += (_, _) => Nav.SelectedIndex = 0;
    }

    public static ShellWindow? Current => Application.Current.Windows.OfType<ShellWindow>().FirstOrDefault();

    public void Navigate(FrameworkElement page)
    {
        Host.Content = page;
        Transitions.PageIn(page);
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        Navigate(item.Create());
        if (Nav.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row)
            Transitions.SlideTo(IndicatorMove, row.TranslatePoint(new Point(0, 0), Nav).Y);
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        var collapse = Sidebar.Width > 100;
        Sidebar.Width = collapse ? 60 : 232;
        UserCard.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

- [ ] **Step 5: Sign-in window**

`clients/Secucode.Desktop/Features/Login/LoginWindow.xaml`:

```xml
<Window x:Class="Secucode.Desktop.Features.Login.LoginWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:Secucode.Desktop.Shell"
        xmlns:theme="clr-namespace:Secucode.Desktop.Theme"
        Style="{StaticResource AppWindow}" Title="Sign in · SecuCode" Width="460" Height="640"
        ResizeMode="CanMinimize" WindowStartupLocation="CenterScreen">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="40" ResizeBorderThickness="0" GlassFrameThickness="0" UseAeroCaptionButtons="False"/>
    </WindowChrome.WindowChrome>
    <DockPanel>
        <shell:TitleBar DockPanel.Dock="Top"/>
        <Border x:Name="Card" Style="{StaticResource Card}" Margin="32,8,32,32" Padding="32">
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="SecuCode" Style="{StaticResource Title}"/>
                <TextBlock Text="Sign in to continue" Style="{StaticResource Caption}" FontSize="14" Margin="0,4,0,24"/>
                <TextBlock Text="Username" Style="{StaticResource Caption}" Margin="0,0,0,6"/>
                <TextBox x:Name="Username"/>
                <TextBlock Text="Password" Style="{StaticResource Caption}" Margin="0,14,0,6"/>
                <PasswordBox x:Name="Password"/>
                <theme:InfoBar x:Name="Info" Margin="0,16,0,0"/>
                <Button x:Name="SignInButton" Content="Sign in" Style="{StaticResource PrimaryButton}" IsDefault="True"
                        Margin="0,20,0,0" Click="SignIn_Click"/>
                <Button Content="Server settings" Style="{StaticResource SubtleButton}" HorizontalAlignment="Left"
                        Margin="-8,16,0,0" Foreground="{DynamicResource TextSecondary}" Click="ToggleServer_Click"/>
                <StackPanel x:Name="ServerPanel" Visibility="Collapsed" Margin="0,6,0,0">
                    <TextBox x:Name="ServerAddress"/>
                    <TextBlock Text="The SecuCode server address, for example http://localhost:8080" Style="{StaticResource Caption}" Margin="0,6,0,0"/>
                </StackPanel>
            </StackPanel>
        </Border>
    </DockPanel>
</Window>
```

`clients/Secucode.Desktop/Features/Login/LoginWindow.xaml.cs`:

```csharp
using System.Windows;
using Secucode.Desktop.Api;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Login;

// One sign-in for everyone; the server's answer decides between the student and staff experience.
public partial class LoginWindow : Window
{
    public LoginWindow(string? message)
    {
        InitializeComponent();
        ServerAddress.Text = App.Settings.ApiBaseUrl;
        if (message is not null) Info.Show(InfoSeverity.Warning, message);
        Loaded += (_, _) =>
        {
            Transitions.PopIn(Card);
            Username.Focus();
        };
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        Info.Hide();
        var address = ServerAddress.Text.Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ServerPanel.Visibility = Visibility.Visible;
            Info.Show(InfoSeverity.Error, "Enter the server address as http://host:port.");
            return;
        }
        if (address != App.Settings.ApiBaseUrl)
        {
            App.UseServer(address);
            App.Settings.Save();
        }

        SignInButton.IsEnabled = false;
        try
        {
            var me = await App.Api.LoginAsync(Username.Text.Trim(), Password.Password);
            App.SignedIn(me);
            Close();
        }
        catch (ApiError error)
        {
            Info.ShowError(error);
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void ToggleServer_Click(object sender, RoutedEventArgs e) =>
        ServerPanel.Visibility = ServerPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
}
```

- [ ] **Step 6: Settings page**

`clients/Secucode.Desktop/Features/Settings/SettingsPage.xaml`:

```xml
<UserControl x:Class="Secucode.Desktop.Features.Settings.SettingsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel MaxWidth="680" HorizontalAlignment="Left">
        <TextBlock Text="Settings" Style="{StaticResource Title}" Margin="0,0,0,24"/>
        <Border Style="{StaticResource Card}" Margin="0,0,0,12">
            <DockPanel>
                <ComboBox x:Name="ThemeChoiceBox" DockPanel.Dock="Right" Width="160" VerticalAlignment="Center" SelectionChanged="Theme_Changed"/>
                <StackPanel>
                    <TextBlock Text="App theme" FontWeight="SemiBold"/>
                    <TextBlock Text="System follows your Windows setting." Style="{StaticResource Caption}" Margin="0,2,0,0"/>
                </StackPanel>
            </DockPanel>
        </Border>
        <Border Style="{StaticResource Card}" Margin="0,0,0,12">
            <StackPanel>
                <TextBlock Text="Server" FontWeight="SemiBold"/>
                <TextBlock x:Name="ServerText" Margin="0,2,0,0"/>
                <TextBlock Text="To change it, sign out and use Server settings on the sign-in screen." Style="{StaticResource Caption}" Margin="0,2,0,0"/>
            </StackPanel>
        </Border>
        <Border Style="{StaticResource Card}">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Sign out" VerticalAlignment="Center" Click="SignOut_Click"/>
                <StackPanel>
                    <TextBlock x:Name="SignedInAs" FontWeight="SemiBold"/>
                    <TextBlock x:Name="VersionText" Style="{StaticResource Caption}" Margin="0,2,0,0"/>
                </StackPanel>
            </DockPanel>
        </Border>
    </StackPanel>
</UserControl>
```

`clients/Secucode.Desktop/Features/Settings/SettingsPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Settings;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        ThemeChoiceBox.ItemsSource = Enum.GetValues<ThemeChoice>();
        ThemeChoiceBox.SelectedItem = App.Settings.Theme;
        ServerText.Text = App.Settings.ApiBaseUrl;
        SignedInAs.Text = $"Signed in as {App.Session?.User.FullName} ({App.Session?.User.Username})";
        VersionText.Text = $"SecuCode {typeof(App).Assembly.GetName().Version}";
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeChoiceBox.SelectedItem is not ThemeChoice choice || choice == App.Settings.Theme) return;
        App.Settings.Theme = choice;
        App.Settings.Save();
        ThemeManager.Apply(choice);
    }

    private void SignOut_Click(object sender, RoutedEventArgs e) => App.SignOut(null);
}
```

- [ ] **Step 7: Build and run the tests**

Run: `dotnet build Secucode.sln` — expected: 0 errors. If anything still references a deleted type, it is leftover old code: delete the reference, don't restore the type.
Run: `dotnet test clients/Secucode.Desktop.Tests` — expected: PASS.

- [ ] **Step 8: Check it by running the app**

Start the API (see README "Run the API"): `dotnet run --project server/Secucode.Api -- seed` once, then `dotnet run --project server/Secucode.Api --urls http://localhost:8080`. Run the app: `dotnet run --project clients/Secucode.Desktop`.
Use the `screenshotting-to-verify` skill to capture and check: the sign-in window; a wrong password (red InfoBar with "Invalid username or password."); a stopped API (InfoBar "Can't reach the server…"); signing in as `faculty.cse / Faculty@123` (shell with sidebar, name and role, the temporary Exams title); Settings → Dark (whole window switches immediately), → Light, → System; sidebar collapse; maximize/restore (no content cut off at the screen edges); Sign out (back to sign-in). Fix anything that looks broken before committing.

- [ ] **Step 9: Commit**

```bash
git add -A clients/Secucode.Desktop clients/Secucode.Desktop.Checks
git commit -m "feat(desktop): themed shell, sign-in and settings on the API; remove the MySQL UI

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Proctoring hardware, without UI or network

**Files:**
- Create: `clients/Secucode.Desktop/Proctoring/Images.cs`, `FaceMonitor.cs`, `FaceReference.cs`, `AudioRecorder.cs`, `Lockdown.cs`, `ScreenCapture.cs`
- Modify: `clients/Secucode.Desktop.Checks/Secucode.Checks.csproj`, `clients/Secucode.Desktop.Checks/Program.cs`

**Interfaces:**
- Produces (namespace `Secucode.Desktop.Proctoring`):
  - `static class Images { byte[] Encode(IInputArray image, string extension /* ".jpg" | ".png" */); BitmapImage ToBitmap(byte[] encoded); }`
  - `enum FaceCheck { Ok, NoFrame, Missing, Multiple, Mismatch }`; `sealed class FaceMonitor : IDisposable { const int CheckSeconds = 3; static CascadeClassifier? LoadDetector(); static FaceMonitor? TryStart(byte[]? referencePng); (FaceCheck Result, byte[]? Jpeg) Check(); byte[]? CaptureJpeg(); }` — `TryStart` returns null when the webcam or model is unavailable. `Check` returns the frame as JPEG only when the result is `Missing`, `Multiple` or `Mismatch`. **Create and use a `FaceMonitor` on the UI thread** (DirectShow capture is apartment-bound).
  - `record FaceCapture(byte[] Png, BitmapImage Preview)`; `static class FaceReference { FaceCapture Capture(); }` — throws `InvalidOperationException` with a message fit for the student; safe to call on a background thread (it opens and closes its own camera).
  - `sealed class AudioRecorder : IDisposable { static TimeSpan ChunkLength = 60 s; event Action<byte[]>? ChunkReady; bool Start(out string? error); void Stop(); }` — `Stop` emits the last partial chunk; `ChunkReady` fires on a background thread.
  - `static class Lockdown { void Enable(); void Disable(); bool ForegroundIsOurProcess(); }`
  - `static class ScreenCapture { byte[]? Jpeg(); }`

All of this is ported from the deleted `TestPage`/`StudentTestDetailsPage` (same thresholds, same keyboard hook). It is checked by `Secucode.Desktop.Checks` because it needs real hardware.

- [ ] **Step 1: Write the hardware checks first**

`clients/Secucode.Desktop.Checks/Secucode.Checks.csproj` — add `<UseWPF>true</UseWPF>` to the `PropertyGroup` and:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Secucode.Desktop\Secucode.csproj" />
  </ItemGroup>
```

In `clients/Secucode.Desktop.Checks/Program.cs`, add `using Secucode.Desktop.Proctoring;` at the top, add this helper after `Check`:

```csharp
void Skip(string name, string why) => Console.WriteLine($"SKIP  {name} ({why})");
```

and insert before the final `Console.WriteLine`:

```csharp
// --- Image encoding ---
using (var img = new Image<Bgr, byte>(64, 64, new Bgr(40, 80, 120)))
{
    var jpg = Images.Encode(img, ".jpg");
    var png = Images.Encode(img, ".png");
    Check("jpeg encodes", jpg.Length > 2 && jpg[0] == 0xFF && jpg[1] == 0xD8);
    Check("png encodes", png.Length > 4 && png[0] == 0x89 && png[1] == 0x50);
}

// --- Webcam (needs a camera; run on a real PC) ---
var monitor = FaceMonitor.TryStart(null);
if (monitor is null) Skip("webcam", "no camera or model");
else
    using (monitor)
    {
        var (result, _) = monitor.Check();
        Check("webcam check runs", Enum.IsDefined(result), result.ToString());
        Check("webcam frame captures", monitor.CaptureJpeg() is { Length: > 2 } f && f[0] == 0xFF);
    }

// --- Microphone (needs a mic) ---
using (var audio = new AudioRecorder())
{
    byte[]? chunk = null;
    audio.ChunkReady += c => chunk = c;
    if (!audio.Start(out var audioError)) Skip("microphone", audioError ?? "unavailable");
    else
    {
        Thread.Sleep(1500);
        audio.Stop();
        Check("audio chunk is a WAV", chunk is { Length: > 44 } && chunk[0] == (byte)'R' && chunk[1] == (byte)'I', $"len={chunk?.Length}");
    }
}

// --- Screenshot ---
Check("screenshot is a JPEG", ScreenCapture.Jpeg() is { Length: > 2 } s && s[0] == 0xFF && s[1] == 0xD8);
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project clients/Secucode.Desktop.Checks`
Expected: build errors — `Secucode.Desktop.Proctoring` not found.

- [ ] **Step 3: Implement the proctoring classes**

`clients/Secucode.Desktop/Proctoring/Images.cs`:

```csharp
using System.IO;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Util;

namespace Secucode.Desktop.Proctoring;

public static class Images
{
    public static byte[] Encode(IInputArray image, string extension)
    {
        using var buffer = new VectorOfByte();
        CvInvoke.Imencode(extension, image, buffer);
        return buffer.ToArray();
    }

    public static BitmapImage ToBitmap(byte[] encoded)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(encoded);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze(); // usable from any thread
        return bitmap;
    }
}
```

`clients/Secucode.Desktop/Proctoring/FaceMonitor.cs`:

```csharp
using System.Drawing;
using System.IO;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace Secucode.Desktop.Proctoring;

public enum FaceCheck { Ok, NoFrame, Missing, Multiple, Mismatch }

// Webcam presence and identity checks, one frame per call. No UI and no network.
public sealed class FaceMonitor : IDisposable
{
    public const int CheckSeconds = 3;
    private const int DetectMinNeighbors = 5;
    // ponytail: LBPH distance cutoff (lower = same person). Calibrate on real webcams/lighting; ~60-100 is typical.
    private const double MatchMaxDistance = 90;

    private readonly VideoCapture _capture;
    private readonly CascadeClassifier _detector;
    private readonly LBPHFaceRecognizer? _recognizer;

    private FaceMonitor(VideoCapture capture, CascadeClassifier detector, LBPHFaceRecognizer? recognizer)
    {
        _capture = capture;
        _detector = detector;
        _recognizer = recognizer;
    }

    public static CascadeClassifier? LoadDetector()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "haarcascade_frontalface_default.xml");
        return File.Exists(path) ? new CascadeClassifier(path) : null;
    }

    // Null when the webcam or the detection model is unavailable: the exam must not start then.
    public static FaceMonitor? TryStart(byte[]? referencePng)
    {
        var detector = LoadDetector();
        if (detector is null) return null;
        var capture = new VideoCapture(0, VideoCapture.API.DShow);
        if (!capture.IsOpened)
        {
            capture.Dispose();
            detector.Dispose();
            return null;
        }
        capture.Set(CapProp.FrameWidth, 320);
        capture.Set(CapProp.FrameHeight, 240);
        return new FaceMonitor(capture, detector, referencePng is null ? null : Train(referencePng));
    }

    private static LBPHFaceRecognizer Train(byte[] referencePng)
    {
        using var mat = new Mat();
        CvInvoke.Imdecode(referencePng, ImreadModes.Grayscale, mat);
        using var gray = mat.ToImage<Gray, byte>();
        using var face = Prepare(gray); // the reference is already a face crop
        var recognizer = new LBPHFaceRecognizer();
        using var images = new VectorOfMat(face.Mat);
        using var labels = new VectorOfInt([1]);
        recognizer.Train(images, labels);
        return recognizer;
    }

    private static Image<Gray, byte> Prepare(Image<Gray, byte> face)
    {
        var resized = face.Resize(100, 100, Inter.Linear);
        resized._EqualizeHist(); // less sensitive to lighting
        return resized;
    }

    // One check. The frame comes back as JPEG evidence when the check fails.
    public (FaceCheck Result, byte[]? Jpeg) Check()
    {
        using var frame = new Mat();
        if (!_capture.Read(frame) || frame.IsEmpty) return (FaceCheck.NoFrame, null); // a dropped frame is not the student's fault

        using var image = frame.ToImage<Bgr, byte>();
        using var gray = image.Convert<Gray, byte>();
        var faces = _detector.DetectMultiScale(gray, 1.1, DetectMinNeighbors);
        var result = faces.Length switch
        {
            0 => FaceCheck.Missing,
            > 1 => FaceCheck.Multiple,
            _ => Matches(gray, faces[0]) ? FaceCheck.Ok : FaceCheck.Mismatch,
        };
        return (result, result == FaceCheck.Ok ? null : Images.Encode(image, ".jpg"));
    }

    private bool Matches(Image<Gray, byte> gray, Rectangle face)
    {
        if (_recognizer is null) return true; // no reference: presence-only monitoring
        using var crop = gray.Copy(face);
        using var live = Prepare(crop);
        return _recognizer.Predict(live).Distance <= MatchMaxDistance;
    }

    public byte[]? CaptureJpeg()
    {
        using var frame = new Mat();
        return _capture.Read(frame) && !frame.IsEmpty ? Images.Encode(frame, ".jpg") : null;
    }

    public void Dispose()
    {
        _capture.Dispose();
        _detector.Dispose();
        _recognizer?.Dispose();
    }
}
```

`clients/Secucode.Desktop/Proctoring/FaceReference.cs`:

```csharp
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Structure;

namespace Secucode.Desktop.Proctoring;

public sealed record FaceCapture(byte[] Png, BitmapImage Preview);

// Captures the student's reference face before the exam. Opens and closes its own camera.
public static class FaceReference
{
    public static FaceCapture Capture()
    {
        using var detector = FaceMonitor.LoadDetector()
            ?? throw new InvalidOperationException("The face detection model is missing. Reinstall SecuCode.");
        using var capture = new VideoCapture(0, VideoCapture.API.DShow);
        if (!capture.IsOpened)
            throw new InvalidOperationException("Couldn't open the webcam. Close other apps that use the camera and try again.");

        // Cameras start dark while they adjust: try for about 3 seconds.
        for (var i = 0; i < 30; i++)
        {
            using var frame = capture.QueryFrame();
            if (frame is null || frame.IsEmpty)
            {
                Thread.Sleep(100);
                continue;
            }
            using var image = frame.ToImage<Bgr, byte>();
            using var gray = image.Convert<Gray, byte>();
            var faces = detector.DetectMultiScale(gray, 1.1, 10, System.Drawing.Size.Empty);
            if (faces.Length == 0) continue;

            using var face = image.Copy(faces[0]);
            var png = Images.Encode(face, ".png");
            return new FaceCapture(png, Images.ToBitmap(png));
        }
        throw new InvalidOperationException("No face detected. Face the camera in good light and try again.");
    }
}
```

`clients/Secucode.Desktop/Proctoring/AudioRecorder.cs`:

```csharp
using System.IO;
using NAudio.Utils;
using NAudio.Wave;

namespace Secucode.Desktop.Proctoring;

// Microphone recording in 60 s WAV chunks (16 kHz mono, about 1.9 MB each: well under the 10 MB evidence limit).
public sealed class AudioRecorder : IDisposable
{
    public static readonly TimeSpan ChunkLength = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private WaveInEvent? _input;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private Timer? _rotate;
    private bool _stopped;

    // Raised on a background thread with a complete WAV file.
    public event Action<byte[]>? ChunkReady;

    // False when there is no usable microphone; the caller records AudioUnavailable instead of blocking the exam.
    public bool Start(out string? error)
    {
        try
        {
            _input = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1) };
            _input.DataAvailable += (_, a) => { lock (_lock) _writer?.Write(a.Buffer, 0, a.BytesRecorded); };
            lock (_lock) NewChunkLocked();
            _input.StartRecording();
            _rotate = new Timer(_ => Rotate(), null, ChunkLength, ChunkLength);
            error = null;
            return true;
        }
        catch (Exception e)
        {
            _input?.Dispose();
            _input = null;
            error = e.Message;
            return false;
        }
    }

    // Stops recording and emits the last, partial chunk.
    public void Stop()
    {
        _rotate?.Dispose();
        _rotate = null;
        _input?.StopRecording();
        byte[]? last;
        lock (_lock)
        {
            _stopped = true;
            last = FinishChunkLocked();
        }
        if (last is not null) ChunkReady?.Invoke(last);
    }

    private void Rotate()
    {
        byte[]? done;
        lock (_lock)
        {
            if (_stopped) return;
            done = FinishChunkLocked();
            NewChunkLocked();
        }
        if (done is not null) ChunkReady?.Invoke(done);
    }

    private void NewChunkLocked()
    {
        _buffer = new MemoryStream();
        _writer = new WaveFileWriter(new IgnoreDisposeStream(_buffer), _input!.WaveFormat);
    }

    private byte[]? FinishChunkLocked()
    {
        if (_writer is null || _buffer is null) return null;
        _writer.Dispose(); // writes the final sizes into the WAV header
        var bytes = _buffer.ToArray();
        _writer = null;
        _buffer = null;
        return bytes.Length > 44 ? bytes : null; // header only: nothing was recorded
    }

    public void Dispose()
    {
        Stop();
        _input?.Dispose();
        _input = null;
    }
}
```

`clients/Secucode.Desktop/Proctoring/Lockdown.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Secucode.Desktop.Proctoring;

// Low-level keyboard hook that swallows Alt+Tab, Alt+Esc, Ctrl+Esc, the Windows keys and Alt+F4.
// Ctrl+Alt+Del can't be blocked by any app; the focus loss it causes is caught by the exam window.
public static class Lockdown
{
    // Kept in a static field so the GC can't collect the delegate while Windows still calls it.
    private static readonly LowLevelKeyboardProc HookProc = HookCallback;
    private static IntPtr _hookId = IntPtr.Zero;

    public static void Enable()
    {
        if (_hookId != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, HookProc, GetModuleHandle(module.ModuleName), 0);
    }

    public static void Disable()
    {
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    public static bool ForegroundIsOurProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var vk = Marshal.ReadInt32(lParam);
            var alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            var ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            if ((vk == VK_TAB && alt) || (vk == VK_ESCAPE && (alt || ctrl)) || vk == VK_LWIN || vk == VK_RWIN || (vk == VK_F4 && alt))
                return 1;
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_F4 = 0x73, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MENU = 0x12, VK_CONTROL = 0x11;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
}
```

`clients/Secucode.Desktop/Proctoring/ScreenCapture.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace Secucode.Desktop.Proctoring;

public static class ScreenCapture
{
    // ponytail: SystemParameters are in DIPs, so on a scaled display this captures the top-left part of the desktop
    // (same as the old app). Switch to per-monitor pixel bounds if staff need the full screen.
    public static byte[]? Jpeg()
    {
        try
        {
            var left = (int)SystemParameters.VirtualScreenLeft;
            var top = (int)SystemParameters.VirtualScreenTop;
            using var bitmap = new Bitmap((int)SystemParameters.VirtualScreenWidth, (int)SystemParameters.VirtualScreenHeight);
            using (var g = Graphics.FromImage(bitmap))
                g.CopyFromScreen(left, top, 0, 0, bitmap.Size);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Jpeg);
            return stream.ToArray();
        }
        catch (Exception)
        {
            return null; // evidence is best-effort; the submission must not fail because of it
        }
    }
}
```

- [ ] **Step 4: Run the checks**

Run: `dotnet run --project clients/Secucode.Desktop.Checks`
Expected on a PC with webcam and microphone: every line PASS. Without one of them: that line is SKIP, the rest PASS. Also run `dotnet test clients/Secucode.Desktop.Tests` — expected PASS.

- [ ] **Step 5: Commit**

```bash
git add clients/Secucode.Desktop/Proctoring clients/Secucode.Desktop.Checks
git commit -m "feat(desktop): proctoring hardware (face, audio chunks, lockdown, screenshots) without UI

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: The exam — `ExamSession` rules and the proctored `ExamWindow`

**Files:**
- Create: `clients/Secucode.Desktop/Features/Student/ExamSession.cs`, `clients/Secucode.Desktop/Features/Student/ExamWindow.xaml`, `ExamWindow.xaml.cs`
- Modify: `clients/Secucode.Desktop/App.xaml.cs` (`SignOut` must not close the exam window)
- Test: `clients/Secucode.Desktop.Tests/ExamSessionTests.cs`

**Interfaces:**
- Consumes: `AttemptOutbox` (Task 6), `ExamClock` (Task 5), `ApiClient`, `LiveConnection` (Task 4), `FaceMonitor`, `FaceCheck`, `AudioRecorder`, `Lockdown`, `ScreenCapture` (Task 9), `InfoBar`, `Transitions` (Task 7), Contracts `AttemptDto`, `LiveAttemptMessage`, `LiveMessages`.
- Produces:
  - `sealed class ExamSession(AttemptDto attempt, AttemptOutbox outbox, Func<DateTime> utcNow)` with `const int MaxFaceStrikes = 3, MaxFocusMisses = 3`; `AttemptDto Attempt`; `ExamClock Clock`; `int FaceStrikes`; `int FocusMisses`; `string? EndReason`; `bool Ended`; events `Action<string>? AutoSubmitRequested`, `Action<string>? Closed`; methods `SaveCode(string code, string language)`, `FaceResult(FaceCheck, byte[]? jpeg)`, `FocusLost()`, `Minimized()`, `AudioUnavailable(string detail)`, `bool Submit(string code, string language, string reason, byte[]? faceJpeg, byte[]? screenshotJpeg)`, `Abandon(string reason)`, `OnLive(string method, LiveAttemptMessage)`, `Resync(AttemptDto fresh)`.
  - `partial class ExamWindow(ApiClient api, string examTitle, AttemptDto attempt, FaceMonitor face, byte[] referencePng) : Window` — takes ownership of `face`; created on the UI thread.
- Rules (spec §5, same as today's app): 3 consecutive failed face checks → auto-submit (a passed check resets the count; a dropped frame changes nothing); 3 focus losses → auto-submit; minimizing → auto-submit; each violation queues its event (face violations with the webcam frame). Submit happens once: final webcam frame, screenshot, then the submit item. `Abandon` (signed out) never submits.

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/ExamSessionTests.cs`:

```csharp
using Secucode.Contracts;
using Secucode.Desktop.Features.Student;
using Secucode.Desktop.Outbox;
using Secucode.Desktop.Proctoring;

namespace Secucode.Desktop.Tests;

public sealed class ExamSessionTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Directory.CreateTempSubdirectory("secucode_session_").FullName;
    private AttemptOutbox _outbox = null!;

    public Task InitializeAsync()
    {
        _outbox = new AttemptOutbox(_root, 3, new FakeSender()); // never started: tests inspect the queue
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _outbox.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private static AttemptDto Attempt(AttemptStatus status = AttemptStatus.InProgress, DateTime? deadline = null) =>
        new(3, 1, 2, 1, "Reverse a string", status, ClientKind.Desktop, Now, deadline ?? Now.AddMinutes(30), null, "Python", "", 0, null, Now);

    private ExamSession New() => new(Attempt(), _outbox, () => Now);

    private List<string> Queued() => _outbox.Snapshot().Select(i => i.Kind switch
    {
        OutboxKind.Event => $"event:{i.EventType}",
        OutboxKind.Evidence => $"evidence:{i.Evidence}",
        _ => i.Kind.ToString(),
    }).ToList();

    [Fact]
    public void Three_failed_face_checks_in_a_row_request_one_auto_submit()
    {
        var session = New();
        var requests = new List<string>();
        session.AutoSubmitRequested += requests.Add;

        session.FaceResult(FaceCheck.Missing, [1]);
        session.FaceResult(FaceCheck.Ok, null);
        session.FaceResult(FaceCheck.Missing, [1]);
        session.FaceResult(FaceCheck.Multiple, [1]);
        Assert.Empty(requests);
        Assert.Equal(2, session.FaceStrikes);

        session.FaceResult(FaceCheck.Mismatch, [1]);

        Assert.StartsWith("Face check failed 3 times in a row", Assert.Single(requests));
        Assert.Equal(["event:FaceMissing", "event:FaceMissing", "event:MultipleFaces", "event:FaceMismatch"], Queued());
        Assert.All(_outbox.Snapshot(), i => Assert.Equal(EvidenceKind.Webcam, i.Evidence));
    }

    [Fact]
    public void A_dropped_frame_changes_nothing()
    {
        var session = New();
        session.FaceResult(FaceCheck.Missing, [1]);
        session.FaceResult(FaceCheck.NoFrame, null);
        Assert.Equal(1, session.FaceStrikes);
        Assert.Single(Queued());
    }

    [Fact]
    public void The_third_focus_loss_requests_auto_submit()
    {
        var session = New();
        var requests = new List<string>();
        session.AutoSubmitRequested += requests.Add;

        session.FocusLost();
        session.FocusLost();
        Assert.Empty(requests);
        session.FocusLost();

        Assert.Equal("Test window lost focus too many times.", Assert.Single(requests));
        Assert.Equal(["event:FocusLost", "event:FocusLost", "event:FocusLost"], Queued());
    }

    [Fact]
    public void Minimizing_requests_auto_submit_at_once()
    {
        var session = New();
        var requests = new List<string>();
        session.AutoSubmitRequested += requests.Add;

        session.Minimized();

        Assert.Equal("Test window was minimized.", Assert.Single(requests));
        Assert.Equal(["event:Minimized"], Queued());
    }

    [Fact]
    public void Submit_happens_once_with_final_evidence_first()
    {
        var session = New();

        Assert.True(session.Submit("print(1)", "Python", "Done", [1], [2]));
        Assert.False(session.Submit("print(2)", "Python", "Again", null, null));
        session.FaceResult(FaceCheck.Missing, [1]);
        session.SaveCode("late", "Python");

        Assert.True(session.Ended);
        Assert.Equal(["evidence:Webcam", "evidence:Screenshot", "Submit"], Queued());
        Assert.Equal(new SubmitRequest("print(1)", "Python", "Done"), _outbox.Snapshot()[^1].Submit);
    }

    [Fact]
    public void Abandon_never_submits()
    {
        var session = New();

        session.Abandon("Signed out");

        Assert.False(session.Submit("x", "Python", "Closed", null, null));
        Assert.True(session.Ended);
        Assert.Empty(Queued());
    }

    [Fact]
    public void A_live_deadline_change_moves_the_clock_for_this_attempt_only()
    {
        var session = New();

        session.OnLive(LiveMessages.DeadlineChanged, new LiveAttemptMessage(99, 1, 2, AttemptStatus.InProgress, Now.AddHours(5), 0, null));
        session.OnLive(LiveMessages.DeadlineChanged, new LiveAttemptMessage(3, 1, 2, AttemptStatus.InProgress, Now.AddMinutes(45), 0, null));

        Assert.Equal(TimeSpan.FromMinutes(45), session.Clock.Remaining(Now));
    }

    [Fact]
    public void A_voided_attempt_closes_without_submitting()
    {
        var session = New();
        string? closed = null;
        session.Closed += reason => closed = reason;

        session.OnLive(LiveMessages.AttemptVoided, new LiveAttemptMessage(3, 1, 2, AttemptStatus.Voided, Now, 0, null));

        Assert.Equal("This attempt was voided by staff.", closed);
        Assert.Empty(Queued());
    }

    [Fact]
    public void Resync_takes_the_servers_deadline_and_notices_a_closed_attempt()
    {
        var session = New();
        string? closed = null;
        session.Closed += reason => closed = reason;

        session.Resync(Attempt(deadline: Now.AddMinutes(50)));
        Assert.Equal(TimeSpan.FromMinutes(50), session.Clock.Remaining(Now));
        Assert.Null(closed);

        session.Resync(Attempt(AttemptStatus.AutoSubmitted));
        Assert.NotNull(closed);
    }

    [Fact]
    public void Code_saves_are_queued_until_the_exam_ends()
    {
        var session = New();
        session.SaveCode("a", "Python");
        session.SaveCode("b", "Java");
        Assert.Equal(new SaveCodeRequest("b", "Java"), Assert.Single(_outbox.Snapshot()).Code);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ExamSessionTests"`
Expected: build errors — `ExamSession` not found.

- [ ] **Step 3: Implement `ExamSession`**

`clients/Secucode.Desktop/Features/Student/ExamSession.cs`:

```csharp
using Secucode.Contracts;
using Secucode.Desktop.Outbox;
using Secucode.Desktop.Proctoring;

namespace Secucode.Desktop.Features.Student;

// The exam's rules, free of UI and hardware: strike counting, submit-once, and reacting to the server.
// Every write goes through the outbox, so it survives network loss and crashes.
public sealed class ExamSession(AttemptDto attempt, AttemptOutbox outbox, Func<DateTime> utcNow)
{
    public const int MaxFaceStrikes = 3;
    public const int MaxFocusMisses = 3;

    public AttemptDto Attempt { get; private set; } = attempt;
    public ExamClock Clock { get; private set; } = new(attempt.DeadlineAt, attempt.ServerTime, utcNow());
    public int FaceStrikes { get; private set; }
    public int FocusMisses { get; private set; }
    public string? EndReason { get; private set; }
    public bool Ended => EndReason is not null;

    // The window should submit with this reason.
    public event Action<string>? AutoSubmitRequested;

    // The server closed the attempt (voided, or no longer in progress): stop without submitting.
    public event Action<string>? Closed;

    public void SaveCode(string code, string language)
    {
        if (!Ended) outbox.SaveCode(code, language);
    }

    public void FaceResult(FaceCheck result, byte[]? jpeg)
    {
        if (Ended || result == FaceCheck.NoFrame) return;
        if (result == FaceCheck.Ok)
        {
            FaceStrikes = 0; // strikes count consecutive failed checks
            return;
        }
        FaceStrikes++;
        outbox.Event(result switch
        {
            FaceCheck.Missing => AttemptEventType.FaceMissing,
            FaceCheck.Multiple => AttemptEventType.MultipleFaces,
            _ => AttemptEventType.FaceMismatch,
        }, null, jpeg);
        if (FaceStrikes >= MaxFaceStrikes)
            AutoSubmitRequested?.Invoke($"Face check failed {MaxFaceStrikes} times in a row ({result}).");
    }

    public void FocusLost()
    {
        if (Ended) return;
        FocusMisses++;
        outbox.Event(AttemptEventType.FocusLost, $"{FocusMisses}/{MaxFocusMisses}");
        if (FocusMisses >= MaxFocusMisses) AutoSubmitRequested?.Invoke("Test window lost focus too many times.");
    }

    public void Minimized()
    {
        if (Ended) return;
        outbox.Event(AttemptEventType.Minimized, null);
        AutoSubmitRequested?.Invoke("Test window was minimized.");
    }

    public void AudioUnavailable(string detail)
    {
        if (!Ended) outbox.Event(AttemptEventType.AudioUnavailable, detail.Length > 500 ? detail[..500] : detail);
    }

    // Queues the final evidence, then the submission. Only the first call counts.
    public bool Submit(string code, string language, string reason, byte[]? faceJpeg, byte[]? screenshotJpeg)
    {
        if (Ended) return false;
        EndReason = reason;
        if (faceJpeg is not null) outbox.Evidence(EvidenceKind.Webcam, faceJpeg, "image/jpeg");
        if (screenshotJpeg is not null) outbox.Evidence(EvidenceKind.Screenshot, screenshotJpeg, "image/jpeg");
        outbox.Submit(new SubmitRequest(code, language, reason));
        return true;
    }

    // Signed out mid-exam: nothing is submitted, and the attempt stays resumable.
    public void Abandon(string reason) => EndReason ??= reason;

    public void OnLive(string method, LiveAttemptMessage message)
    {
        if (message.AttemptId != Attempt.Id) return;
        if (method == LiveMessages.DeadlineChanged) Clock.DeadlineUtc = message.DeadlineAt;
        else if (method == LiveMessages.AttemptVoided) Close("This attempt was voided by staff.");
    }

    // After (re)connecting: pushes may have been missed, so take the server's view.
    public void Resync(AttemptDto fresh)
    {
        Attempt = fresh;
        Clock = new ExamClock(fresh.DeadlineAt, fresh.ServerTime, utcNow());
        if (fresh.Status != AttemptStatus.InProgress) Close("This attempt is no longer in progress.");
    }

    private void Close(string reason)
    {
        if (Ended) return;
        EndReason = reason;
        Closed?.Invoke(reason);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ExamSessionTests"`
Expected: PASS (10 tests).

- [ ] **Step 5: Write the exam window**

`clients/Secucode.Desktop/Features/Student/ExamWindow.xaml`:

```xml
<Window x:Class="Secucode.Desktop.Features.Student.ExamWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:theme="clr-namespace:Secucode.Desktop.Theme"
        Style="{StaticResource AppWindow}" Title="SecuCode exam"
        WindowStyle="None" ResizeMode="NoResize" WindowState="Maximized" Topmost="True">
    <Grid>
        <DockPanel Margin="20">
            <Grid DockPanel.Dock="Top" Margin="0,0,0,12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel>
                    <TextBlock x:Name="ExamTitle" Style="{StaticResource Subtitle}"/>
                    <TextBlock Style="{StaticResource Caption}" Margin="0,2,0,0"
                               Text="Proctoring is on: your webcam, microphone and window focus are monitored."/>
                </StackPanel>
                <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0" ToolTip="Face check strikes">
                    <TextBlock Text="&#xE722;" Style="{StaticResource Icon}" Foreground="{DynamicResource TextSecondary}" Margin="0,0,8,0" VerticalAlignment="Center"/>
                    <Ellipse x:Name="Strike1" Width="10" Height="10" Margin="2" Fill="{DynamicResource BorderStrong}"/>
                    <Ellipse x:Name="Strike2" Width="10" Height="10" Margin="2" Fill="{DynamicResource BorderStrong}"/>
                    <Ellipse x:Name="Strike3" Width="10" Height="10" Margin="2" Fill="{DynamicResource BorderStrong}"/>
                </StackPanel>
                <Border Grid.Column="2" CornerRadius="8" Padding="14,6" Background="{DynamicResource SurfaceRaised}"
                        BorderBrush="{DynamicResource Border}" BorderThickness="1">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="&#xE916;" Style="{StaticResource Icon}" VerticalAlignment="Center" Margin="0,0,8,0"/>
                        <TextBlock x:Name="TimerText" FontFamily="{StaticResource CodeFont}" FontSize="20" FontWeight="SemiBold"/>
                    </StackPanel>
                </Border>
            </Grid>
            <theme:InfoBar x:Name="Banner" DockPanel.Dock="Top" Margin="0,0,0,12"/>
            <DockPanel DockPanel.Dock="Bottom" Margin="0,12,0,0">
                <Button x:Name="SubmitButton" DockPanel.Dock="Right" Content="Submit exam" Style="{StaticResource PrimaryButton}" Click="Submit_Click"/>
                <TextBlock x:Name="SavedText" Style="{StaticResource Caption}" VerticalAlignment="Center"/>
            </DockPanel>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="2*"/>
                    <ColumnDefinition Width="16"/>
                    <ColumnDefinition Width="3*"/>
                </Grid.ColumnDefinitions>
                <Border Style="{StaticResource Card}">
                    <DockPanel>
                        <TextBlock DockPanel.Dock="Top" Text="Question" Style="{StaticResource Caption}" Margin="0,0,0,8"/>
                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <TextBlock x:Name="QuestionText" Style="{StaticResource Body}" FontSize="15"/>
                        </ScrollViewer>
                    </DockPanel>
                </Border>
                <Grid Grid.Column="2">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="3*"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="2*"/>
                    </Grid.RowDefinitions>
                    <DockPanel Margin="0,0,0,8">
                        <Button x:Name="RunButton" DockPanel.Dock="Right" Click="Run_Click">
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="&#xE768;" Style="{StaticResource Icon}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"/>
                                <TextBlock Text="Run"/>
                            </StackPanel>
                        </Button>
                        <ComboBox x:Name="LanguageBox" Width="160" HorizontalAlignment="Left"/>
                    </DockPanel>
                    <TextBox x:Name="CodeEditor" Grid.Row="1" Style="{StaticResource CodeEditor}"/>
                    <Grid Grid.Row="2" Margin="0,12,0,6">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition/>
                            <ColumnDefinition Width="12"/>
                            <ColumnDefinition Width="2*"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="Input" Style="{StaticResource Caption}"/>
                        <TextBlock Grid.Column="2" Text="Output" Style="{StaticResource Caption}"/>
                    </Grid>
                    <Grid Grid.Row="3">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition/>
                            <ColumnDefinition Width="12"/>
                            <ColumnDefinition Width="2*"/>
                        </Grid.ColumnDefinitions>
                        <TextBox x:Name="StdinBox" Style="{StaticResource CodeEditor}"/>
                        <TextBox x:Name="OutputBox" Grid.Column="2" Style="{StaticResource CodeEditor}" IsReadOnly="True"
                                 Background="{DynamicResource SurfaceSunken}"/>
                    </Grid>
                </Grid>
            </Grid>
        </DockPanel>

        <!-- Shown while finishing: submitting, waiting offline, and the final outcome. -->
        <Grid x:Name="Overlay" Visibility="Collapsed" Background="{DynamicResource Surface}">
            <Border x:Name="OverlayCard" Style="{StaticResource Card}" Width="480" Padding="32"
                    HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel>
                    <TextBlock x:Name="OverlayTitle" Style="{StaticResource Subtitle}"/>
                    <TextBlock x:Name="OverlayText" Style="{StaticResource Body}" Margin="0,8,0,0" Foreground="{DynamicResource TextSecondary}"/>
                    <Button x:Name="OverlayButton" Content="Back to exams" Style="{StaticResource PrimaryButton}"
                            HorizontalAlignment="Right" Margin="0,24,0,0" Visibility="Collapsed" Click="OverlayButton_Click"/>
                </StackPanel>
            </Border>
        </Grid>
    </Grid>
</Window>
```

`clients/Secucode.Desktop/Features/Student/ExamWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Outbox;
using Secucode.Desktop.Proctoring;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Student;

// The proctored exam window. The rules live in ExamSession; this wires hardware, timers, the outbox and the hub
// to it. Every way out goes through FinishAsync, which shows the outcome before the window may close.
public partial class ExamWindow : Window
{
#if DEBUG
    private const bool DevMode = true; // focus-loss checks are off while debugging
#else
    private const bool DevMode = false;
#endif
    private static readonly string[] Languages = ["Python", "JavaScript", "Java", "C++"];

    private readonly ApiClient _api;
    private readonly FaceMonitor _face;
    private readonly AttemptOutbox _outbox;
    private readonly ExamSession _session;
    private readonly LiveConnection _live;
    private readonly AudioRecorder _audio = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _faceTimer = new() { Interval = TimeSpan.FromSeconds(FaceMonitor.CheckSeconds) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly string _localCopy;
    private bool _proctoring;
    private bool _finishing;
    private bool _canClose;

    public ExamWindow(ApiClient api, string examTitle, AttemptDto attempt, FaceMonitor face, byte[] referencePng)
    {
        InitializeComponent();
        _api = api;
        _face = face;
        _outbox = new AttemptOutbox(AttemptOutbox.DefaultRoot, attempt.Id, new ApiAttemptSender(api));
        _session = new ExamSession(attempt, _outbox, () => DateTime.UtcNow);
        _live = new LiveConnection(api.Http.BaseAddress!, api.FreshAccessTokenAsync);
        _localCopy = Path.Combine(AppSettings.DataDir, "code", $"{attempt.Id}.txt");

        ExamTitle.Text = examTitle;
        QuestionText.Text = attempt.QuestionText;
        CodeEditor.Text = attempt.Code;
        LanguageBox.ItemsSource = Languages;
        LanguageBox.SelectedItem = Languages.Contains(attempt.Language) ? attempt.Language : "Python";

        _outbox.Evidence(EvidenceKind.ReferenceFace, referencePng, "image/png");
        _outbox.OnlineChanged += online => Dispatcher.BeginInvoke(() => ShowConnection(online));
        _outbox.AttemptClosed += () => Dispatcher.BeginInvoke(() => _ = FinishAsync());
        _session.AutoSubmitRequested += reason => Dispatcher.BeginInvoke(() => Submit(reason));
        _session.Closed += _ => Dispatcher.BeginInvoke(() => _ = FinishAsync());
        _live.Received += (method, message) => Dispatcher.BeginInvoke(() => _session.OnLive(method, message));
        _live.Connected += () => Dispatcher.InvokeAsync(ResyncAsync).Task.Unwrap();
        _api.SessionExpired += OnSessionExpired;

        Loaded += (_, _) => StartProctoring();
        Closing += OnClosing;
    }

    private string Language => LanguageBox.SelectedItem as string ?? "Python";

    private void StartProctoring()
    {
        _proctoring = true;
        Lockdown.Enable();
        // Block copy, cut and paste everywhere in the window (shortcuts and context menus).
        CommandManager.AddPreviewExecutedHandler(this, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Copy || e.Command == ApplicationCommands.Cut || e.Command == ApplicationCommands.Paste)
                e.Handled = true;
        });
        CodeEditor.AllowDrop = false;
        StdinBox.AllowDrop = false;
        if (!DevMode) Deactivated += OnDeactivated;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) _session.Minimized();
        };

        _audio.ChunkReady += chunk => _outbox.Evidence(EvidenceKind.Audio, chunk, "audio/wav");
        if (!_audio.Start(out var audioError)) _session.AudioUnavailable(audioError ?? "No microphone.");

        _tick.Tick += (_, _) => Tick();
        _faceTimer.Tick += (_, _) =>
        {
            var (result, jpeg) = _face.Check();
            _session.FaceResult(result, jpeg);
            ShowStrikes();
        };
        _saveTimer.Tick += (_, _) => Save();
        _tick.Start();
        _faceTimer.Start();
        _saveTimer.Start();
        _outbox.Start();
        _ = _live.StartWithRetryAsync(_cts.Token);
        Tick();
    }

    private void Tick()
    {
        var left = _session.Clock.Remaining(DateTime.UtcNow);
        TimerText.Text = left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        TimerText.SetResourceReference(ForegroundProperty, left < TimeSpan.FromMinutes(5) ? "Danger" : "TextPrimary");
        if (left == TimeSpan.Zero && !_session.Ended) Submit("Time limit reached.");
    }

    private void Save()
    {
        _session.SaveCode(CodeEditor.Text, Language);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_localCopy)!);
            File.WriteAllText(_localCopy, CodeEditor.Text);
        }
        catch (IOException) { } // the outbox copy is the one that matters
        SavedText.Text = $"Saved at {DateTime.Now:HH:mm}";
    }

    private void ShowStrikes()
    {
        foreach (var (dot, n) in new[] { (Strike1, 1), (Strike2, 2), (Strike3, 3) })
            dot.SetResourceReference(Shape.FillProperty, _session.FaceStrikes >= n ? "Danger" : "BorderStrong");
    }

    private void ShowConnection(bool online)
    {
        if (online) Banner.Hide();
        else Banner.Show(InfoSeverity.Warning, "Offline — your work is saved on this PC and will be sent when the connection returns.");
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_session.Ended) return;
        // Let activation settle, then count it only if focus went to another process (our own dialogs also deactivate us).
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_session.Ended || IsActive || Lockdown.ForegroundIsOurProcess()) return;
            _session.FocusLost();
            if (!_session.Ended)
                Banner.Show(InfoSeverity.Error,
                    $"You switched away from the exam ({_session.FocusMisses}/{ExamSession.MaxFocusMisses}). Reaching the limit submits your exam.");
        });
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        OutputBox.Text = "Running…";
        try
        {
            var r = await _api.RunAsync(new RunRequest(Language, CodeEditor.Text, StdinBox.Text));
            OutputBox.Text = r.Stdout
                + (r.Stderr.Length > 0 ? "\n" + r.Stderr : "")
                + (r.TimedOut ? "\n[Timed out]" : "")
                + $"\n[exit code {r.ExitCode} · {r.DurationMs} ms]";
        }
        catch (ApiError error) when (error.Status == 429)
        {
            OutputBox.Text = "Too many runs. Wait a moment and try again.";
        }
        catch (ApiError error)
        {
            OutputBox.Text = error.Display;
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Submit your code now? You can't change it afterwards.", "Submit exam",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Submit("Submitted by the student.");
    }

    // Final frame and screenshot first, then stop proctoring (which queues the last audio chunk), then submit.
    private void Submit(string reason)
    {
        if (_session.Ended) return;
        var face = _face.CaptureJpeg();
        var screen = ScreenCapture.Jpeg();
        StopProctoring();
        _session.Submit(CodeEditor.Text, Language, reason, face, screen);
        _ = FinishAsync();
    }

    private void StopProctoring()
    {
        if (!_proctoring) return;
        _proctoring = false;
        _tick.Stop();
        _faceTimer.Stop();
        _saveTimer.Stop();
        Deactivated -= OnDeactivated;
        _audio.Stop();
        Lockdown.Disable();
    }

    // Waits until the queue is sent, the server closes the attempt, or the submit grace period is over; then shows the outcome.
    private async Task FinishAsync()
    {
        if (_finishing) return;
        _finishing = true;
        StopProctoring();
        Overlay.Visibility = Visibility.Visible;
        Transitions.PopIn(OverlayCard);
        OverlayTitle.Text = "Submitting…";
        OverlayText.Text = "Sending your code and evidence.";

        while (_outbox.Pending > 0 && !_outbox.Closed && !_session.Clock.PastSubmitGrace(DateTime.UtcNow))
        {
            if (!_outbox.IsOnline)
                OverlayText.Text = "Offline — your submission will be sent when the connection returns. Keep this window open.";
            await Task.Delay(500);
        }

        (OverlayTitle.Text, OverlayText.Text) = await OutcomeAsync();
        OverlayButton.Visibility = Visibility.Visible;
        Topmost = false;
    }

    private async Task<(string Title, string Text)> OutcomeAsync()
    {
        try
        {
            var a = (await _api.AttemptAsync(_session.Attempt.Id)).Attempt;
            return a.Status switch
            {
                AttemptStatus.Submitted => ("Submitted", $"Your exam was submitted. {a.SubmitReason}"),
                AttemptStatus.AutoSubmitted => ("Submitted by the server", "The deadline passed, so the server submitted your last saved code."),
                AttemptStatus.Voided => ("Attempt voided", "Staff voided this attempt. Ask your teacher what to do next."),
                _ => ("Not submitted yet", "The server hasn't received your submission. Sign in again and choose Resume; your work is saved on this PC."),
            };
        }
        catch (ApiError)
        {
            return _outbox.Pending == 0
                ? ("Submitted", "Your exam was submitted.")
                : ("Couldn't reach the server", "The deadline has passed, so the server has submitted your last saved code.");
        }
    }

    private void OverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _canClose = true;
        Close();
    }

    // Signed out (session revoked): leave without submitting; the attempt can be resumed after signing in again.
    private void OnSessionExpired(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_finishing) return;
        _session.Abandon("Signed out");
        StopProctoring();
        _canClose = true;
        MessageBox.Show(this, "You were signed out. Sign in again and choose Resume to continue — your work is saved on this PC.",
            "Signed out", MessageBoxButton.OK, MessageBoxImage.Warning);
        Close();
    });

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_canClose)
        {
            e.Cancel = true; // a close we didn't start (e.g. a key combination the hook missed) submits
            if (!_session.Ended) Submit("Test window was closed.");
            return;
        }
        _api.SessionExpired -= OnSessionExpired;
        StopProctoring();
        _cts.Cancel();
        _face.Dispose();
        _audio.Dispose();
        _ = _live.DisposeAsync().AsTask();
        _ = CloseOutboxAsync();
    }

    private async Task CloseOutboxAsync()
    {
        await _outbox.DisposeAsync();
        _outbox.DeleteIfEmpty(); // anything left stays on disk and is sent when the attempt is resumed
    }

    private async Task ResyncAsync()
    {
        try { _session.Resync((await _api.AttemptAsync(_session.Attempt.Id)).Attempt); }
        catch (ApiError) { } // offline: the hub reconnects and this runs again
    }
}
```

- [ ] **Step 6: Keep the exam window open on sign-out** — in `App.xaml.cs` `SignOut`, change the close loop's filter (the exam window handles its own exit without submitting):

```csharp
        foreach (var window in Current.Windows.OfType<Window>().Where(w => w != login && w is not Features.Student.ExamWindow).ToList())
            window.Close();
```

- [ ] **Step 7: Build and test**

Run: `dotnet build Secucode.sln` — expected: 0 errors (a CS0162 "unreachable code" warning from `DevMode` is expected).
Run: `dotnet test clients/Secucode.Desktop.Tests` — expected: PASS.
The window is exercised by hand in Task 11, once students can open it.

- [ ] **Step 8: Commit**

```bash
git add clients/Secucode.Desktop clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): exam session rules and the proctored exam window on the API

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Student exams list and the start/resume page

**Files:**
- Create: `clients/Secucode.Desktop/Features/Student/StudentExamRow.cs`, `StudentExamsPage.xaml(.cs)`, `ExamStartViewModel.cs`, `ExamStartPage.xaml(.cs)`
- Modify: `clients/Secucode.Desktop/Shell/ShellWindow.xaml.cs` (Exams entry for students)
- Test: `clients/Secucode.Desktop.Tests/StudentExamRowTests.cs`, `clients/Secucode.Desktop.Tests/ExamStartViewModelTests.cs`

**Interfaces:**
- Consumes: `ApiClient.StudentExamsAsync/StartAttemptAsync/AttemptAsync` (Task 4), `FaceReference`, `FaceMonitor` (Task 9), `ExamWindow` (Task 10), `ShellWindow.Current.Navigate` (Task 8), `InfoBar` (Task 7).
- Produces:
  - `record StudentExamRow(StudentExamDto Exam, DateTime NowUtc) { string Title; bool IsOpen; bool CanResume; bool CanStart; string ActionLabel; string When; string ModeText; }`
  - `sealed class ExamStartViewModel(ApiClient api, StudentExamDto exam) { StudentExamDto Exam; bool IsResume; Task<AttemptDto> BeginAsync(string? pin, CancellationToken ct = default); }` — resume reads the attempt (`GET /attempts/{id}`) without a PIN; start posts the PIN.
  - `partial class StudentExamsPage : UserControl` (parameterless); `partial class ExamStartPage(StudentExamDto exam) : UserControl`.

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/StudentExamRowTests.cs`:

```csharp
using Secucode.Contracts;
using Secucode.Desktop.Features.Student;

namespace Secucode.Desktop.Tests;

public sealed class StudentExamRowTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

    private static StudentExamRow Row(int startsInMinutes, int? inProgress = null, int windowMinutes = 120) =>
        new(new StudentExamDto(1, "DSA mid-term", Now.AddMinutes(startsInMinutes), Now.AddMinutes(startsInMinutes + windowMinutes), 30,
            ExamMode.DesktopOnly, inProgress), Now);

    [Fact]
    public void An_open_exam_can_be_started()
    {
        var row = Row(-5);
        Assert.True(row.CanStart);
        Assert.Equal("Start", row.ActionLabel);
    }

    [Fact]
    public void An_attempt_in_progress_is_resumed_even_after_the_window_closed()
    {
        var row = Row(-200, inProgress: 42);
        Assert.True(row.CanStart);
        Assert.Equal("Resume", row.ActionLabel);
    }

    [Fact]
    public void Future_and_past_exams_cannot_be_started()
    {
        Assert.False(Row(30).CanStart);
        Assert.StartsWith("Opens ", Row(30).ActionLabel);
        Assert.False(Row(-300).CanStart);
        Assert.Equal("Closed", Row(-300).ActionLabel);
    }

    [Fact]
    public void Details_read_naturally()
    {
        var row = Row(-5);
        Assert.EndsWith("· 30 min", row.When);
        Assert.Equal("Desktop only", row.ModeText);
    }
}
```

`clients/Secucode.Desktop.Tests/ExamStartViewModelTests.cs`:

```csharp
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Student;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class ExamStartViewModelTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<(ApiClient Client, int ExamId)> StudentWithExamAsync()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var client = new ApiClient(api.Factory.CreateClient());
        await client.LoginAsync(student.Username, World.Password);
        return (client, exam.Id);
    }

    [Fact]
    public async Task Start_uses_the_pin_and_resume_returns_the_same_attempt_without_it()
    {
        var (client, examId) = await StudentWithExamAsync();
        var listed = (await client.StudentExamsAsync()).Single(e => e.Id == examId);

        var started = await new ExamStartViewModel(client, listed).BeginAsync(World.Pin);

        var again = (await client.StudentExamsAsync()).Single(e => e.Id == examId);
        var resume = new ExamStartViewModel(client, again);
        Assert.True(resume.IsResume);
        var resumed = await resume.BeginAsync(null);
        Assert.Equal(started.Id, resumed.Id);
        Assert.Equal(started.QuestionText, resumed.QuestionText);
    }

    [Fact]
    public async Task A_wrong_pin_shows_the_servers_reason()
    {
        var (client, examId) = await StudentWithExamAsync();
        var listed = (await client.StudentExamsAsync()).Single(e => e.Id == examId);

        var e = await Assert.ThrowsAsync<ApiError>(() => new ExamStartViewModel(client, listed).BeginAsync("0000"));

        Assert.Equal(403, e.Status);
        Assert.Equal("Incorrect PIN.", e.Detail);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~StudentExamRowTests|FullyQualifiedName~ExamStartViewModelTests"`
Expected: build errors — `StudentExamRow`, `ExamStartViewModel` not found.

- [ ] **Step 3: Implement the row and view model**

`clients/Secucode.Desktop/Features/Student/StudentExamRow.cs`:

```csharp
using Secucode.Contracts;

namespace Secucode.Desktop.Features.Student;

// One exam card on the student's list. Times are shown in local time.
public sealed record StudentExamRow(StudentExamDto Exam, DateTime NowUtc)
{
    public string Title => Exam.Title;
    public bool IsOpen => NowUtc >= Exam.StartsAt && NowUtc < Exam.EndsAt;
    public bool CanResume => Exam.InProgressAttemptId is not null;
    public bool CanStart => CanResume || IsOpen;

    public string ActionLabel =>
        CanResume ? "Resume"
        : IsOpen ? "Start"
        : NowUtc < Exam.StartsAt ? $"Opens {Local(Exam.StartsAt):d MMM, HH:mm}"
        : "Closed";

    public string When => $"{Local(Exam.StartsAt):ddd d MMM, HH:mm} – {Local(Exam.EndsAt):HH:mm} · {Exam.DurationMinutes} min";
    public string ModeText => Exam.Mode == ExamMode.DesktopOnly ? "Desktop only" : "Desktop or web";

    private static DateTime Local(DateTime utc) => utc.ToLocalTime();
}
```

`clients/Secucode.Desktop/Features/Student/ExamStartViewModel.cs`:

```csharp
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Features.Student;

public sealed class ExamStartViewModel(ApiClient api, StudentExamDto exam)
{
    public StudentExamDto Exam => exam;
    public bool IsResume => exam.InProgressAttemptId is not null;

    // Start checks the PIN on the server; resume just reads the attempt that is already running.
    public async Task<AttemptDto> BeginAsync(string? pin, CancellationToken ct = default) =>
        exam.InProgressAttemptId is int id
            ? (await api.AttemptAsync(id, ct)).Attempt
            : await api.StartAttemptAsync(exam.Id, pin ?? "", ct);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~StudentExamRowTests|FullyQualifiedName~ExamStartViewModelTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: The exams list page**

`clients/Secucode.Desktop/Features/Student/StudentExamsPage.xaml`:

```xml
<UserControl x:Class="Secucode.Desktop.Features.Student.StudentExamsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:theme="clr-namespace:Secucode.Desktop.Theme">
    <DockPanel>
        <DockPanel DockPanel.Dock="Top" Margin="0,0,0,20">
            <Button DockPanel.Dock="Right" Style="{StaticResource IconButton}" Content="&#xE72C;" ToolTip="Refresh" Click="Refresh_Click"/>
            <StackPanel>
                <TextBlock Text="Exams" Style="{StaticResource Title}"/>
                <TextBlock Text="Exams you can take now, and the ones coming up." Style="{StaticResource Caption}" FontSize="14" Margin="0,4,0,0"/>
            </StackPanel>
        </DockPanel>
        <theme:InfoBar x:Name="Info" DockPanel.Dock="Top" Margin="0,0,0,12"/>
        <Grid>
            <TextBlock x:Name="Empty" Visibility="Collapsed" Style="{StaticResource Body}" Foreground="{DynamicResource TextSecondary}"
                       Text="No exams right now. They appear here when your teacher schedules one."/>
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <ItemsControl x:Name="List">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Style="{StaticResource Card}" Margin="0,0,0,12">
                                <DockPanel>
                                    <Button DockPanel.Dock="Right" Style="{StaticResource PrimaryButton}" MinWidth="120" VerticalAlignment="Center"
                                            Content="{Binding ActionLabel}" IsEnabled="{Binding CanStart}" Click="Open_Click"/>
                                    <StackPanel>
                                        <TextBlock Text="{Binding Title}" FontSize="16" FontWeight="SemiBold"/>
                                        <TextBlock Text="{Binding When}" Style="{StaticResource Caption}" FontSize="13" Margin="0,4,0,0"/>
                                        <TextBlock Text="{Binding ModeText}" Style="{StaticResource Caption}" Margin="0,2,0,0"/>
                                    </StackPanel>
                                </DockPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>
    </DockPanel>
</UserControl>
```

`clients/Secucode.Desktop/Features/Student/StudentExamsPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Api;
using Secucode.Desktop.Shell;

namespace Secucode.Desktop.Features.Student;

public partial class StudentExamsPage : UserControl
{
    public StudentExamsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Info.Hide();
        try
        {
            var now = DateTime.UtcNow;
            var rows = (await App.Api.StudentExamsAsync()).OrderBy(e => e.StartsAt).Select(e => new StudentExamRow(e, now)).ToList();
            List.ItemsSource = rows;
            Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiError error)
        {
            Info.ShowError(error, () => _ = LoadAsync());
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is StudentExamRow row)
            ShellWindow.Current?.Navigate(new ExamStartPage(row.Exam));
    }
}
```

- [ ] **Step 6: The start/resume page**

`clients/Secucode.Desktop/Features/Student/ExamStartPage.xaml`:

```xml
<UserControl x:Class="Secucode.Desktop.Features.Student.ExamStartPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:theme="clr-namespace:Secucode.Desktop.Theme">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel MaxWidth="720" HorizontalAlignment="Left">
            <Button Style="{StaticResource SubtleButton}" HorizontalAlignment="Left" Margin="-8,0,0,8" Click="Back_Click">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="&#xE72B;" Style="{StaticResource Icon}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBlock Text="Exams"/>
                </StackPanel>
            </Button>
            <TextBlock x:Name="ExamTitle" Style="{StaticResource Title}"/>
            <TextBlock x:Name="WhenText" Style="{StaticResource Caption}" FontSize="14" Margin="0,4,0,20"/>

            <Border Style="{StaticResource Card}" Margin="0,0,0,12">
                <StackPanel>
                    <TextBlock Text="Before you start" FontWeight="SemiBold" Margin="0,0,0,8"/>
                    <TextBlock Style="{StaticResource Body}" Foreground="{DynamicResource TextSecondary}">
                        The exam opens full screen. Your webcam checks that you are present every few seconds, and your microphone records.
                        Three failed face checks in a row, switching away three times, or minimizing the window submits your exam.
                    </TextBlock>
                    <TextBlock x:Name="ModeText" Style="{StaticResource Caption}" Margin="0,10,0,0"/>
                </StackPanel>
            </Border>

            <Border Style="{StaticResource Card}" Margin="0,0,0,12">
                <DockPanel>
                    <Border DockPanel.Dock="Left" Width="160" Height="120" CornerRadius="8" Background="{DynamicResource SurfaceSunken}" Margin="0,0,20,0">
                        <Grid>
                            <TextBlock x:Name="PreviewHint" Text="&#xE722;" Style="{StaticResource Icon}" FontSize="28"
                                       Foreground="{DynamicResource TextDisabled}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            <Image x:Name="Preview" Stretch="UniformToFill"/>
                        </Grid>
                    </Border>
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="Your face" FontWeight="SemiBold"/>
                        <TextBlock Text="Look at the camera in good light. This picture is used to check it's you during the exam."
                                   Style="{StaticResource Caption}" TextWrapping="Wrap" Margin="0,4,0,12"/>
                        <Button x:Name="CaptureButton" Content="Capture face" HorizontalAlignment="Left" Click="Capture_Click"/>
                    </StackPanel>
                </DockPanel>
            </Border>

            <Border x:Name="PinPanel" Style="{StaticResource Card}" Margin="0,0,0,12">
                <StackPanel>
                    <TextBlock Text="Exam PIN" FontWeight="SemiBold"/>
                    <TextBlock Text="Your personal PIN, given by your teacher." Style="{StaticResource Caption}" Margin="0,4,0,10"/>
                    <PasswordBox x:Name="Pin" Width="200" HorizontalAlignment="Left" PasswordChanged="Pin_PasswordChanged"/>
                </StackPanel>
            </Border>

            <theme:InfoBar x:Name="Info" Margin="0,0,0,12"/>
            <Button x:Name="BeginButton" Style="{StaticResource PrimaryButton}" HorizontalAlignment="Right" MinWidth="160" Click="Begin_Click"/>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

`clients/Secucode.Desktop/Features/Student/ExamStartPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Proctoring;
using Secucode.Desktop.Shell;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Student;

public partial class ExamStartPage : UserControl
{
    private readonly ExamStartViewModel _vm;
    private byte[]? _referencePng;

    public ExamStartPage(StudentExamDto exam)
    {
        InitializeComponent();
        _vm = new ExamStartViewModel(App.Api, exam);
        var row = new StudentExamRow(exam, DateTime.UtcNow);
        ExamTitle.Text = exam.Title;
        WhenText.Text = row.When;
        ModeText.Text = row.ModeText;
        PinPanel.Visibility = _vm.IsResume ? Visibility.Collapsed : Visibility.Visible;
        BeginButton.Content = _vm.IsResume ? "Resume exam" : "Start exam";
        UpdateBegin();
    }

    private void UpdateBegin() =>
        BeginButton.IsEnabled = _referencePng is not null && (_vm.IsResume || Pin.Password.Length > 0);

    private void Pin_PasswordChanged(object sender, RoutedEventArgs e) => UpdateBegin();

    private void Back_Click(object sender, RoutedEventArgs e) => ShellWindow.Current?.Navigate(new StudentExamsPage());

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        CaptureButton.IsEnabled = false;
        Info.Hide();
        try
        {
            var face = await Task.Run(() => FaceReference.Capture()); // up to ~3 s of camera warm-up
            _referencePng = face.Png;
            Preview.Source = face.Preview;
            PreviewHint.Visibility = Visibility.Collapsed;
            CaptureButton.Content = "Retake";
        }
        catch (InvalidOperationException error)
        {
            Info.Show(InfoSeverity.Error, error.Message);
        }
        finally
        {
            CaptureButton.IsEnabled = true;
            UpdateBegin();
        }
    }

    private async void Begin_Click(object sender, RoutedEventArgs e)
    {
        BeginButton.IsEnabled = false;
        Info.Hide();
        // Proctoring must work before the server starts the clock (spec §9). Created on the UI thread on purpose.
        var monitor = FaceMonitor.TryStart(_referencePng);
        if (monitor is null)
        {
            Info.Show(InfoSeverity.Error, "The webcam isn't available, so the exam can't start. Close other apps that use the camera and try again.");
            UpdateBegin();
            return;
        }
        try
        {
            var attempt = await _vm.BeginAsync(Pin.Password);
            var window = new ExamWindow(App.Api, _vm.Exam.Title, attempt, monitor, _referencePng!);
            window.Closed += (_, _) => ShellWindow.Current?.Navigate(new StudentExamsPage());
            window.Show();
            ShellWindow.Current?.Navigate(new StudentExamsPage());
        }
        catch (ApiError error)
        {
            monitor.Dispose();
            Info.ShowError(error);
            UpdateBegin();
        }
    }
}
```

- [ ] **Step 7: Show it to students** — in `ShellWindow.xaml.cs`, add `using Secucode.Desktop.Features.Student;` and replace the Exams entry:

```csharp
            new NavItem("", "Exams", () => session.IsStudent
                ? new StudentExamsPage()
                : new TextBlock { Text = "Exams", Style = (Style)FindResource("Title") }),
```

- [ ] **Step 8: Build, test, and take an exam by hand**

Run: `dotnet build Secucode.sln` and `dotnet test clients/Secucode.Desktop.Tests` — expected: 0 errors, PASS.
With the API running (Task 8 Step 8; if the seeded exam's window has passed, re-run the seed or create an open exam as `faculty.cse` through Swagger at `/swagger`), build Release so focus checks are on: `dotnet run --project clients/Secucode.Desktop -c Release`. Sign in as `student1 / Student@123` (PIN `1234`) and use the `screenshotting-to-verify` skill to check, in light and dark:
1. The exams list shows the seeded open exam with **Start**.
2. Start page: capture face (preview appears), enter PIN, **Start exam** → full-screen exam window with question, timer counting down, strike dots.
3. Type code, **Run** a Python `print('hi')` → output shows `hi` and the exit line.
4. Alt+Tab is swallowed; click another monitor/app once → red banner "1/3".
5. Stop the API process for ~40 s → orange offline banner appears; start it again → banner disappears.
6. Close the app from Task Manager mid-exam, start it again, sign in → the exam shows **Resume** → resume (no PIN) → your code is back and the timer continues from the server's deadline.
7. **Submit exam** → overlay "Submitted" → Back to exams → the exam is gone from the list.
Fix anything that misbehaves before committing.

- [ ] **Step 9: Commit**

```bash
git add clients/Secucode.Desktop clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): student exam list with start and resume

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Staff exam list and attempts

**Files:**
- Create: `clients/Secucode.Desktop/Features/Staff/StaffExamsViewModel.cs`, `StaffExamsPage.xaml(.cs)`, `AttemptsViewModel.cs`, `AttemptsPage.xaml(.cs)`
- Modify: `clients/Secucode.Desktop/Shell/ShellWindow.xaml.cs` (Exams entry for staff)
- Test: `clients/Secucode.Desktop.Tests/StaffExamsViewModelTests.cs`, `clients/Secucode.Desktop.Tests/AttemptsViewModelTests.cs`

**Interfaces:**
- Consumes: `ApiClient.StaffExamsAsync/SetExamActiveAsync/DeleteExamAsync/ExamAttemptsAsync/AttemptAsync` (Task 4), `Session` (Task 5), `InfoBar` (Task 7), `ShellWindow` (Task 8).
- Produces:
  - `sealed class StaffExamsViewModel(ApiClient api, Session session) { DateTime From; DateTime To; bool CanCreate; List<StaffExamRow> Rows; Task LoadAsync(); Task ToggleActiveAsync(StaffExamRow); Task DeleteAsync(StaffExamRow); }`
  - `record StaffExamRow(ExamDto Exam, Session Session) { string Title, Window, Duration, Mode, Status, CreatedBy; bool CanManage; bool CanViewResults; }`
  - `sealed class AttemptsViewModel(ApiClient api, ExamDto exam) { ExamDto Exam; List<AttemptRow> Rows; Task LoadAsync(); Task<AttemptDetailDto> DetailAsync(int attemptId); }`; `record AttemptRow(AttemptSummaryDto Attempt) { string Student, Status, Client, Started, Submitted; int Strikes; }`
  - `partial class StaffExamsPage(Session session)`; `partial class AttemptsPage(ExamDto exam)` — the editor page it links to (`ExamEditorPage`) arrives in Task 13; New/Edit are empty handlers until Task 13 Step 6 wires them.

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/StaffExamsViewModelTests.cs`:

```csharp
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Staff;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class StaffExamsViewModelTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<(StaffExamsViewModel Vm, Section S, TestUser Author)> FacultyAsync()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var client = new ApiClient(api.Factory.CreateClient());
        var me = await client.LoginAsync(author.Username, World.Password);
        return (new StaffExamsViewModel(client, new Session(me)), s, author);
    }

    [Fact]
    public async Task Own_exams_are_listed_with_manage_and_results_rights()
    {
        var (vm, s, author) = await FacultyAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows, r => r.Exam.Id == exam.Id);
        Assert.True(row.CanManage);
        Assert.True(row.CanViewResults);
        Assert.True(vm.CanCreate);
        Assert.Equal("Active", row.Status);
    }

    [Fact]
    public async Task Toggling_flips_active_and_reloads()
    {
        var (vm, s, author) = await FacultyAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        await vm.LoadAsync();

        await vm.ToggleActiveAsync(vm.Rows.Single(r => r.Exam.Id == exam.Id));

        Assert.Equal("Inactive", vm.Rows.Single(r => r.Exam.Id == exam.Id).Status);
    }

    [Fact]
    public async Task Deleting_an_exam_with_attempts_is_refused_with_the_servers_reason()
    {
        var (vm, s, author) = await FacultyAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        await _world.AttemptAsync(exam.Id, student.Id);
        await vm.LoadAsync();

        var e = await Assert.ThrowsAsync<ApiError>(() => vm.DeleteAsync(vm.Rows.Single(r => r.Exam.Id == exam.Id)));

        Assert.Equal(409, e.Status);
        Assert.Contains("Deactivate it instead", e.Detail);
    }
}
```

`clients/Secucode.Desktop.Tests/AttemptsViewModelTests.cs`:

```csharp
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Staff;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class AttemptsViewModelTests(ApiFixture api)
{
    private readonly World _world = new(api);

    [Fact]
    public async Task Lists_attempts_and_loads_one_in_detail()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var attempt = await _world.AttemptAsync(exam.Id, student.Id, AttemptStatus.Submitted);
        var client = new ApiClient(api.Factory.CreateClient());
        await client.LoginAsync(author.Username, World.Password);
        var vm = new AttemptsViewModel(client, await client.ExamAsync(exam.Id));

        await vm.LoadAsync();
        var detail = await vm.DetailAsync(attempt.Id);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("Submitted", row.Status);
        Assert.Equal("Student Tester", row.Student);
        Assert.Equal("print(1)", detail.Attempt.Code);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~StaffExamsViewModelTests|FullyQualifiedName~AttemptsViewModelTests"`
Expected: build errors — `Secucode.Desktop.Features.Staff` types not found.

- [ ] **Step 3: Implement the view models**

`clients/Secucode.Desktop/Features/Staff/StaffExamsViewModel.cs`:

```csharp
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Features.Staff;

public sealed class StaffExamsViewModel(ApiClient api, Session session)
{
    public DateTime From { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime To { get; set; } = DateTime.Today.AddDays(60);
    public bool CanCreate => session.CanCreateExams;
    public List<StaffExamRow> Rows { get; private set; } = [];

    public async Task LoadAsync() =>
        Rows = (await api.StaffExamsAsync(From.ToUniversalTime(), To.AddDays(1).ToUniversalTime()))
            .Select(e => new StaffExamRow(e, session)).ToList();

    public async Task ToggleActiveAsync(StaffExamRow row)
    {
        await api.SetExamActiveAsync(row.Exam.Id, !row.Exam.IsActive);
        await LoadAsync();
    }

    public async Task DeleteAsync(StaffExamRow row)
    {
        await api.DeleteExamAsync(row.Exam.Id);
        await LoadAsync();
    }
}

public sealed record StaffExamRow(ExamDto Exam, Session Session)
{
    public string Title => Exam.Title;
    public string Duration => $"{Exam.DurationMinutes} min";
    public string Mode => Exam.Mode == ExamMode.DesktopOnly ? "Desktop only" : "Desktop or web";
    public string Status => Exam.IsActive ? "Active" : "Inactive";
    public string CreatedBy => Exam.CreatedByName;
    public bool CanManage => Session.CanManage(Exam);
    public bool CanViewResults => Session.CanViewResults(Exam);

    public string Window
    {
        get
        {
            var start = Exam.StartsAt.ToLocalTime();
            var end = Exam.EndsAt.ToLocalTime();
            return start.Date == end.Date ? $"{start:ddd d MMM, HH:mm} – {end:HH:mm}" : $"{start:d MMM HH:mm} – {end:d MMM HH:mm}";
        }
    }
}
```

`clients/Secucode.Desktop/Features/Staff/AttemptsViewModel.cs`:

```csharp
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Features.Staff;

public sealed class AttemptsViewModel(ApiClient api, ExamDto exam)
{
    public ExamDto Exam => exam;
    public List<AttemptRow> Rows { get; private set; } = [];

    public async Task LoadAsync() => Rows = (await api.ExamAttemptsAsync(exam.Id)).Select(a => new AttemptRow(a)).ToList();

    public Task<AttemptDetailDto> DetailAsync(int attemptId) => api.AttemptAsync(attemptId);
}

public sealed record AttemptRow(AttemptSummaryDto Attempt)
{
    public string Student => Attempt.StudentName;
    public string Status => Attempt.Status switch
    {
        AttemptStatus.InProgress => "In progress",
        AttemptStatus.AutoSubmitted => "Auto-submitted",
        _ => Attempt.Status.ToString(),
    };
    public string Client => Attempt.Client.ToString();
    public string Started => Attempt.StartedAt.ToLocalTime().ToString("d MMM HH:mm");
    public string Submitted => Attempt.SubmittedAt?.ToLocalTime().ToString("d MMM HH:mm") ?? "—";
    public int Strikes => Attempt.Strikes;
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~StaffExamsViewModelTests|FullyQualifiedName~AttemptsViewModelTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: The exam list page**

`clients/Secucode.Desktop/Features/Staff/StaffExamsPage.xaml`:

```xml
<UserControl x:Class="Secucode.Desktop.Features.Staff.StaffExamsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:theme="clr-namespace:Secucode.Desktop.Theme">
    <DockPanel>
        <DockPanel DockPanel.Dock="Top" Margin="0,0,0,16">
            <Button x:Name="NewButton" DockPanel.Dock="Right" Style="{StaticResource PrimaryButton}" VerticalAlignment="Center" Click="New_Click">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="&#xE710;" Style="{StaticResource Icon}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBlock Text="New exam"/>
                </StackPanel>
            </Button>
            <TextBlock Text="Exams" Style="{StaticResource Title}"/>
        </DockPanel>
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="From" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <DatePicker x:Name="FromDate" Width="140"/>
            <TextBlock Text="to" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="12,0,8,0"/>
            <DatePicker x:Name="ToDate" Width="140"/>
            <Button Style="{StaticResource IconButton}" Content="&#xE72C;" ToolTip="Refresh" Margin="8,0,0,0" Click="Refresh_Click"/>
        </StackPanel>
        <theme:InfoBar x:Name="Info" DockPanel.Dock="Top" Margin="0,0,0,12"/>
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button x:Name="AttemptsButton" Content="Attempts" Margin="0,0,8,0" Click="Attempts_Click"/>
            <Button x:Name="EditButton" Content="Edit" Margin="0,0,8,0" Click="Edit_Click"/>
            <Button x:Name="ToggleButton" Content="Deactivate" Margin="0,0,8,0" Click="Toggle_Click"/>
            <Button x:Name="DeleteButton" Content="Delete" Click="Delete_Click"/>
        </StackPanel>
        <Border Style="{StaticResource Card}" Padding="8">
            <DataGrid x:Name="Grid" SelectionChanged="Grid_SelectionChanged" MouseDoubleClick="Grid_MouseDoubleClick">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Title" Binding="{Binding Title}" Width="2*"/>
                    <DataGridTextColumn Header="When" Binding="{Binding Window}" Width="2*"/>
                    <DataGridTextColumn Header="Duration" Binding="{Binding Duration}" Width="Auto"/>
                    <DataGridTextColumn Header="Mode" Binding="{Binding Mode}" Width="Auto"/>
                    <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="Auto"/>
                    <DataGridTextColumn Header="Created by" Binding="{Binding CreatedBy}" Width="*"/>
                </DataGrid.Columns>
            </DataGrid>
        </Border>
    </DockPanel>
</UserControl>
```

`clients/Secucode.Desktop/Features/Staff/StaffExamsPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Secucode.Desktop.Api;
using Secucode.Desktop.Shell;

namespace Secucode.Desktop.Features.Staff;

public partial class StaffExamsPage : UserControl
{
    private readonly StaffExamsViewModel _vm;

    public StaffExamsPage(Session session)
    {
        InitializeComponent();
        _vm = new StaffExamsViewModel(App.Api, session);
        FromDate.SelectedDate = _vm.From;
        ToDate.SelectedDate = _vm.To;
        NewButton.Visibility = _vm.CanCreate ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
        Loaded += async (_, _) => await LoadAsync();
    }

    private StaffExamRow? Selected => Grid.SelectedItem as StaffExamRow;

    private async Task LoadAsync()
    {
        Info.Hide();
        _vm.From = FromDate.SelectedDate ?? _vm.From;
        _vm.To = ToDate.SelectedDate ?? _vm.To;
        await RunAsync(_vm.LoadAsync);
    }

    // Runs an action, then refreshes the grid; errors go to the InfoBar.
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            Grid.ItemsSource = _vm.Rows;
        }
        catch (ApiError error)
        {
            Info.ShowError(error, () => _ = LoadAsync());
        }
        UpdateActions();
    }

    private void UpdateActions()
    {
        var row = Selected;
        AttemptsButton.IsEnabled = row?.CanViewResults == true;
        EditButton.IsEnabled = ToggleButton.IsEnabled = DeleteButton.IsEnabled = row?.CanManage == true;
        ToggleButton.Content = row?.Exam.IsActive == false ? "Activate" : "Deactivate";
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected?.CanViewResults == true) Attempts_Click(sender, e);
    }

    private void Attempts_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row) ShellWindow.Current?.Navigate(new AttemptsPage(row.Exam));
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row) _ = RunAsync(() => _vm.ToggleActiveAsync(row));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (MessageBox.Show(Window.GetWindow(this)!, $"Delete \"{row.Title}\"? This can't be undone.", "Delete exam",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            _ = RunAsync(() => _vm.DeleteAsync(row));
    }

    private void New_Click(object sender, RoutedEventArgs e) { } // wired to ExamEditorPage in Task 13
    private void Edit_Click(object sender, RoutedEventArgs e) { } // wired to ExamEditorPage in Task 13
}
```

- [ ] **Step 6: The attempts page**

`clients/Secucode.Desktop/Features/Staff/AttemptsPage.xaml`:

```xml
<UserControl x:Class="Secucode.Desktop.Features.Staff.AttemptsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:theme="clr-namespace:Secucode.Desktop.Theme">
    <DockPanel>
        <StackPanel DockPanel.Dock="Top" Margin="0,0,0,16">
            <Button Style="{StaticResource SubtleButton}" HorizontalAlignment="Left" Margin="-8,0,0,8" Click="Back_Click">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="&#xE72B;" Style="{StaticResource Icon}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBlock Text="Exams"/>
                </StackPanel>
            </Button>
            <TextBlock x:Name="ExamTitle" Style="{StaticResource Title}"/>
            <TextBlock Text="Attempts" Style="{StaticResource Caption}" FontSize="14" Margin="0,4,0,0"/>
        </StackPanel>
        <theme:InfoBar x:Name="Info" DockPanel.Dock="Top" Margin="0,0,0,12"/>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="3*"/>
                <ColumnDefinition Width="16"/>
                <ColumnDefinition Width="2*"/>
            </Grid.ColumnDefinitions>
            <Border Style="{StaticResource Card}" Padding="8">
                <Grid>
                    <TextBlock x:Name="Empty" Text="No attempts yet." Margin="12" Visibility="Collapsed" Foreground="{DynamicResource TextSecondary}"/>
                    <DataGrid x:Name="Grid" SelectionChanged="Grid_SelectionChanged">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Student" Binding="{Binding Student}" Width="2*"/>
                            <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="*"/>
                            <DataGridTextColumn Header="App" Binding="{Binding Client}" Width="Auto"/>
                            <DataGridTextColumn Header="Started" Binding="{Binding Started}" Width="Auto"/>
                            <DataGridTextColumn Header="Submitted" Binding="{Binding Submitted}" Width="Auto"/>
                            <DataGridTextColumn Header="Strikes" Binding="{Binding Strikes}" Width="Auto"/>
                        </DataGrid.Columns>
                    </DataGrid>
                </Grid>
            </Border>
            <Border x:Name="DetailCard" Grid.Column="2" Style="{StaticResource Card}" Visibility="Hidden">
                <DockPanel>
                    <StackPanel DockPanel.Dock="Top">
                        <TextBlock x:Name="DetailStudent" Style="{StaticResource Subtitle}"/>
                        <TextBlock x:Name="DetailReason" Style="{StaticResource Caption}" TextWrapping="Wrap" Margin="0,4,0,12"/>
                        <TextBlock Text="Question" Style="{StaticResource Caption}" Margin="0,0,0,4"/>
                        <TextBlock x:Name="DetailQuestion" Style="{StaticResource Body}" Margin="0,0,0,12"/>
                        <TextBlock Text="Events" Style="{StaticResource Caption}" Margin="0,0,0,4"/>
                        <ItemsControl x:Name="DetailEvents" Margin="0,0,0,12"/>
                        <TextBlock x:Name="DetailLanguage" Style="{StaticResource Caption}" Margin="0,0,0,4"/>
                    </StackPanel>
                    <TextBox x:Name="DetailCode" Style="{StaticResource CodeEditor}" IsReadOnly="True"/>
                </DockPanel>
            </Border>
        </Grid>
    </DockPanel>
</UserControl>
```

`clients/Secucode.Desktop/Features/Staff/AttemptsPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Shell;

namespace Secucode.Desktop.Features.Staff;

// Read-only results. Evidence viewing and corrections come in sub-project 5 (web-first).
public partial class AttemptsPage : UserControl
{
    private readonly AttemptsViewModel _vm;

    public AttemptsPage(ExamDto exam)
    {
        InitializeComponent();
        _vm = new AttemptsViewModel(App.Api, exam);
        ExamTitle.Text = exam.Title;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Info.Hide();
        try
        {
            await _vm.LoadAsync();
            Grid.ItemsSource = _vm.Rows;
            Empty.Visibility = _vm.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiError error)
        {
            Info.ShowError(error, () => _ = LoadAsync());
        }
    }

    private async void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not AttemptRow row) return;
        try
        {
            var detail = await _vm.DetailAsync(row.Attempt.Id);
            DetailStudent.Text = detail.StudentName;
            DetailReason.Text = $"{row.Status} · attempt {detail.Attempt.AttemptNumber}" +
                                (detail.Attempt.SubmitReason is { } reason ? $" · {reason}" : "");
            DetailQuestion.Text = detail.Attempt.QuestionText;
            DetailEvents.ItemsSource = detail.Events.Count == 0
                ? new List<string> { "None" }
                : detail.Events.Select(ev => $"{ev.OccurredAt.ToLocalTime():HH:mm:ss}  {ev.Type}{(ev.Detail is null ? "" : $" ({ev.Detail})")}").ToList();
            DetailLanguage.Text = $"Code · {detail.Attempt.Language ?? "not saved yet"}";
            DetailCode.Text = detail.Attempt.Code;
            DetailCard.Visibility = Visibility.Visible;
        }
        catch (ApiError error)
        {
            Info.ShowError(error);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        ShellWindow.Current?.Navigate(new StaffExamsPage(App.Session!));
}
```

- [ ] **Step 7: Show it to staff** — in `ShellWindow.xaml.cs`, add `using Secucode.Desktop.Features.Staff;` and replace the Exams entry:

```csharp
            new NavItem("", "Exams", () => session.IsStudent ? new StudentExamsPage() : new StaffExamsPage(session)),
```

- [ ] **Step 8: Build, test, check by hand**

Run: `dotnet build Secucode.sln` and `dotnet test clients/Secucode.Desktop.Tests` — expected: 0 errors, PASS.
With the API running, sign in as `faculty.cse / Faculty@123` and use `screenshotting-to-verify` to check in light and dark: the exam grid (hover and selected rows readable), date filter + refresh, Deactivate/Activate flips Status, Delete on an exam with attempts shows the server's 409 message in the InfoBar, Attempts (or double-click) shows the attempt from Task 11 with its events and code. Sign in as `hod.cse / Hod@123`: department exams are listed and manageable.

- [ ] **Step 9: Commit**

```bash
git add clients/Secucode.Desktop clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): staff exam list and read-only attempts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Staff exam editor

**Files:**
- Create: `clients/Secucode.Desktop/Features/Staff/ExamEditorViewModel.cs`, `ExamEditorPage.xaml(.cs)`
- Modify: `clients/Secucode.Desktop/Features/Staff/StaffExamsPage.xaml.cs` (`New_Click`, `Edit_Click`)
- Test: `clients/Secucode.Desktop.Tests/ExamEditorViewModelTests.cs`

**Interfaces:**
- Consumes: `ApiClient.MastersAsync/ExamAsync/CreateExamAsync/UpdateExamAsync` (Task 4), `InfoBar` (Task 7), `ShellWindow` (Task 8), `StaffExamsPage` (Task 12).
- Produces:
  - `record MasterOption(int? Id, string Name)`; `class QuestionRow { int? Id; string Text; }`
  - `sealed class ExamEditorViewModel(ApiClient api, int? examId)` with bindable properties `Title`, `StartDate`, `StartTime` ("HH:mm"), `EndDate`, `EndTime`, `Duration` (text), `AllowWeb`, `IsActive`, `AllowQuestionEditAfterAttempts`, `AllowAttemptReset`, `AllowAttemptReopen`, `AllowTimeExtension`, `Departments/Years/Batches` (`List<MasterOption>`), `Department/Year/Batch` (selected `MasterOption?`), `ObservableCollection<QuestionRow> Questions`; methods `Task LoadAsync()`, `(ExamRequest? Request, string? Error) BuildRequest()`, `Task<ExamDto> SaveAsync(ExamRequest)`, `void MoveQuestion(QuestionRow, int delta)`; statics `DateTime ToUtc(DateTime localDate, TimeSpan time)`, `(DateTime Date, string Time) Split(DateTime utc)`.
  - `partial class ExamEditorPage(int? examId)`.
- The client only catches input it can't send (unparseable time/duration, no department). Every other rule (title, end after start, duration range, at least one question, target shape, locked questions after attempts) is the server's, shown from its validation errors.
- The view model has no change notification: the page sets `DataContext` once after `LoadAsync`, and two-way bindings write back into it.

- [ ] **Step 1: Write the failing tests**

`clients/Secucode.Desktop.Tests/ExamEditorViewModelTests.cs`:

```csharp
using System.Net.Http;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Staff;

namespace Secucode.Desktop.Tests;

public sealed class ExamEditorInputTests
{
    private static ExamEditorViewModel Offline() => new(new ApiClient(new HttpClient()), null);

    [Fact]
    public void Local_times_become_utc_and_back()
    {
        var utc = ExamEditorViewModel.ToUtc(new DateTime(2026, 11, 2), new TimeSpan(9, 30, 0));

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Local).ToUniversalTime(), utc);
        Assert.Equal((new DateTime(2026, 11, 2), "09:30"), ExamEditorViewModel.Split(utc));
    }

    [Fact]
    public void Unsendable_input_is_caught_before_the_server()
    {
        var vm = Offline();
        vm.Department = new MasterOption(1, "CSE");

        vm.StartTime = "9.30";
        Assert.Equal("Use HH:mm for times, for example 09:30.", vm.BuildRequest().Error);

        vm.StartTime = "9:30";
        vm.Duration = "an hour";
        Assert.Equal("Duration must be a whole number of minutes.", vm.BuildRequest().Error);

        vm.Duration = "60";
        vm.Department = null;
        Assert.Equal("Choose a department.", vm.BuildRequest().Error);
    }

    [Fact]
    public void Blank_questions_are_left_out_and_order_is_kept()
    {
        var vm = Offline();
        vm.Department = new MasterOption(1, "CSE");
        vm.Questions.Add(new QuestionRow { Text = "First" });
        vm.Questions.Add(new QuestionRow { Text = "  " });
        vm.Questions.Add(new QuestionRow { Text = "Second" });
        vm.MoveQuestion(vm.Questions[2], -2);

        var request = vm.BuildRequest().Request!;

        Assert.Equal(["Second", "First"], request.Questions.Select(q => q.Text));
    }
}

[Collection(DesktopApiCollection.Name)]
public sealed class ExamEditorViewModelTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<(ApiClient Client, Section S, TestUser Author)> FacultyAsync()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var client = new ApiClient(api.Factory.CreateClient());
        await client.LoginAsync(author.Username, World.Password);
        return (client, s, author);
    }

    [Fact]
    public async Task Creates_an_exam_for_a_batch()
    {
        var (client, s, _) = await FacultyAsync();
        var vm = new ExamEditorViewModel(client, null);
        await vm.LoadAsync();
        vm.Title = World.Unique("Editor");
        vm.Department = vm.Departments.Single(d => d.Id == s.BranchId);
        vm.Year = vm.Years.Single(y => y.Id == s.ClassId);
        vm.Batch = vm.Batches.Single(b => b.Id == s.BatchId);
        vm.StartDate = DateTime.Today;
        vm.StartTime = "08:00";
        vm.EndDate = DateTime.Today.AddDays(1);
        vm.EndTime = "08:00";
        vm.Questions[0].Text = "Reverse a linked list";

        var (request, error) = vm.BuildRequest();
        var saved = await vm.SaveAsync(request!);

        Assert.Null(error);
        var exam = await client.ExamAsync(saved.Id);
        Assert.Equal(ExamEditorViewModel.ToUtc(DateTime.Today, new TimeSpan(8, 0, 0)), exam.StartsAt);
        Assert.Equal((s.BranchId, s.ClassId, s.BatchId), (exam.BranchId, exam.ClassId, exam.BatchId));
        Assert.Equal("Reverse a linked list", Assert.Single(exam.Questions!).Text);
    }

    [Fact]
    public async Task Editing_keeps_question_ids()
    {
        var (client, s, author) = await FacultyAsync();
        var existing = await _world.ExamAsync(author.Id, s.Batch);
        var vm = new ExamEditorViewModel(client, existing.Id);
        await vm.LoadAsync();
        var firstId = vm.Questions[0].Id;

        vm.Questions[0].Text = "Changed";
        await vm.SaveAsync(vm.BuildRequest().Request!);

        var exam = await client.ExamAsync(existing.Id);
        Assert.Equal(2, exam.Questions!.Count);
        Assert.Equal((firstId, "Changed"), (exam.Questions[0].Id, exam.Questions[0].Text));
        Assert.Equal(existing.Title, vm.Title);
    }

    [Fact]
    public async Task Server_rules_come_back_as_field_errors()
    {
        var (client, s, _) = await FacultyAsync();
        var vm = new ExamEditorViewModel(client, null);
        await vm.LoadAsync();
        vm.Department = vm.Departments.Single(d => d.Id == s.BranchId);
        vm.Title = "";

        var e = await Assert.ThrowsAsync<ApiError>(() => vm.SaveAsync(vm.BuildRequest().Request!));

        Assert.Equal(400, e.Status);
        Assert.Contains("title", e.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ExamEditor"`
Expected: build errors — `ExamEditorViewModel` not found.

- [ ] **Step 3: Implement the view model**

`clients/Secucode.Desktop/Features/Staff/ExamEditorViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Globalization;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Features.Staff;

public sealed record MasterOption(int? Id, string Name);

public sealed class QuestionRow
{
    public int? Id { get; init; }
    public string Text { get; set; } = "";
}

// Create or edit an exam. Staff type local times; the API gets UTC.
public sealed class ExamEditorViewModel(ApiClient api, int? examId)
{
    private static readonly string[] TimeFormats = [@"h\:mm", @"hh\:mm"];

    public bool IsNew => examId is null;
    public string Title { get; set; } = "";
    public DateTime? StartDate { get; set; } = DateTime.Today;
    public string StartTime { get; set; } = "09:00";
    public DateTime? EndDate { get; set; } = DateTime.Today;
    public string EndTime { get; set; } = "11:00";
    public string Duration { get; set; } = "60";
    public bool AllowWeb { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowQuestionEditAfterAttempts { get; set; } = true;
    public bool AllowAttemptReset { get; set; } = true;
    public bool AllowAttemptReopen { get; set; } = true;
    public bool AllowTimeExtension { get; set; } = true;

    public List<MasterOption> Departments { get; private set; } = [];
    public List<MasterOption> Years { get; private set; } = [];
    public List<MasterOption> Batches { get; private set; } = [];
    public MasterOption? Department { get; set; }
    public MasterOption? Year { get; set; }
    public MasterOption? Batch { get; set; }
    public ObservableCollection<QuestionRow> Questions { get; } = [];

    public async Task LoadAsync()
    {
        static IEnumerable<MasterOption> Options(List<MasterDto> list) => list.Select(m => new MasterOption(m.Id, m.Name));
        Departments = Options(await api.MastersAsync("branches")).ToList();
        Years = [new MasterOption(null, "Whole department"), .. Options(await api.MastersAsync("classes"))];
        Batches = [new MasterOption(null, "Whole year"), .. Options(await api.MastersAsync("batches"))];

        if (examId is not int id)
        {
            Department = Departments.FirstOrDefault();
            Year = Years[0];
            Batch = Batches[0];
            Questions.Add(new QuestionRow());
            return;
        }

        var e = await api.ExamAsync(id);
        Title = e.Title;
        (StartDate, StartTime) = Split(e.StartsAt);
        (EndDate, EndTime) = Split(e.EndsAt);
        Duration = e.DurationMinutes.ToString();
        AllowWeb = e.Mode == ExamMode.WebAllowed;
        IsActive = e.IsActive;
        AllowQuestionEditAfterAttempts = e.AllowQuestionEditAfterAttempts;
        AllowAttemptReset = e.AllowAttemptReset;
        AllowAttemptReopen = e.AllowAttemptReopen;
        AllowTimeExtension = e.AllowTimeExtension;
        Department = Departments.FirstOrDefault(d => d.Id == e.BranchId);
        Year = Years.FirstOrDefault(y => y.Id == e.ClassId) ?? Years[0];
        Batch = Batches.FirstOrDefault(b => b.Id == e.BatchId) ?? Batches[0];
        foreach (var q in e.Questions ?? [])
            Questions.Add(new QuestionRow { Id = q.Id, Text = q.Text });
    }

    public static DateTime ToUtc(DateTime localDate, TimeSpan time) =>
        DateTime.SpecifyKind(localDate.Date + time, DateTimeKind.Local).ToUniversalTime();

    public static (DateTime Date, string Time) Split(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return (local.Date, local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    // Catches only what can't be sent; every other rule is the server's.
    public (ExamRequest? Request, string? Error) BuildRequest()
    {
        if (StartDate is null || EndDate is null) return (null, "Choose the start and end dates.");
        if (!TryTime(StartTime, out var start) || !TryTime(EndTime, out var end)) return (null, "Use HH:mm for times, for example 09:30.");
        if (!int.TryParse(Duration.Trim(), out var minutes)) return (null, "Duration must be a whole number of minutes.");
        if (Department?.Id is not int branchId) return (null, "Choose a department.");

        var questions = Questions.Where(q => !string.IsNullOrWhiteSpace(q.Text))
            .Select(q => new QuestionInput(q.Id, q.Text.Trim())).ToList();
        return (new ExamRequest(Title.Trim(), ToUtc(StartDate.Value, start), ToUtc(EndDate.Value, end), minutes,
            AllowWeb ? ExamMode.WebAllowed : ExamMode.DesktopOnly, IsActive, branchId, Year?.Id, Batch?.Id,
            AllowQuestionEditAfterAttempts, AllowAttemptReset, AllowAttemptReopen, AllowTimeExtension, questions), null);
    }

    public Task<ExamDto> SaveAsync(ExamRequest request) =>
        examId is int id ? api.UpdateExamAsync(id, request) : api.CreateExamAsync(request);

    public void MoveQuestion(QuestionRow row, int delta)
    {
        var from = Questions.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Questions.Count) return;
        Questions.Move(from, to);
    }

    private static bool TryTime(string text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test clients/Secucode.Desktop.Tests --filter "FullyQualifiedName~ExamEditor"`
Expected: PASS (6 tests).

- [ ] **Step 5: The editor page**

`clients/Secucode.Desktop/Features/Staff/ExamEditorPage.xaml`:

```xml
<UserControl x:Class="Secucode.Desktop.Features.Staff.ExamEditorPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:theme="clr-namespace:Secucode.Desktop.Theme">
    <DockPanel>
        <StackPanel DockPanel.Dock="Top" Margin="0,0,0,16">
            <Button Style="{StaticResource SubtleButton}" HorizontalAlignment="Left" Margin="-8,0,0,8" Click="Back_Click">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="&#xE72B;" Style="{StaticResource Icon}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBlock Text="Exams"/>
                </StackPanel>
            </Button>
            <TextBlock x:Name="PageTitle" Style="{StaticResource Title}"/>
        </StackPanel>
        <DockPanel DockPanel.Dock="Bottom" Margin="0,12,0,0">
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="Cancel" Margin="0,0,8,0" Click="Back_Click"/>
                <Button x:Name="SaveButton" Content="Save exam" Style="{StaticResource PrimaryButton}" Click="Save_Click"/>
            </StackPanel>
            <theme:InfoBar x:Name="Info" Margin="0,0,16,0"/>
        </DockPanel>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="Form" MaxWidth="900" HorizontalAlignment="Left">
                <Border Style="{StaticResource Card}" Margin="0,0,0,12">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition/>
                            <ColumnDefinition Width="24"/>
                            <ColumnDefinition/>
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Title" Style="{StaticResource Caption}" Margin="0,0,0,6"/>
                            <TextBox Text="{Binding Title}"/>
                            <TextBlock Text="Starts (date, time)" Style="{StaticResource Caption}" Margin="0,14,0,6"/>
                            <StackPanel Orientation="Horizontal">
                                <DatePicker SelectedDate="{Binding StartDate}" Width="150"/>
                                <TextBox Text="{Binding StartTime}" Width="80" Margin="8,0,0,0"/>
                            </StackPanel>
                            <TextBlock Text="Ends (date, time)" Style="{StaticResource Caption}" Margin="0,14,0,6"/>
                            <StackPanel Orientation="Horizontal">
                                <DatePicker SelectedDate="{Binding EndDate}" Width="150"/>
                                <TextBox Text="{Binding EndTime}" Width="80" Margin="8,0,0,0"/>
                            </StackPanel>
                            <TextBlock Text="Duration (minutes)" Style="{StaticResource Caption}" Margin="0,14,0,6"/>
                            <TextBox Text="{Binding Duration}" Width="100" HorizontalAlignment="Left"/>
                        </StackPanel>
                        <StackPanel Grid.Column="2">
                            <TextBlock Text="Department" Style="{StaticResource Caption}" Margin="0,0,0,6"/>
                            <ComboBox ItemsSource="{Binding Departments}" SelectedItem="{Binding Department}" DisplayMemberPath="Name"/>
                            <TextBlock Text="Year" Style="{StaticResource Caption}" Margin="0,14,0,6"/>
                            <ComboBox ItemsSource="{Binding Years}" SelectedItem="{Binding Year}" DisplayMemberPath="Name"/>
                            <TextBlock Text="Batch" Style="{StaticResource Caption}" Margin="0,14,0,6"/>
                            <ComboBox ItemsSource="{Binding Batches}" SelectedItem="{Binding Batch}" DisplayMemberPath="Name"/>
                            <CheckBox Style="{StaticResource ToggleSwitch}" IsChecked="{Binding IsActive}" Content="Active" Margin="0,20,0,0"/>
                            <CheckBox Style="{StaticResource ToggleSwitch}" IsChecked="{Binding AllowWeb}" Content="Students may also use the web app" Margin="0,10,0,0"/>
                        </StackPanel>
                    </Grid>
                </Border>

                <Border Style="{StaticResource Card}" Margin="0,0,0,12">
                    <StackPanel>
                        <TextBlock Text="Staff corrections for this exam" FontWeight="SemiBold"/>
                        <TextBlock Text="An exam can only turn these off; the global settings still apply." Style="{StaticResource Caption}" Margin="0,2,0,12"/>
                        <CheckBox Style="{StaticResource ToggleSwitch}" IsChecked="{Binding AllowAttemptReset}" Content="Reset attempts" Margin="0,0,0,8"/>
                        <CheckBox Style="{StaticResource ToggleSwitch}" IsChecked="{Binding AllowAttemptReopen}" Content="Reopen attempts" Margin="0,0,0,8"/>
                        <CheckBox Style="{StaticResource ToggleSwitch}" IsChecked="{Binding AllowTimeExtension}" Content="Extend time" Margin="0,0,0,8"/>
                        <CheckBox Style="{StaticResource ToggleSwitch}" IsChecked="{Binding AllowQuestionEditAfterAttempts}" Content="Edit questions after students start"/>
                    </StackPanel>
                </Border>

                <Border Style="{StaticResource Card}">
                    <StackPanel>
                        <DockPanel Margin="0,0,0,12">
                            <Button DockPanel.Dock="Right" Click="AddQuestion_Click">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="&#xE710;" Style="{StaticResource Icon}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"/>
                                    <TextBlock Text="Add question"/>
                                </StackPanel>
                            </Button>
                            <StackPanel>
                                <TextBlock Text="Questions" FontWeight="SemiBold"/>
                                <TextBlock Text="Each student gets one of these at random." Style="{StaticResource Caption}" Margin="0,2,0,0"/>
                            </StackPanel>
                        </DockPanel>
                        <ItemsControl ItemsSource="{Binding Questions}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <DockPanel Margin="0,0,0,8">
                                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Top" Margin="8,0,0,0">
                                            <Button Style="{StaticResource IconButton}" Content="&#xE70E;" ToolTip="Move up" Click="Up_Click"/>
                                            <Button Style="{StaticResource IconButton}" Content="&#xE70D;" ToolTip="Move down" Click="Down_Click"/>
                                            <Button Style="{StaticResource IconButton}" Content="&#xE74D;" ToolTip="Remove" Click="Remove_Click"/>
                                        </StackPanel>
                                        <TextBox Text="{Binding Text}" AcceptsReturn="True" TextWrapping="Wrap" MinHeight="60"/>
                                    </DockPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </Border>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`clients/Secucode.Desktop/Features/Staff/ExamEditorPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Secucode.Desktop.Api;
using Secucode.Desktop.Shell;
using Secucode.Desktop.Theme;

namespace Secucode.Desktop.Features.Staff;

public partial class ExamEditorPage : UserControl
{
    private readonly ExamEditorViewModel _vm;

    public ExamEditorPage(int? examId)
    {
        InitializeComponent();
        _vm = new ExamEditorViewModel(App.Api, examId);
        PageTitle.Text = _vm.IsNew ? "New exam" : "Edit exam";
        Form.IsEnabled = SaveButton.IsEnabled = false;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _vm.LoadAsync();
            DataContext = _vm; // once, after loading: the view model doesn't raise change notifications
            Form.IsEnabled = SaveButton.IsEnabled = true;
        }
        catch (ApiError error)
        {
            Info.ShowError(error, () => _ = LoadAsync());
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Info.Hide();
        var (request, problem) = _vm.BuildRequest();
        if (request is null)
        {
            Info.Show(InfoSeverity.Error, problem!);
            return;
        }
        SaveButton.IsEnabled = false;
        try
        {
            await _vm.SaveAsync(request);
            ShellWindow.Current?.Navigate(new StaffExamsPage(App.Session!));
        }
        catch (ApiError error)
        {
            Info.ShowError(error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShellWindow.Current?.Navigate(new StaffExamsPage(App.Session!));
    private void AddQuestion_Click(object sender, RoutedEventArgs e) => _vm.Questions.Add(new QuestionRow());
    private static QuestionRow Row(object sender) => (QuestionRow)((FrameworkElement)sender).DataContext;
    private void Up_Click(object sender, RoutedEventArgs e) => _vm.MoveQuestion(Row(sender), -1);
    private void Down_Click(object sender, RoutedEventArgs e) => _vm.MoveQuestion(Row(sender), 1);
    private void Remove_Click(object sender, RoutedEventArgs e) => _vm.Questions.Remove(Row(sender));
}
```

- [ ] **Step 6: Wire the list's New and Edit** — in `StaffExamsPage.xaml.cs` replace the two placeholder handlers:

```csharp
    private void New_Click(object sender, RoutedEventArgs e) => ShellWindow.Current?.Navigate(new ExamEditorPage(null));

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row) ShellWindow.Current?.Navigate(new ExamEditorPage(row.Exam.Id));
    }
```

- [ ] **Step 7: Build, test, check by hand**

Run: `dotnet build Secucode.sln` and `dotnet test clients/Secucode.Desktop.Tests` — expected: 0 errors, PASS.
As `faculty.cse`, use `screenshotting-to-verify` in light and dark: New exam → fill the form, add/reorder/remove questions, toggles animate, Save → back on the list with the new exam showing the times you typed (local). Edit it → fields come back as entered. Save with an empty title → InfoBar shows the server's "Title is required…". Type `9.30` as a time → "Use HH:mm…". Edit an exam that has attempts and change a question with the per-exam "Edit questions after students start" switch off → the server's 403 reason is shown.

- [ ] **Step 8: Commit**

```bash
git add clients/Secucode.Desktop clients/Secucode.Desktop.Tests
git commit -m "feat(desktop): staff exam editor with local-time entry and question ordering

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Retire the MySQL scripts, update the README, verify everything

**Files:**
- Delete: `Database/setup.sql`, `Database/add-user.ps1`
- Modify: `README.md`

- [ ] **Step 1: Delete the retired scripts and check nothing references them**

```bash
git rm -r Database
```

Search the repo for `setup.sql`, `add-user.ps1`, `MySql.Data`, `SECUCODE_DB` and `Db.Open` (excluding `docs/` history and `bin/obj`). Expected: only `README.md` hits, fixed in the next step.

- [ ] **Step 2: Rewrite the README's desktop parts** — replace the old desktop setup instructions and the project-structure block (the lines listing `Database/`, `Db.cs`, `CodeExecutor.cs`, `MainWindow`, `TeacherLoginWindow → …`, `StudentLoginWindow → …`, `TestPage`, `Exam.cs / Test.cs / User.cs`, `Secucode.Checks/`) with:

````markdown
**Run the desktop app** (Windows): start the API first (above), then

```powershell
dotnet run --project clients/Secucode.Desktop        # Debug: focus-loss checks are off
dotnet run --project clients/Secucode.Desktop -c Release
```

Sign in with a demo account. The server address (default `http://localhost:8080`) is under **Server settings** on the sign-in screen and is saved in `%LocalAppData%\Secucode\settings.json`. During an exam, unsent work is kept in `%LocalAppData%\Secucode\outbox\` and sent when the connection returns.

**Desktop tests** (Docker Desktop running; `python` on PATH): `dotnet test clients/Secucode.Desktop.Tests`. Hardware checks (webcam, microphone, screenshot): `dotnet run --project clients/Secucode.Desktop.Checks`.
````

and the structure block with:

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
```

- [ ] **Step 3: Full verification**

Run all of these and read the output:
- `dotnet build Secucode.sln` — 0 errors.
- `dotnet test server/Secucode.Api.Tests --filter "Category!=Docker"` — PASS.
- `dotnet test clients/Secucode.Desktop.Tests` — PASS.
- `dotnet run --project clients/Secucode.Desktop.Checks` — PASS or SKIP only.

Then a last run of the whole app in Release against the API, with `screenshotting-to-verify`, in **light and dark**: sign-in; student exams list; start page; exam window (normal, offline banner, finishing overlay); staff exam list; editor; attempts. Keep the screenshots for the PR description.

- [ ] **Step 4: Commit**

```bash
git add -A Database README.md
git commit -m "chore(desktop): retire MySQL setup scripts and document the API-based desktop app

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
