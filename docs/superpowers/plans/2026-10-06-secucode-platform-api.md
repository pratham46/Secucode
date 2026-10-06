# SecuCode Platform API (Sub-project 0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ASP.NET Core 8 backend API that owns all SecuCode data: configurable roles and capabilities with multi-role scoped staff, server-authoritative exam attempts, audited staff corrections, central evidence storage, live updates, and sandboxed code running.

**Architecture:** One ASP.NET Core 8 minimal-API project (`server/Secucode.Api`) using EF Core 8 + Pomelo on MySQL 8, JWT access tokens with rotated refresh tokens, a per-request permission engine (`Caller`) that evaluates role grants against scopes, a SignalR hub for live exam events, a background auto-close service, and an `ICodeRunner` with Local (process) and Docker (container) implementations. Request/response types live in `server/Secucode.Contracts` so the WPF client can share them later. Integration tests run the real API against MySQL in Testcontainers.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, EF Core 8.0 + Pomelo.EntityFrameworkCore.MySql 8.0.2, Microsoft.AspNetCore.Authentication.JwtBearer, BCrypt.Net-Next, SignalR, Swashbuckle, Docker.DotNet, xUnit, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.MySql, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-06-secucode-platform-backend-design.md`. Read it before starting. Section numbers below (e.g. "spec §5.3") refer to it.

## Before Task 1

The working tree on `main` contains uncommitted fixes from earlier work (WPF fixes, `Database/`, `.gitignore`, untracked `bin/obj`). Ask the user to commit them, then create the branch:

```bash
git switch -c feature/platform-api
```

## Global Constraints

- Target framework `net8.0` for server projects; the WPF app stays `net8.0-windows`.
- Package versions (use exactly these; if one is unavailable, use the newest 8.x/compatible patch and note it in the commit): `Pomelo.EntityFrameworkCore.MySql 8.0.2`, `Microsoft.EntityFrameworkCore.Design 8.0.11`, `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.11`, `Swashbuckle.AspNetCore 6.9.0`, `BCrypt.Net-Next 4.0.3`, `Docker.DotNet 3.125.15`, `Microsoft.AspNetCore.Mvc.Testing 8.0.11`, `Microsoft.AspNetCore.SignalR.Client 8.0.11`, `Microsoft.Extensions.TimeProvider.Testing 8.10.0`, `Testcontainers.MySql 3.10.0`, `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`, `Microsoft.NET.Test.Sdk 17.11.1`, tool `dotnet-ef 8.0.11`.
- MySQL server version pinned in code: `8.0.36` (`DatabaseSetup.ServerVersion`). Test container image: `mysql:8.4`.
- Dev database name is **`secucode_api`**, never `secucode` (the desktop app still uses `secucode` until sub-project 1).
- Docker Desktop must be running to run the tests (Testcontainers). Python (`python`) and Node (`node`) must be on PATH for the Local runner tests.
- All timestamps are UTC (`DateTime` with `Kind=Utc`); "now" always comes from the injected `TimeProvider`, never `DateTime.UtcNow`.
- All API routes are under `/api`; the hub is `/hubs/exams`. Every endpoint requires a bearer token unless it calls `.AllowAnonymous()`.
- Errors are RFC 7807 problem+json produced by `ApiException` / `ErrorHandling` (Task 3). Never return ad-hoc error bodies.
- Every state-changing staff endpoint writes exactly one audit row via `AuditWriter` in the same `SaveChanges`/transaction as the change (spec §7).
- JSON enums are strings (`JsonStringEnumConverter`) everywhere; tests use `Json.Options` from Contracts.
- Limits copied from the spec: access token 15 min; refresh token 7 days; lockout 5 failures → 5 min; submit grace 30 s; auto-close every 15 s; extra/extension minutes 1–240; evidence ≤ 10 MB (jpeg/png/wav); runner 10 s compile / 5 s run / 64 KB output per stream / 10 runs per minute per user / Docker 256 MB, 0.5 CPU, pids 64, no network.
- Default roles, ranks, scopes and grants exactly as spec §5.4.
- Tests share one MySQL database and run sequentially (one xUnit collection). Every test creates its own uniquely named data; any test that changes shared state (system switches, default role grants) restores it in `finally`.
- Commits: one per task, message prefix `feat(api):` / `test(api):` / `chore:`. End every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Exam times sent without a `Z` suffix** (e.g. `"2026-10-06T10:00:00"` from a form): a reasonable person expects them treated as UTC and returned with `Z`, not shifted by the server's local zone. Pinned in Task 8.
2. **A student deactivated mid-exam** keeps a valid 15-minute access token: they must not be able to keep saving or submitting. Expect 401 on attempt writes. Pinned in Task 12.
3. **Hindi/Marathi text and emoji in questions and code** (`नमस्ते 🙂`): must round-trip byte-exact through MySQL (utf8mb4). Pinned in Task 12.
4. **An exam whose questions were all removed after it went live**: starting must fail cleanly with 409, not crash with 500. Pinned in Task 12.
5. **Refresh tokens after a password reset or deactivation**: a stolen or old session must stop working. Expect 401 on refresh. Pinned in Task 6.

## File Map

```
Secucode.sln                                   (modified: new paths + 3 server projects)
.config/dotnet-tools.json                      dotnet-ef local tool
docker-compose.yml, .env.example               Task 16
clients/Secucode.Desktop/                      moved from Secucode/ (unchanged code)
clients/Secucode.Desktop.Checks/               moved from Secucode.Checks/
clients/Secucode_installer/                    moved from Secucode_installer/
server/Secucode.Contracts/
  Secucode.Contracts.csproj
  Json.cs                                      shared serializer options
  Enums.cs                                     ClientKind, ExamMode, AttemptStatus, Reach, ScopeLevels, Capability, AttemptEventType, EvidenceKind
  Common.cs                                    ScopeDto, PageDto<T>
  AuthContracts.cs  UserContracts.cs  RoleContracts.cs  MasterContracts.cs
  ExamContracts.cs  RunContracts.cs  LiveContracts.cs  AttemptContracts.cs
  CorrectionContracts.cs  AuditContracts.cs
server/Secucode.Api/
  Secucode.Api.csproj, Program.cs, ServiceSetup.cs, Endpoints.cs, Dockerfile
  appsettings.json, appsettings.Development.json
  Common/      ApiException.cs  Validator.cs  ErrorHandling.cs  TimeExtensions.cs  AuditWriter.cs
  Data/        AppDbContext.cs  DatabaseSetup.cs  UtcDateTimeConverter.cs  Seeder.cs  Migrations/
  Data/Entities/  Identity.cs  Academics.cs  Exams.cs  Admin.cs
  Auth/        Scope.cs  RoleNames.cs  DefaultRoles.cs  JwtOptions.cs  TokenService.cs  LoginThrottle.cs  Caller.cs  AuthEndpoints.cs
  Features/Users/      UserEndpoints.cs  UserMapping.cs  AssignmentRules.cs
  Features/Roles/      RoleEndpoints.cs  AssignmentEndpoints.cs
  Features/Masters/    MasterEndpoints.cs  SettingsEndpoints.cs
  Features/Exams/      ExamEndpoints.cs  ExamMapping.cs  Switches.cs
  Features/Runner/     RunnerOptions.cs  ICodeRunner.cs  LocalCodeRunner.cs  DockerCodeRunner.cs  RunnerEndpoints.cs
  Features/Live/       ExamHub.cs  LiveNotifier.cs
  Features/Attempts/   AttemptEndpoints.cs  AttemptMapping.cs  EvidenceStore.cs  AutoCloseService.cs
  Features/Corrections/CorrectionEndpoints.cs
  Features/Audit/      AuditEndpoints.cs
server/Secucode.Api.Tests/
  Secucode.Api.Tests.csproj
  Infrastructure/  ApiFixture.cs  ApiFactory.cs  World.cs  Http.cs  LiveClient.cs
  HealthTests.cs  ReferenceDataTests.cs  ScopeTests.cs  AuthTests.cs  PermissionTests.cs
  MasterAndSettingsTests.cs  UserTests.cs  RoleTests.cs  ExamTests.cs  LocalRunnerTests.cs
  DockerRunnerTests.cs  LiveHubTests.cs  AttemptTests.cs  AutoCloseTests.cs  CorrectionTests.cs
  AuditTests.cs  SeedTests.cs
```

---
### Task 1: Repository layout, server project skeleton, test harness

Moves the WPF app under `clients/`, creates the three server projects, and proves the test harness can boot the API against a MySQL container.

**Files:**
- Move: `Secucode/` → `clients/Secucode.Desktop/`, `Secucode.Checks/` → `clients/Secucode.Desktop.Checks/`, `Secucode_installer/` → `clients/Secucode_installer/`
- Modify: `Secucode.sln`, `clients/Secucode.Desktop.Checks/Secucode.Checks.csproj`, `clients/Secucode.Desktop.Checks/Program.cs`, `clients/Secucode_installer/Secucode_installer.vdproj`, `README.md`
- Create: `server/Secucode.Contracts/Secucode.Contracts.csproj`, `server/Secucode.Contracts/Json.cs`
- Create: `server/Secucode.Api/Secucode.Api.csproj`, `Program.cs`, `ServiceSetup.cs`, `Endpoints.cs`, `appsettings.json`, `appsettings.Development.json`
- Create: `server/Secucode.Api.Tests/Secucode.Api.Tests.csproj`, `Infrastructure/ApiFactory.cs`, `Infrastructure/ApiFixture.cs`, `Infrastructure/Http.cs`, `HealthTests.cs`
- Create: `.config/dotnet-tools.json`

**Interfaces:**
- Produces: `Program` (public partial, for `WebApplicationFactory`), `ServiceSetup.AddSecucodeApi(IServiceCollection, IConfiguration)`, `ServiceSetup.UseSecucodeApi(WebApplication)`, `Endpoints.MapSecucodeEndpoints(WebApplication)` with a `RouteGroupBuilder api` for `/api`; `Json.Options`; test `ApiFixture` (`Factory`, `Time`, `ConnectionString(string db)`), `ApiCollection.Name`, `Http` extensions `PostJsonAsync/PutJsonAsync/PatchJsonAsync/ReadAsync<T>/ExpectAsync/ExpectProblemAsync`.

- [ ] **Step 1: Move the desktop projects**

```bash
mkdir -p clients
git mv Secucode clients/Secucode.Desktop
git mv Secucode.Checks clients/Secucode.Desktop.Checks
git mv Secucode_installer clients/Secucode_installer
rm -rf Secucode Secucode.Checks Secucode_installer   # leftover untracked bin/obj only
```

In `Secucode.sln` replace these three paths (keep the GUIDs):

| Old | New |
|---|---|
| `"Secucode\Secucode.csproj"` | `"clients\Secucode.Desktop\Secucode.csproj"` |
| `"Secucode_installer\Secucode_installer.vdproj"` | `"clients\Secucode_installer\Secucode_installer.vdproj"` |
| `"Secucode.Checks\Secucode.Checks.csproj"` | `"clients\Secucode.Desktop.Checks\Secucode.Checks.csproj"` |

In `clients/Secucode.Desktop.Checks/Secucode.Checks.csproj` change both `..\Secucode\` link paths to `..\Secucode.Desktop\`.
In `clients/Secucode.Desktop.Checks/Program.cs` change the cascade path segment `"Secucode"` to `"Secucode.Desktop"`:

```csharp
string cascade = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Secucode.Desktop", "haarcascade_frontalface_default.xml");
```

In `clients/Secucode_installer/Secucode_installer.vdproj` replace every `..\\Secucode\\` with `..\\Secucode.Desktop\\`.
In `README.md` replace `dotnet run --project Secucode.Checks` with `dotnet run --project clients/Secucode.Desktop.Checks`, `dotnet run --project Secucode` with `dotnet run --project clients/Secucode.Desktop`, and the layout line `Secucode/                     the WPF app` with `clients/Secucode.Desktop/      the WPF app`.

- [ ] **Step 2: Verify the desktop still builds and its checks pass**

Run: `dotnet build clients/Secucode.Desktop/Secucode.csproj -v q` → `Build succeeded`.
Run: `dotnet run --project clients/Secucode.Desktop.Checks` → ends with `All checks passed.` (Java/C++ may print SKIP).

- [ ] **Step 3: Create the Contracts project**

`server/Secucode.Contracts/Secucode.Contracts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`server/Secucode.Contracts/Json.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Secucode.Contracts;

// One serializer configuration for the API, its tests and (later) the clients: web casing, enums as strings.
public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
```

- [ ] **Step 4: Create the API project**

`server/Secucode.Api/Secucode.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Secucode.Api</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
    <PackageReference Include="Docker.DotNet" Version="3.125.15" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.2" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.9.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Secucode.Contracts\Secucode.Contracts.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Secucode.Api.Tests" />
  </ItemGroup>
</Project>
```

`server/Secucode.Api/Program.cs`:

```csharp
using Secucode.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSecucodeApi(builder.Configuration);

var app = builder.Build();
app.UseSecucodeApi();
app.Run();

public partial class Program;
```

`server/Secucode.Api/ServiceSetup.cs`:

```csharp
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;

namespace Secucode.Api;

public static class ServiceSetup
{
    public static IServiceCollection AddSecucodeApi(this IServiceCollection services, IConfiguration config)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(config.GetSection("Cors:WebOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
            });
        });
        return services;
    }

    public static WebApplication UseSecucodeApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors();
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.MapSecucodeEndpoints();
        return app;
    }
}
```

`server/Secucode.Api/Endpoints.cs`:

```csharp
namespace Secucode.Api;

public static class Endpoints
{
    public static void MapSecucodeEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
    }
}
```

`server/Secucode.Api/appsettings.json`:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "ConnectionStrings": { "Default": "" },
  "Jwt": { "Issuer": "secucode", "Audience": "secucode", "AccessTokenMinutes": 15, "RefreshTokenDays": 7 },
  "Cors": { "WebOrigins": [ "http://localhost:5173" ] },
  "Evidence": { "Root": "evidence" },
  "Attempts": { "AutoCloseEnabled": true, "AutoCloseIntervalSeconds": 15 },
  "Runner": { "Mode": "Local" }
}
```

`server/Secucode.Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": { "Default": "Server=localhost;Database=secucode_api;User Id=root;Password=root;" },
  "Jwt": { "Key": "dev-only-signing-key-change-me-0123456789abcdef" }
}
```

- [ ] **Step 5: Create the test project and harness**

`server/Secucode.Api.Tests/Secucode.Api.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.11" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="8.10.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="Testcontainers.MySql" Version="3.10.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Secucode.Api\Secucode.Api.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

`server/Secucode.Api.Tests/Infrastructure/ApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Secucode.Api.Tests.Infrastructure;

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    // Starts at the real "now" so JWTs and MySQL timestamps look normal; tests move it forward with Advance().
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
    public string EvidenceRoot { get; } = Directory.CreateTempSubdirectory("secucode_evidence_").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Jwt:Key", "test-signing-key-0123456789abcdef0123456789");
        builder.UseSetting("Runner:Mode", "Local");
        builder.UseSetting("Evidence:Root", EvidenceRoot);
        builder.UseSetting("Attempts:AutoCloseEnabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }
}
```

`server/Secucode.Api.Tests/Infrastructure/ApiFixture.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using MySqlConnector;
using Testcontainers.MySql;

namespace Secucode.Api.Tests.Infrastructure;

// One MySQL container and one API host for the whole run. Tests run sequentially in one collection
// and isolate themselves by creating uniquely named data.
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder()
        .WithImage("mysql:8.4")
        .WithUsername("root")
        .WithPassword("root")
        .Build();

    public ApiFactory Factory { get; private set; } = null!;
    public FakeTimeProvider Time => Factory.Time;

    public string ConnectionString(string database) =>
        new MySqlConnectionStringBuilder(_mysql.GetConnectionString()) { Database = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        Factory = new ApiFactory(ConnectionString("secucode_test"));
        _ = Factory.Server; // boots the app (and, from Task 2 on, migrates the database)
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _mysql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
```

`server/Secucode.Api.Tests/Infrastructure/Http.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Secucode.Contracts;

namespace Secucode.Api.Tests.Infrastructure;

public static class Http
{
    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient c, string url, T body) => c.PostAsJsonAsync(url, body, Json.Options);
    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient c, string url, T body) => c.PutAsJsonAsync(url, body, Json.Options);
    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient c, string url, T body) => c.PatchAsJsonAsync(url, body, Json.Options);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await r.ExpectAsync(expected);
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    public static async Task<JsonElement> ExpectAsync(this HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {(int)expected} {expected} but got {(int)r.StatusCode} {r.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    // Asserts an RFC 7807 problem response and returns its "detail".
    public static async Task<string> ExpectProblemAsync(this HttpResponseMessage r, HttpStatusCode expected)
    {
        var json = await r.ExpectAsync(expected);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return json.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
    }

    public static async Task<string[]> ValidationFieldsAsync(this HttpResponseMessage r)
    {
        var json = await r.ExpectAsync(HttpStatusCode.BadRequest);
        return json.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToArray();
    }
}
```

- [ ] **Step 6: Write the failing test**

`server/Secucode.Api.Tests/HealthTests.cs`:

```csharp
using System.Net;
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class HealthTests(ApiFixture api)
{
    [Fact]
    public async Task Health_is_anonymous_and_ok()
    {
        var body = await (await api.Factory.CreateClient().GetAsync("/api/health")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_routes_return_a_problem()
    {
        await (await api.Factory.CreateClient().GetAsync("/api/nope")).ExpectProblemAsync(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 7: Add projects to the solution and the EF tool**

```bash
dotnet sln Secucode.sln add server/Secucode.Contracts/Secucode.Contracts.csproj server/Secucode.Api/Secucode.Api.csproj server/Secucode.Api.Tests/Secucode.Api.Tests.csproj
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 8.0.11
```

- [ ] **Step 8: Run the tests**

Start Docker Desktop, then run: `dotnet test server/Secucode.Api.Tests --filter HealthTests`
Expected: 2 passed. (Before Steps 4–5 existed, this failed to compile — that was the "red".)

- [ ] **Step 9: Commit**

```bash
git add -A clients server Secucode.sln .config README.md
git commit -m "chore: move desktop app under clients/ and add API skeleton with test harness"
```

---

### Task 2: Data model, migrations, reference data

Creates every table from spec §4, the `Scope` value type (spec §5.2) and the default roles (spec §5.4), applied automatically at startup.

**Files:**
- Create: `server/Secucode.Contracts/Enums.cs`, `server/Secucode.Contracts/Common.cs`
- Create: `server/Secucode.Api/Common/TimeExtensions.cs`
- Create: `server/Secucode.Api/Auth/Scope.cs`, `Auth/RoleNames.cs`, `Auth/DefaultRoles.cs`
- Create: `server/Secucode.Api/Data/Entities/Identity.cs`, `Academics.cs`, `Exams.cs`, `Admin.cs`
- Create: `server/Secucode.Api/Data/AppDbContext.cs`, `Data/UtcDateTimeConverter.cs`, `Data/DatabaseSetup.cs`, `Data/Seeder.cs`
- Create (generated): `server/Secucode.Api/Data/Migrations/*`
- Modify: `server/Secucode.Api/ServiceSetup.cs`, `server/Secucode.Api/Program.cs`
- Test: `server/Secucode.Api.Tests/ScopeTests.cs`, `server/Secucode.Api.Tests/ReferenceDataTests.cs`

**Interfaces:**
- Consumes: Task 1 skeleton.
- Produces: enums `ClientKind, ExamMode, AttemptStatus, Reach, ScopeLevels (flags), Capability, AttemptEventType, EvidenceKind`; `ScopeDto(int? BranchId, int? ClassId, int? BatchId)`, `PageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)`; `Scope` (`Institution`, `Level`, `IsValidShape`, `Contains(Scope)`, `ToDto()`); `RoleNames.*`; entities `User, Role, RoleCapability, RoleAssignment (Scope, IsActiveOn(DateOnly)), RefreshToken, MasterEntity, Branch, SchoolClass, Batch, Exam (Target), Question, Attempt, AttemptEvent, EvidenceFile, SystemSettings, AuditEntry`; `AppDbContext` DbSets `Users, Roles, RoleCapabilities, RoleAssignments, RefreshTokens, Branches, Classes, Batches, Exams, Questions, Attempts, AttemptEvents, EvidenceFiles, SystemSettings, AuditLog`; `DatabaseSetup.ServerVersion`, `IServiceProvider.InitializeDatabaseAsync()`; `Seeder.EnsureReferenceDataAsync(AppDbContext, TimeProvider)`; `TimeProvider.UtcNow()`, `TimeProvider.Today()`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/ScopeTests.cs`:

```csharp
using Secucode.Api.Auth;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

public sealed class ScopeTests
{
    private static readonly Scope Dept = new(1, null, null);
    private static readonly Scope Year = new(1, 2, null);
    private static readonly Scope Batch = new(1, 2, 3);

    [Fact]
    public void Levels_follow_the_set_fields()
    {
        Assert.Equal(ScopeLevels.Institution, Scope.Institution.Level);
        Assert.Equal(ScopeLevels.Department, Dept.Level);
        Assert.Equal(ScopeLevels.Year, Year.Level);
        Assert.Equal(ScopeLevels.Batch, Batch.Level);
    }

    [Fact]
    public void Wider_scopes_contain_narrower_ones_but_not_the_reverse()
    {
        Assert.True(Scope.Institution.Contains(Batch));
        Assert.True(Dept.Contains(Year));
        Assert.True(Year.Contains(Batch));
        Assert.True(Batch.Contains(Batch));
        Assert.False(Batch.Contains(Year));
        Assert.False(Year.Contains(Dept));
        Assert.False(Dept.Contains(new Scope(9, 2, 3)));
        Assert.False(Year.Contains(new Scope(1, 9, 3)));
    }

    [Fact]
    public void A_class_needs_a_department_and_a_batch_needs_a_class()
    {
        Assert.True(Batch.IsValidShape);
        Assert.False(new Scope(null, 2, null).IsValidShape);
        Assert.False(new Scope(1, null, 3).IsValidShape);
    }
}
```

`server/Secucode.Api.Tests/ReferenceDataTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Data;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ReferenceDataTests(ApiFixture api)
{
    [Fact]
    public async Task Default_roles_and_settings_are_seeded_at_startup()
    {
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roles = await db.Roles.Include(r => r.Capabilities).AsNoTracking().ToDictionaryAsync(r => r.Name);

        Assert.Equal(
            new Dictionary<string, int>
            {
                [RoleNames.SuperAdmin] = 1000, [RoleNames.Principal] = 900, [RoleNames.ExamController] = 800,
                [RoleNames.Hod] = 700, [RoleNames.SubHod] = 600, [RoleNames.ClassCoordinator] = 400,
                [RoleNames.Faculty] = 300, [RoleNames.Student] = 0,
            },
            roles.Where(r => r.Key is RoleNames.SuperAdmin or RoleNames.Principal or RoleNames.ExamController or RoleNames.Hod
                or RoleNames.SubHod or RoleNames.ClassCoordinator or RoleNames.Faculty or RoleNames.Student)
                .ToDictionary(r => r.Key, r => r.Value.Rank));

        var superAdmin = roles[RoleNames.SuperAdmin];
        Assert.True(superAdmin.IsSystem);
        Assert.Equal(Enum.GetValues<Capability>().Length, superAdmin.Capabilities.Count);
        Assert.All(superAdmin.Capabilities, c => Assert.Equal(Reach.InScope, c.Reach));

        var faculty = roles[RoleNames.Faculty];
        Assert.Equal(Reach.InScope, faculty.Capabilities.Single(c => c.Capability == Capability.CreateExams).Reach);
        Assert.Equal(Reach.OwnOnly, faculty.Capabilities.Single(c => c.Capability == Capability.ResetAttempts).Reach);
        Assert.Equal(ScopeLevels.Department | ScopeLevels.Year | ScopeLevels.Batch, faculty.AllowedScopeLevels);

        var student = roles[RoleNames.Student];
        Assert.True(student.IsStudent);
        Assert.Empty(student.Capabilities);
        Assert.Equal(ScopeLevels.Batch, student.AllowedScopeLevels);

        var settings = await db.SystemSettings.SingleAsync();
        Assert.True(settings.AllowQuestionEditAfterAttempts && settings.AllowAttemptReset && settings.AllowAttemptReopen && settings.AllowTimeExtension);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter "ScopeTests|ReferenceDataTests"`
Expected: build fails — `Scope`, `RoleNames`, `AppDbContext` don't exist.

- [ ] **Step 3: Add shared enums and DTOs**

`server/Secucode.Contracts/Enums.cs`:

```csharp
namespace Secucode.Contracts;

public enum ClientKind { Desktop, Web }

public enum ExamMode { DesktopOnly, WebAllowed }

public enum AttemptStatus { InProgress, Submitted, AutoSubmitted, Voided }

// InScope: anywhere inside the holder's assignment scope. OwnOnly: only on exams the holder created.
public enum Reach { InScope, OwnOnly }

[Flags]
public enum ScopeLevels { None = 0, Institution = 1, Department = 2, Year = 4, Batch = 8 }

public enum Capability
{
    ManageSettings, ManageMasters, ManageRoles, AssignRoles, ManageUsers,
    CreateExams, ManageExams,
    ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts,
    ViewResults, ViewEvidence, ViewAudit,
}

public enum AttemptEventType { FaceMissing, MultipleFaces, FaceMismatch, FocusLost, Minimized, TabHidden, FullscreenExited, AudioUnavailable }

public enum EvidenceKind { ReferenceFace, Webcam, Screenshot, Audio }
```

`server/Secucode.Contracts/Common.cs`:

```csharp
namespace Secucode.Contracts;

// All fields null = whole institution; branch = department; + class = year; + batch = batch.
public sealed record ScopeDto(int? BranchId, int? ClassId, int? BatchId);

public sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
```

- [ ] **Step 4: Add time helpers, Scope and role definitions**

`server/Secucode.Api/Common/TimeExtensions.cs`:

```csharp
namespace Secucode.Api.Common;

public static class TimeExtensions
{
    public static DateTime UtcNow(this TimeProvider time) => time.GetUtcNow().UtcDateTime;
    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.UtcNow());
}
```

`server/Secucode.Api/Auth/Scope.cs`:

```csharp
using Secucode.Contracts;

namespace Secucode.Api.Auth;

// Where a role assignment applies, or what an exam targets (spec §5.2).
public readonly record struct Scope(int? BranchId, int? ClassId, int? BatchId)
{
    public static readonly Scope Institution = new(null, null, null);

    public ScopeLevels Level =>
        BatchId is not null ? ScopeLevels.Batch
        : ClassId is not null ? ScopeLevels.Year
        : BranchId is not null ? ScopeLevels.Department
        : ScopeLevels.Institution;

    public bool IsValidShape => (ClassId is null || BranchId is not null) && (BatchId is null || ClassId is not null);

    // True when every field this scope sets matches the inner scope.
    public bool Contains(Scope inner) =>
        (BranchId is null || BranchId == inner.BranchId)
        && (ClassId is null || ClassId == inner.ClassId)
        && (BatchId is null || BatchId == inner.BatchId);

    public ScopeDto ToDto() => new(BranchId, ClassId, BatchId);
}
```

`server/Secucode.Api/Auth/RoleNames.cs`:

```csharp
namespace Secucode.Api.Auth;

// Names of the seeded roles. Admins may rename them later; code only uses these for seeding and tests.
public static class RoleNames
{
    public const string SuperAdmin = "Super Admin";
    public const string Principal = "Principal";
    public const string ExamController = "Exam Controller";
    public const string Hod = "HOD";
    public const string SubHod = "Sub-HOD";
    public const string ClassCoordinator = "Class Coordinator";
    public const string Faculty = "Faculty";
    public const string Student = "Student";
}
```

`server/Secucode.Api/Auth/DefaultRoles.cs`:

```csharp
using Secucode.Api.Data.Entities;
using Secucode.Contracts;
using static Secucode.Contracts.Capability;
using static Secucode.Contracts.ScopeLevels;

namespace Secucode.Api.Auth;

// The seeded Indian-college hierarchy, exactly as spec §5.4. Everything except Super Admin is editable afterwards.
public static class DefaultRoles
{
    private static readonly Capability[] Corrections = [ResetAttempts, ReopenAttempts, ExtendTime];

    public static List<Role> Create(DateTime now)
    {
        var all = Enum.GetValues<Capability>();
        Capability[] hod = [ManageUsers, AssignRoles, CreateExams, ManageExams, .. Corrections, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit];

        return
        [
            Make(RoleNames.SuperAdmin, 1000, Institution, InScope(all), isSystem: true),
            Make(RoleNames.Principal, 900, Institution, InScope(all.Where(c => c is not (ManageSettings or ManageMasters or ManageRoles)))),
            Make(RoleNames.ExamController, 800, Institution | Department,
                InScope([CreateExams, ManageExams, .. Corrections, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit])),
            Make(RoleNames.Hod, 700, Department, InScope(hod)),
            Make(RoleNames.SubHod, 600, Department, InScope(hod.Where(c => c != AssignRoles))),
            Make(RoleNames.ClassCoordinator, 400, Year | Batch,
                InScope([ManageUsers, CreateExams, .. Corrections, ViewResults, ViewEvidence])),
            Make(RoleNames.Faculty, 300, Department | Year | Batch,
                [.. InScope([CreateExams]), .. OwnOnly([.. Corrections, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit])]),
            Make(RoleNames.Student, 0, Batch, [], isStudent: true),
        ];

        Role Make(string name, int rank, ScopeLevels levels, IEnumerable<RoleCapability> grants, bool isSystem = false, bool isStudent = false) => new()
        {
            Name = name, Rank = rank, AllowedScopeLevels = levels, IsSystem = isSystem, IsStudent = isStudent,
            CreatedAt = now, Capabilities = grants.ToList(),
        };
    }

    private static IEnumerable<RoleCapability> InScope(IEnumerable<Capability> caps) =>
        caps.Select(c => new RoleCapability { Capability = c, Reach = Reach.InScope });

    private static IEnumerable<RoleCapability> OwnOnly(IEnumerable<Capability> caps) =>
        caps.Select(c => new RoleCapability { Capability = c, Reach = Reach.OwnOnly });
}
```

- [ ] **Step 5: Add the entities**

`server/Secucode.Api/Data/Entities/Identity.cs`:

```csharp
using Secucode.Api.Auth;
using Secucode.Contracts;

namespace Secucode.Api.Data.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? PinHash { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public List<RoleAssignment> Assignments { get; set; } = [];
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int Rank { get; set; }
    public bool IsSystem { get; set; }
    public bool IsStudent { get; set; }
    public ScopeLevels AllowedScopeLevels { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<RoleCapability> Capabilities { get; set; } = [];
}

public class RoleCapability
{
    public int RoleId { get; set; }
    public Capability Capability { get; set; }
    public Reach Reach { get; set; }
}

public class RoleAssignment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public int? BranchId { get; set; }
    public int? ClassId { get; set; }
    public int? BatchId { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Scope Scope => new(BranchId, ClassId, BatchId);
    public bool IsActiveOn(DateOnly day) => (ValidFrom is null || ValidFrom <= day) && (ValidTo is null || ValidTo >= day);
}

public class RefreshToken
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public ClientKind Client { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? ReplacedById { get; set; }
    public RefreshToken? ReplacedBy { get; set; }
}
```

`server/Secucode.Api/Data/Entities/Academics.cs`:

```csharp
namespace Secucode.Api.Data.Entities;

// Masters share a shape so one set of endpoints can serve all three (Task 5).
public abstract class MasterEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Branch : MasterEntity { }      // a department, e.g. Computer Engineering

public class SchoolClass : MasterEntity { } // a year, e.g. TE

public class Batch : MasterEntity { }       // a group within a year, e.g. A1
```

`server/Secucode.Api/Data/Entities/Exams.cs`:

```csharp
using Secucode.Api.Auth;
using Secucode.Contracts;

namespace Secucode.Api.Data.Entities;

public class Exam
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int DurationMinutes { get; set; }
    public ExamMode Mode { get; set; }
    public bool IsActive { get; set; } = true;
    public int BranchId { get; set; }
    public int? ClassId { get; set; }
    public int? BatchId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool AllowQuestionEditAfterAttempts { get; set; } = true;
    public bool AllowAttemptReset { get; set; } = true;
    public bool AllowAttemptReopen { get; set; } = true;
    public bool AllowTimeExtension { get; set; } = true;
    public List<Question> Questions { get; set; } = [];

    public Scope Target => new(BranchId, ClassId, BatchId);
}

public class Question
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
}

public class Attempt
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;
    public int AttemptNumber { get; set; }
    public int QuestionId { get; set; }
    public string QuestionText { get; set; } = ""; // snapshot: later question edits never change it
    public ClientKind Client { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime DeadlineAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public AttemptStatus Status { get; set; }
    public string? SubmitReason { get; set; }
    public string? Language { get; set; }
    public string Code { get; set; } = "";
    public int Strikes { get; set; }
    public DateTime? LastSavedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    public int? AllowanceMinutes { get; set; } // set by a reset after the window closed
    public List<AttemptEvent> Events { get; set; } = [];
    public List<EvidenceFile> Evidence { get; set; } = [];
}

public class AttemptEvent
{
    public long Id { get; set; }
    public int AttemptId { get; set; }
    public AttemptEventType Type { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Detail { get; set; }
    public long? EvidenceId { get; set; }
}

public class EvidenceFile
{
    public long Id { get; set; }
    public int AttemptId { get; set; }
    public EvidenceKind Kind { get; set; }
    public string StoragePath { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

`server/Secucode.Api/Data/Entities/Admin.cs`:

```csharp
namespace Secucode.Api.Data.Entities;

public class SystemSettings
{
    public int Id { get; set; } = 1; // single row
    public bool AllowQuestionEditAfterAttempts { get; set; } = true;
    public bool AllowAttemptReset { get; set; } = true;
    public bool AllowAttemptReopen { get; set; } = true;
    public bool AllowTimeExtension { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public class AuditEntry
{
    public long Id { get; set; }
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public int? ExamId { get; set; }
    public int? BranchId { get; set; }
    public string? Reason { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Client { get; set; }
    public string? Ip { get; set; }
    public DateTime OccurredAt { get; set; }
}
```

- [ ] **Step 6: Add the DbContext and database setup**

`server/Secucode.Api/Data/UtcDateTimeConverter.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Secucode.Api.Data;

// MySQL DATETIME has no zone; everything we store is UTC, so mark it UTC on the way out.
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
```

`server/Secucode.Api/Data/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleCapability> RoleCapabilities => Set<RoleCapability>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptEvent> AttemptEvents => Set<AttemptEvent>();
    public DbSet<EvidenceFile> EvidenceFiles => Set<EvidenceFile>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        b.Properties<ClientKind>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<ExamMode>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<AttemptStatus>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<Reach>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<Capability>().HaveConversion<string>().HaveMaxLength(40);
        b.Properties<AttemptEventType>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<EvidenceKind>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.PasswordHash).HasMaxLength(100);
            e.Property(x => x.PinHash).HasMaxLength(100);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(200);
        });

        m.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
        });

        m.Entity<RoleCapability>(e =>
        {
            e.ToTable("role_capabilities");
            e.HasKey(x => new { x.RoleId, x.Capability });
            e.HasOne<Role>().WithMany(r => r.Capabilities).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<RoleAssignment>(e =>
        {
            e.ToTable("role_assignments");
            e.Ignore(x => x.Scope);
            e.HasOne(x => x.User).WithMany(u => u.Assignments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReplacedBy).WithMany().HasForeignKey(x => x.ReplacedById).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Branch>(e => { e.ToTable("branches"); e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(100); });
        m.Entity<SchoolClass>(e => { e.ToTable("classes"); e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(100); });
        m.Entity<Batch>(e => { e.ToTable("batches"); e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(100); });

        m.Entity<Exam>(e =>
        {
            e.ToTable("exams");
            e.Ignore(x => x.Target);
            e.Property(x => x.Title).HasMaxLength(255);
            e.HasIndex(x => x.StartsAt);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(q => q.ExamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Question>(e =>
        {
            e.ToTable("questions");
            e.Property(x => x.Text).HasColumnType("text");
        });

        m.Entity<Attempt>(e =>
        {
            e.ToTable("attempts");
            e.HasIndex(x => new { x.ExamId, x.StudentId, x.AttemptNumber }).IsUnique();
            e.HasIndex(x => new { x.Status, x.DeadlineAt });
            e.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.QuestionText).HasColumnType("text");
            e.Property(x => x.Code).HasColumnType("mediumtext");
            e.Property(x => x.Language).HasMaxLength(20);
            e.Property(x => x.SubmitReason).HasMaxLength(255);
            e.Property(x => x.VoidReason).HasMaxLength(500);
        });

        m.Entity<AttemptEvent>(e =>
        {
            e.ToTable("attempt_events");
            e.HasOne<Attempt>().WithMany(a => a.Events).HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Detail).HasMaxLength(500);
        });

        m.Entity<EvidenceFile>(e =>
        {
            e.ToTable("evidence_files");
            e.HasOne<Attempt>().WithMany(a => a.Evidence).HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.StoragePath).HasMaxLength(500);
            e.Property(x => x.ContentType).HasMaxLength(100);
        });

        m.Entity<SystemSettings>(e =>
        {
            e.ToTable("system_settings");
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        m.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_log");
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.BeforeJson).HasColumnType("json");
            e.Property(x => x.AfterJson).HasColumnType("json");
            e.Property(x => x.Client).HasMaxLength(16);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ExamId);
            e.HasIndex(x => x.BranchId);
            e.HasIndex(x => x.OccurredAt);
        });
    }
}
```

`server/Secucode.Api/Data/Seeder.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data.Entities;

namespace Secucode.Api.Data;

public static class Seeder
{
    // Reference data the API can't work without. Safe to run on every startup.
    public static async Task EnsureReferenceDataAsync(AppDbContext db, TimeProvider time)
    {
        var now = time.UtcNow();
        if (!await db.Roles.AnyAsync())
            db.Roles.AddRange(DefaultRoles.Create(now));
        if (!await db.SystemSettings.AnyAsync())
            db.SystemSettings.Add(new SystemSettings { Id = 1, UpdatedAt = now });
        await db.SaveChangesAsync();
    }
}
```

`server/Secucode.Api/Data/DatabaseSetup.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Secucode.Api.Data;

public static class DatabaseSetup
{
    // Pinned so neither startup nor `dotnet ef` has to connect just to detect the version.
    public static readonly ServerVersion ServerVersion = new MySqlServerVersion(new Version(8, 0, 36));

    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await Seeder.EnsureReferenceDataAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }
}

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseMySql("Server=localhost;Database=secucode_api", DatabaseSetup.ServerVersion).Options);
}
```

- [ ] **Step 7: Register the DbContext and migrate at startup**

In `ServiceSetup.AddSecucodeApi`, add after `services.AddProblemDetails();`:

```csharp
        services.AddDbContext<AppDbContext>(o => o.UseMySql(config.GetConnectionString("Default"), DatabaseSetup.ServerVersion));
```

and add `using Microsoft.EntityFrameworkCore;` and `using Secucode.Api.Data;` at the top.

In `Program.cs`, replace `app.Run();` with:

```csharp
await app.Services.InitializeDatabaseAsync();
app.Run();
```

and add `using Secucode.Api.Data;`.

- [ ] **Step 8: Generate the migration**

Run: `dotnet ef migrations add Initial --project server/Secucode.Api --output-dir Data/Migrations`
Expected: `Done.` and new files under `server/Secucode.Api/Data/Migrations/`. Open the generated `*_Initial.cs` and check it creates all 15 tables (`users`, `roles`, `role_capabilities`, `role_assignments`, `refresh_tokens`, `branches`, `classes`, `batches`, `exams`, `questions`, `attempts`, `attempt_events`, `evidence_files`, `system_settings`, `audit_log`) and **no** `MasterEntity` table.

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter "ScopeTests|ReferenceDataTests|HealthTests"`
Expected: all passed.

- [ ] **Step 10: Commit**

```bash
git add -A server
git commit -m "feat(api): data model, migrations, default roles and scopes"
```

---

### Task 3: Errors, auth (login/refresh/logout/me), audit writer, test world

Adds the problem+json error pipeline, JWT auth with rotated refresh tokens and lockout (spec §9 Auth, §11), the audit writer (spec §7) and the shared test helper `World`.

**Files:**
- Create: `server/Secucode.Contracts/AuthContracts.cs`
- Create: `server/Secucode.Api/Common/ApiException.cs`, `Common/Validator.cs`, `Common/ErrorHandling.cs`, `Common/AuditWriter.cs`
- Create: `server/Secucode.Api/Auth/JwtOptions.cs`, `Auth/TokenService.cs`, `Auth/LoginThrottle.cs`, `Auth/Caller.cs`, `Auth/AuthEndpoints.cs`
- Create: `server/Secucode.Api/Features/Users/UserMapping.cs`
- Modify: `server/Secucode.Api/ServiceSetup.cs`, `server/Secucode.Api/Endpoints.cs`
- Create: `server/Secucode.Api.Tests/Infrastructure/World.cs`
- Test: `server/Secucode.Api.Tests/AuthTests.cs`

**Interfaces:**
- Consumes: Task 2 entities, `Scope`, `TimeExtensions`.
- Produces:
  - Contracts: `LoginRequest(string Username, string Password, ClientKind Client)`, `RefreshRequest(string RefreshToken)`, `LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User)`, `UserDto(int Id, string Username, string FullName, string? Email, bool IsActive, bool HasPin, IReadOnlyList<AssignmentDto> Assignments)`, `AssignmentDto(int Id, int RoleId, string RoleName, int? BranchId, int? ClassId, int? BatchId, DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive)`, `EffectiveCapabilityDto(Capability Capability, Reach Reach, ScopeDto Scope, string RoleName)`, `MeResponse(UserDto User, IReadOnlyList<EffectiveCapabilityDto> Capabilities)`.
  - `ApiException(int status, string title, string? detail = null, IDictionary<string,string[]>? errors = null)` with `NotFound(string what)`, `Forbidden(string detail)`, `Conflict(string detail)`, `Unauthorized(string detail)`, `TooManyRequests(string detail)`, `Validation(string field, string message)`.
  - `Validator.Rule(bool ok, string field, string message)`, `Validator.ThrowIfInvalid()`.
  - `ErrorHandling.UseApiErrors(IApplicationBuilder)`, `ErrorHandling.WriteAsync(HttpContext, int status, string title, string? detail, IDictionary<string,string[]>? errors)`.
  - `AuditWriter.Record(int? actorId, string action, string entityType, object entityId, object? before = null, object? after = null, string? reason = null, int? examId = null, int? branchId = null)` — adds to the DbContext; the caller's `SaveChangesAsync` persists it.
  - `TokenService.ClientClaim = "client"`, `TokenService.CreateAccessToken(int userId, ClientKind client) → (string Token, DateTime ExpiresAt)`, `TokenService.NewRefreshToken()`, `TokenService.Hash(string)`, `TokenService.RefreshTokenDays`.
  - `Caller` (scoped): `Principal`, `UserId`, `Client`, `Today`, `ActiveAssignmentsAsync()`.
  - `UserMapping.ToDto(this User, DateOnly today)`, `UserMapping.ToDto(this RoleAssignment, DateOnly today)`.
  - Test `World`, `TestUser(int Id, string Username)`, `Section(BranchId, ClassId, BatchId)` with `.Batch/.Year/.Department` scopes.

- [ ] **Step 1: Write the test helper `World`**

`server/Secucode.Api.Tests/Infrastructure/World.cs`:

```csharp
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Tests.Infrastructure;

public sealed record TestUser(int Id, string Username);

public sealed record Section(int BranchId, int ClassId, int BatchId)
{
    public Scope Batch => new(BranchId, ClassId, BatchId);
    public Scope Year => new(BranchId, ClassId, null);
    public Scope Department => new(BranchId, null, null);
}

// Builds test data straight into the database (fast) so each test exercises only the API call under test.
public sealed class World(ApiFixture api)
{
    public const string Password = "Pass@1234";
    public const string Pin = "1234";
    private static int _counter;

    public DateTime Now => api.Time.GetUtcNow().UtcDateTime;
    public DateOnly Today => DateOnly.FromDateTime(Now);

    public static string Unique(string prefix) => $"{prefix}{Interlocked.Increment(ref _counter)}_{Guid.NewGuid().ToString("N")[..6]}";

    public async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = api.Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task WithDb(Func<AppDbContext, Task> work) => WithDb(async db => { await work(db); return true; });

    public Task<Section> SectionAsync() => WithDb(async db =>
    {
        var branch = new Branch { Name = Unique("Dept") };
        var cls = new SchoolClass { Name = Unique("Year") };
        var batch = new Batch { Name = Unique("Batch") };
        db.AddRange(branch, cls, batch);
        await db.SaveChangesAsync();
        return new Section(branch.Id, cls.Id, batch.Id);
    });

    public Task<int> RoleIdAsync(string roleName) =>
        WithDb(db => db.Roles.Where(r => r.Name == roleName).Select(r => r.Id).SingleAsync());

    // A user holding one role at the given scope. Students (and withPin) get PIN "1234".
    public Task<TestUser> UserAsync(string roleName, Scope scope, DateOnly? validFrom = null, DateOnly? validTo = null,
        bool withPin = false, bool active = true) => WithDb(async db =>
    {
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);
        var user = new User
        {
            Username = Unique("u"),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 4),
            PinHash = role.IsStudent || withPin ? BCrypt.Net.BCrypt.HashPassword(Pin, 4) : null,
            FullName = $"{roleName} Tester",
            IsActive = active,
            CreatedAt = Now,
        };
        user.Assignments.Add(new RoleAssignment
        {
            RoleId = role.Id, BranchId = scope.BranchId, ClassId = scope.ClassId, BatchId = scope.BatchId,
            ValidFrom = validFrom, ValidTo = validTo, CreatedAt = Now,
        });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new TestUser(user.Id, user.Username);
    });

    // A user with no role assignments (to test assigning roles to them).
    public Task<TestUser> BareUserAsync(bool withPin = false) => WithDb(async db =>
    {
        var user = new User
        {
            Username = Unique("u"), PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 4),
            PinHash = withPin ? BCrypt.Net.BCrypt.HashPassword(Pin, 4) : null, FullName = "Bare Tester", CreatedAt = Now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new TestUser(user.Id, user.Username);
    });

    public Task<int> AssignAsync(int userId, string roleName, Scope scope, DateOnly? validFrom = null, DateOnly? validTo = null) => WithDb(async db =>
    {
        var roleId = await db.Roles.Where(r => r.Name == roleName).Select(r => r.Id).SingleAsync();
        var a = new RoleAssignment
        {
            UserId = userId, RoleId = roleId, BranchId = scope.BranchId, ClassId = scope.ClassId, BatchId = scope.BatchId,
            ValidFrom = validFrom, ValidTo = validTo, CreatedAt = Now,
        };
        db.RoleAssignments.Add(a);
        await db.SaveChangesAsync();
        return a.Id;
    });

    // A custom role created directly in the database (bypassing role-admin rules).
    public Task<int> CustomRoleAsync(string name, int rank, ScopeLevels levels, params (Capability Capability, Reach Reach)[] grants) => WithDb(async db =>
    {
        var role = new Role
        {
            Name = name, Rank = rank, AllowedScopeLevels = levels, CreatedAt = Now,
            Capabilities = grants.Select(g => new RoleCapability { Capability = g.Capability, Reach = g.Reach }).ToList(),
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    });

    public async Task<LoginResponse> LoginResponseAsync(TestUser user, ClientKind client = ClientKind.Web, string password = Password)
    {
        var res = await api.Factory.CreateClient().PostJsonAsync("/api/auth/login", new LoginRequest(user.Username, password, client));
        return await res.ReadAsync<LoginResponse>();
    }

    public async Task<HttpClient> LoginAsync(TestUser user, ClientKind client = ClientKind.Web) =>
        Client((await LoginResponseAsync(user, client)).AccessToken);

    public HttpClient Client(string accessToken)
    {
        var c = api.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return c;
    }

    // An exam open from 5 minutes ago for 2 hours unless told otherwise.
    public Task<Exam> ExamAsync(int createdBy, Scope target, ExamMode mode = ExamMode.WebAllowed, TimeSpan? startsIn = null,
        TimeSpan? window = null, int durationMinutes = 30, int questions = 2, Action<Exam>? configure = null) => WithDb(async db =>
    {
        var starts = Now + (startsIn ?? TimeSpan.FromMinutes(-5));
        var exam = new Exam
        {
            Title = Unique("Exam"), StartsAt = starts, EndsAt = starts + (window ?? TimeSpan.FromHours(2)),
            DurationMinutes = durationMinutes, Mode = mode, BranchId = target.BranchId!.Value, ClassId = target.ClassId,
            BatchId = target.BatchId, CreatedBy = createdBy, CreatedAt = Now,
        };
        for (var i = 1; i <= questions; i++)
            exam.Questions.Add(new Question { Text = $"Question {i}", SortOrder = i - 1 });
        configure?.Invoke(exam);
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        return exam;
    });

    public Task<Attempt> AttemptAsync(int examId, int studentId, AttemptStatus status = AttemptStatus.InProgress,
        TimeSpan? deadlineIn = null, int attemptNumber = 1) => WithDb(async db =>
    {
        var q = await db.Questions.Where(x => x.ExamId == examId && !x.IsDeleted).OrderBy(x => x.SortOrder).FirstAsync();
        var a = new Attempt
        {
            ExamId = examId, StudentId = studentId, AttemptNumber = attemptNumber, QuestionId = q.Id, QuestionText = q.Text,
            Client = ClientKind.Desktop, StartedAt = Now, DeadlineAt = Now + (deadlineIn ?? TimeSpan.FromMinutes(30)),
            Status = status, SubmittedAt = status is AttemptStatus.Submitted or AttemptStatus.AutoSubmitted ? Now : null,
            Code = "print(1)", Language = "Python",
        };
        db.Attempts.Add(a);
        await db.SaveChangesAsync();
        return a;
    });

    public Task<Attempt> ReloadAttemptAsync(int id) => WithDb(db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == id));

    public Task SetSwitchesAsync(bool on) => WithDb(db => db.SystemSettings.ExecuteUpdateAsync(s => s
        .SetProperty(x => x.AllowQuestionEditAfterAttempts, on)
        .SetProperty(x => x.AllowAttemptReset, on)
        .SetProperty(x => x.AllowAttemptReopen, on)
        .SetProperty(x => x.AllowTimeExtension, on)));

    public Task<int> AuditCountAsync() => WithDb(db => db.AuditLog.CountAsync());

    public Task<AuditEntry> LastAuditAsync() => WithDb(db => db.AuditLog.AsNoTracking().OrderByDescending(e => e.Id).FirstAsync());

    public static ClaimsPrincipal PrincipalFor(int userId, ClientKind client = ClientKind.Web) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString()), new Claim("client", client.ToString())], "test"));

    // Runs permission checks directly against the engine, as the given user.
    public Task<T> AsCaller<T>(TestUser user, Func<Caller, AppDbContext, Task<T>> check) =>
        WithDb(db => check(new Caller(db, api.Time) { Principal = PrincipalFor(user.Id) }, db));
}
```

- [ ] **Step 2: Write the failing tests**

`server/Secucode.Api.Tests/AuthTests.cs`:

```csharp
using System.Net;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private HttpClient Anonymous => api.Factory.CreateClient();

    [Fact]
    public async Task Login_returns_tokens_and_the_users_assignments()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.UserAsync(RoleNames.Hod, s.Department);

        var login = await _world.LoginResponseAsync(hod);

        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));
        Assert.Equal(hod.Username, login.User.Username);
        var a = Assert.Single(login.User.Assignments);
        Assert.Equal(RoleNames.Hod, a.RoleName);
        Assert.Equal(s.BranchId, a.BranchId);
        Assert.True(a.IsActive);
        Assert.Equal("auth.login", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Wrong_password_is_a_401_problem_and_is_audited()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);

        var res = await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, "wrong", ClientKind.Web));

        Assert.Equal("Invalid username or password.", await res.ExpectProblemAsync(HttpStatusCode.Unauthorized));
        var last = await _world.LastAuditAsync();
        Assert.Equal("auth.login_failed", last.Action);
        Assert.Equal(u.Username, last.EntityId);
        Assert.Null(last.ActorUserId);
    }

    [Fact]
    public async Task Inactive_users_cannot_log_in()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department, active: false);
        var res = await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, World.Password, ClientKind.Web));
        await res.ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_for_five_minutes()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        for (var i = 0; i < 5; i++)
            await (await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, "wrong", ClientKind.Web)))
                .ExpectProblemAsync(HttpStatusCode.Unauthorized);

        var locked = await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, World.Password, ClientKind.Web));
        await locked.ExpectProblemAsync(HttpStatusCode.TooManyRequests);

        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await _world.LoginResponseAsync(u);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_an_old_token_revokes_every_session()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var first = await _world.LoginResponseAsync(u);

        var second = await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken))).ReadAsync<LoginResponse>();
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(second.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_tokens_expire_after_seven_days()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var login = await _world.LoginResponseAsync(u);
        api.Time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var login = await _world.LoginResponseAsync(u);

        await (await _world.Client(login.AccessToken).PostJsonAsync("/api/auth/logout", new RefreshRequest(login.RefreshToken)))
            .ExpectAsync(HttpStatusCode.NoContent);

        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        await (await Anonymous.GetAsync("/api/auth/me")).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Access_tokens_expire_after_fifteen_minutes()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var client = await _world.LoginAsync(u);
        await (await client.GetAsync("/api/auth/me")).ExpectAsync(HttpStatusCode.OK);

        api.Time.Advance(TimeSpan.FromMinutes(16));
        await (await client.GetAsync("/api/auth/me")).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_union_of_active_roles_only()
    {
        var s = await _world.SectionAsync();
        var u = await _world.UserAsync(RoleNames.Faculty, s.Department);
        await _world.AssignAsync(u.Id, RoleNames.Hod, s.Department);
        await _world.AssignAsync(u.Id, RoleNames.ClassCoordinator, s.Year, validTo: _world.Today.AddDays(-1));

        var me = await (await (await _world.LoginAsync(u)).GetAsync("/api/auth/me")).ReadAsync<MeResponse>();

        Assert.Equal(3, me.User.Assignments.Count);
        Assert.Contains(me.Capabilities, c => c.Capability == Capability.AssignRoles && c.RoleName == RoleNames.Hod);
        Assert.Contains(me.Capabilities, c => c is { Capability: Capability.ResetAttempts, Reach: Reach.OwnOnly, RoleName: RoleNames.Faculty });
        Assert.DoesNotContain(me.Capabilities, c => c.RoleName == RoleNames.ClassCoordinator);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter AuthTests`
Expected: build fails — `LoginRequest`, `Caller` etc. don't exist.

- [ ] **Step 4: Add the auth contracts**

`server/Secucode.Contracts/AuthContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record LoginRequest(string Username, string Password, ClientKind Client);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User);

public sealed record UserDto(int Id, string Username, string FullName, string? Email, bool IsActive, bool HasPin, IReadOnlyList<AssignmentDto> Assignments);

public sealed record AssignmentDto(int Id, int RoleId, string RoleName, int? BranchId, int? ClassId, int? BatchId,
    DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive);

public sealed record EffectiveCapabilityDto(Capability Capability, Reach Reach, ScopeDto Scope, string RoleName);

public sealed record MeResponse(UserDto User, IReadOnlyList<EffectiveCapabilityDto> Capabilities);
```

- [ ] **Step 5: Add errors, validation and the audit writer**

`server/Secucode.Api/Common/ApiException.cs`:

```csharp
namespace Secucode.Api.Common;

// Thrown anywhere in a request; ErrorHandling turns it into an RFC 7807 response.
public sealed class ApiException(int status, string title, string? detail = null, IDictionary<string, string[]>? errors = null)
    : Exception(detail ?? title)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiException NotFound(string what) => new(404, "Not found", $"{what} was not found.");
    public static ApiException Forbidden(string detail) => new(403, "Forbidden", detail);
    public static ApiException Conflict(string detail) => new(409, "Conflict", detail);
    public static ApiException Unauthorized(string detail) => new(401, "Unauthorized", detail);
    public static ApiException TooManyRequests(string detail) => new(429, "Too many requests", detail);

    public static ApiException Validation(string field, string message) =>
        new(400, "Validation failed", "One or more fields are invalid.", new Dictionary<string, string[]> { [field] = [message] });
}
```

`server/Secucode.Api/Common/Validator.cs`:

```csharp
namespace Secucode.Api.Common;

public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public Validator Rule(bool ok, string field, string message)
    {
        if (!ok)
        {
            if (!_errors.TryGetValue(field, out var list))
                _errors[field] = list = [];
            list.Add(message);
        }
        return this;
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
            throw new ApiException(400, "Validation failed", "One or more fields are invalid.",
                _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
```

`server/Secucode.Api/Common/ErrorHandling.cs`:

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Secucode.Contracts;

namespace Secucode.Api.Common;

public static class ErrorHandling
{
    public static IApplicationBuilder UseApiErrors(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        try
        {
            await next(ctx);
        }
        catch (ApiException ex)
        {
            await WriteAsync(ctx, ex.Status, ex.Title, ex.Detail, ex.Errors);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            // e.g. two simultaneous "start attempt" calls racing for the same attempt number
            await WriteAsync(ctx, 409, "Conflict", "That conflicts with an existing record. Refresh and try again.", null);
        }
    });

    public static Task WriteAsync(HttpContext ctx, int status, string title, string? detail, IDictionary<string, string[]>? errors)
    {
        ProblemDetails problem = errors is null ? new ProblemDetails() : new ValidationProblemDetails(errors);
        problem.Status = status;
        problem.Title = title;
        problem.Detail = detail;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? ctx.TraceIdentifier;
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync<object>(problem, Json.Options, "application/problem+json");
    }
}
```

`server/Secucode.Api/Common/AuditWriter.cs`:

```csharp
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Secucode.Api.Auth;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Common;

// Adds an audit row to the same DbContext as the change, so one SaveChanges (or one transaction) persists both.
public sealed class AuditWriter(AppDbContext db, IHttpContextAccessor http, TimeProvider time)
{
    public void Record(int? actorId, string action, string entityType, object entityId, object? before = null, object? after = null,
        string? reason = null, int? examId = null, int? branchId = null)
    {
        var ctx = http.HttpContext;
        db.AuditLog.Add(new AuditEntry
        {
            ActorUserId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = Convert.ToString(entityId, CultureInfo.InvariantCulture) ?? "",
            ExamId = examId,
            BranchId = branchId,
            Reason = reason,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, Json.Options),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, Json.Options),
            Client = ctx?.User.FindFirstValue(TokenService.ClientClaim),
            Ip = ctx?.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = time.UtcNow(),
        });
    }
}
```

- [ ] **Step 6: Add tokens, lockout and the caller**

`server/Secucode.Api/Auth/JwtOptions.cs`:

```csharp
namespace Secucode.Api.Auth;

public sealed class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "secucode";
    public string Audience { get; set; } = "secucode";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
```

`server/Secucode.Api/Auth/TokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Secucode.Api.Common;
using Secucode.Contracts;

namespace Secucode.Api.Auth;

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    public const string ClientClaim = "client";

    public int RefreshTokenDays => options.Value.RefreshTokenDays;

    // Only identity + client go in the token; permissions are loaded per request (spec §5.3).
    public (string Token, DateTime ExpiresAt) CreateAccessToken(int userId, ClientKind client)
    {
        var o = options.Value;
        var now = time.UtcNow();
        var expires = now.AddMinutes(o.AccessTokenMinutes);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(o.Issuer, o.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClientClaim, client.ToString())],
            notBefore: now, expires: expires, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static string NewRefreshToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
```

`server/Secucode.Api/Auth/LoginThrottle.cs`:

```csharp
using System.Collections.Concurrent;

namespace Secucode.Api.Auth;

// ponytail: in-memory, per API instance; move to the database or Redis if the API ever runs as several instances.
public sealed class LoginThrottle(TimeProvider time)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset? LockedUntil)> _state = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLocked(string username) =>
        _state.TryGetValue(username, out var s) && s.LockedUntil is { } until && until > time.GetUtcNow();

    public void Fail(string username) => _state.AddOrUpdate(username,
        _ => (1, null),
        (_, s) =>
        {
            var now = time.GetUtcNow();
            var failures = s.LockedUntil is { } until && until <= now ? 1 : s.Failures + 1; // a served lock starts a fresh count
            return (failures, failures >= MaxFailures ? now + LockFor : null);
        });

    public void Succeed(string username) => _state.TryRemove(username, out _);
}
```

`server/Secucode.Api/Auth/Caller.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Auth;

// The signed-in user for this request. Assignments are read from the database once per request,
// so role changes take effect immediately (spec §5.3).
public sealed class Caller(AppDbContext db, TimeProvider time)
{
    private IReadOnlyList<RoleAssignment>? _active;

    public ClaimsPrincipal Principal { get; set; } = new();

    public int UserId => int.TryParse(Principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
        ? id
        : throw ApiException.Unauthorized("You need to sign in.");

    public ClientKind Client => Enum.TryParse<ClientKind>(Principal.FindFirstValue(TokenService.ClientClaim), out var c) ? c : ClientKind.Web;

    public DateOnly Today => time.Today();

    public async Task<IReadOnlyList<RoleAssignment>> ActiveAssignmentsAsync()
    {
        if (_active is not null)
            return _active;
        var today = Today;
        var userId = UserId;
        _active = await db.RoleAssignments.AsNoTracking()
            .Include(a => a.Role).ThenInclude(r => r.Capabilities)
            .Where(a => a.UserId == userId && a.User.IsActive
                && (a.ValidFrom == null || a.ValidFrom <= today)
                && (a.ValidTo == null || a.ValidTo >= today))
            .ToListAsync();
        return _active;
    }
}
```

- [ ] **Step 7: Add the user mapping and auth endpoints**

`server/Secucode.Api/Features/Users/UserMapping.cs`:

```csharp
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Users;

public static class UserMapping
{
    public static AssignmentDto ToDto(this RoleAssignment a, DateOnly today) =>
        new(a.Id, a.RoleId, a.Role.Name, a.BranchId, a.ClassId, a.BatchId, a.ValidFrom, a.ValidTo, a.IsActiveOn(today));

    public static UserDto ToDto(this User u, DateOnly today) =>
        new(u.Id, u.Username, u.FullName, u.Email, u.IsActive, u.PinHash is not null,
            u.Assignments.OrderByDescending(a => a.Role.Rank).Select(a => a.ToDto(today)).ToList());
}
```

`server/Secucode.Api/Auth/AuthEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Users;
using Secucode.Contracts;

namespace Secucode.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/auth");
        g.MapPost("/login", LoginAsync).AllowAnonymous();
        g.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        g.MapPost("/logout", LogoutAsync);
        g.MapGet("/me", MeAsync);
    }

    private static async Task<LoginResponse> LoginAsync(LoginRequest req, AppDbContext db, TokenService tokens, LoginThrottle throttle,
        AuditWriter audit, TimeProvider time)
    {
        new Validator()
            .Rule(!string.IsNullOrWhiteSpace(req.Username), "username", "Username is required.")
            .Rule(!string.IsNullOrEmpty(req.Password), "password", "Password is required.")
            .ThrowIfInvalid();

        var username = req.Username.Trim();
        if (throttle.IsLocked(username))
            throw ApiException.TooManyRequests("Too many failed attempts. Try again in a few minutes.");

        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username);
        if (user is null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            throttle.Fail(username);
            audit.Record(null, "auth.login_failed", "User", username);
            await db.SaveChangesAsync();
            throw ApiException.Unauthorized("Invalid username or password.");
        }

        throttle.Succeed(username);
        audit.Record(user.Id, "auth.login", "User", user.Id, after: new { req.Client });
        var response = await IssueAsync(db, tokens, time, user, req.Client, replacing: null);
        await db.SaveChangesAsync();
        return response;
    }

    private static async Task<LoginResponse> RefreshAsync(RefreshRequest req, AppDbContext db, TokenService tokens, TimeProvider time)
    {
        var hash = TokenService.Hash(req.RefreshToken ?? "");
        var now = time.UtcNow();
        var rt = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash)
            ?? throw ApiException.Unauthorized("Invalid refresh token.");

        if (rt.RevokedAt is not null)
        {
            // A rotated token came back: assume it was stolen and sign the user out everywhere.
            await db.RefreshTokens.Where(t => t.UserId == rt.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
            throw ApiException.Unauthorized("This session was already used. Please sign in again.");
        }
        if (rt.ExpiresAt <= now)
            throw ApiException.Unauthorized("Your session expired. Please sign in again.");

        var user = await db.Users.SingleAsync(u => u.Id == rt.UserId);
        if (!user.IsActive)
            throw ApiException.Unauthorized("This account is deactivated.");

        var response = await IssueAsync(db, tokens, time, user, rt.Client, replacing: rt);
        await db.SaveChangesAsync();
        return response;
    }

    private static async Task<IResult> LogoutAsync(RefreshRequest req, AppDbContext db, Caller caller, TimeProvider time)
    {
        var hash = TokenService.Hash(req.RefreshToken ?? "");
        var userId = caller.UserId;
        var rt = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId);
        if (rt is { RevokedAt: null })
        {
            rt.RevokedAt = time.UtcNow();
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    private static async Task<MeResponse> MeAsync(Caller caller, AppDbContext db)
    {
        var userId = caller.UserId;
        var user = await db.Users.Include(u => u.Assignments).ThenInclude(a => a.Role).AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw ApiException.Unauthorized("This account is deactivated.");

        var capabilities = (await caller.ActiveAssignmentsAsync())
            .SelectMany(a => a.Role.Capabilities.Select(c => new EffectiveCapabilityDto(c.Capability, c.Reach, a.Scope.ToDto(), a.Role.Name)))
            .ToList();
        return new MeResponse(user.ToDto(caller.Today), capabilities);
    }

    private static async Task<LoginResponse> IssueAsync(AppDbContext db, TokenService tokens, TimeProvider time, User user,
        ClientKind client, RefreshToken? replacing)
    {
        var now = time.UtcNow();
        var (access, expires) = tokens.CreateAccessToken(user.Id, client);
        var raw = TokenService.NewRefreshToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id, Client = client, TokenHash = TokenService.Hash(raw),
            CreatedAt = now, ExpiresAt = now.AddDays(tokens.RefreshTokenDays),
        };
        db.RefreshTokens.Add(refresh);
        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedBy = refresh;
        }

        await db.Entry(user).Collection(u => u.Assignments).Query().Include(a => a.Role).LoadAsync();
        return new LoginResponse(access, raw, expires, user.ToDto(time.Today()));
    }
}
```

- [ ] **Step 8: Wire authentication, errors and the caller into the pipeline**

In `ServiceSetup.AddSecucodeApi`, add before `return services;`:

```csharp
        services.AddOptions<JwtOptions>().Bind(config.GetSection("Jwt"))
            .Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= 32, "Jwt:Key must be at least 32 bytes.")
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((o, jwt, time) =>
            {
                o.MapInboundClaims = false; // keep "sub" as "sub"
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key)),
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                    // Use the injected clock so tests can move time.
                    LifetimeValidator = (notBefore, expires, _, _) =>
                    {
                        var now = time.GetUtcNow().UtcDateTime;
                        return (notBefore is null || notBefore.Value <= now.AddMinutes(1)) && (expires is null || expires.Value > now);
                    },
                };
                o.Events = new JwtBearerEvents
                {
                    // SignalR sends the token in the query string for WebSockets.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddSingleton<TokenService>();
        services.AddSingleton<LoginThrottle>();
        services.AddScoped<Caller>();
        services.AddScoped<AuditWriter>();
```

Add these usings to `ServiceSetup.cs`:

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Secucode.Api.Auth;
using Secucode.Api.Common;
```

Replace `UseSecucodeApi` with:

```csharp
    public static WebApplication UseSecucodeApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseApiErrors();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (ctx, next) =>
        {
            ctx.RequestServices.GetRequiredService<Caller>().Principal = ctx.User;
            await next(ctx);
        });
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.MapSecucodeEndpoints();
        return app;
    }
```

In `Endpoints.MapSecucodeEndpoints`, add after the health line:

```csharp
        api.MapAuth();
```

and add `using Secucode.Api.Auth;`.

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests`
Expected: all tests so far pass (Health, Scope, ReferenceData, Auth).

- [ ] **Step 10: Commit**

```bash
git add -A server
git commit -m "feat(api): problem+json errors, JWT auth with refresh rotation and lockout, audit writer"
```

---

### Task 4: Permission engine

Implements spec §5.3: capability checks against scopes, OwnOnly reach, the exam-creator rule, rank comparisons and user targeting.

**Files:**
- Modify: `server/Secucode.Api/Auth/Caller.cs` (add members)
- Test: `server/Secucode.Api.Tests/PermissionTests.cs`

**Interfaces:**
- Consumes: Task 3 `Caller.ActiveAssignmentsAsync()`, `World.AsCaller`.
- Produces (on `Caller`): `IsStaffAsync()`, `StudentScopeAsync() → Scope?`, `IsSuperAdminAsync()`, `HighestRankAsync() → int` (−1 with no roles), `HasAnywhereAsync(Capability)`, `CanAsync(Capability, Scope target, int? examCreatorId = null)`, `CanOnExamAsync(Capability, Exam)`, `OutranksAsync(Role)`, `OutranksUserAsync(User)` (target must have `Assignments` + `Role` loaded), `CanManageUserAsync(Capability, User)`, `RequireAsync(Capability, Scope, string message)`, `RequireInstitutionAsync(Capability)`, `RequireOnExamAsync(Capability, Exam, string message)`, `RequireStaffAsync()`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/PermissionTests.cs`:

```csharp
using System.Net;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Data.Entities;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using static Secucode.Contracts.Capability;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class PermissionTests(ApiFixture api)
{
    private readonly World _world = new(api);

    // Spec §5.4, written out independently of DefaultRoles so a wrong seed is caught.
    private static readonly Dictionary<string, Capability[]> InScopeGrants = new()
    {
        [RoleNames.SuperAdmin] = Enum.GetValues<Capability>(),
        [RoleNames.Principal] = Enum.GetValues<Capability>().Except([ManageSettings, ManageMasters, ManageRoles]).ToArray(),
        [RoleNames.ExamController] = [CreateExams, ManageExams, ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit],
        [RoleNames.Hod] = [ManageUsers, AssignRoles, CreateExams, ManageExams, ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit],
        [RoleNames.SubHod] = [ManageUsers, CreateExams, ManageExams, ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit],
        [RoleNames.ClassCoordinator] = [ManageUsers, CreateExams, ResetAttempts, ReopenAttempts, ExtendTime, ViewResults, ViewEvidence],
        [RoleNames.Faculty] = [CreateExams],
        [RoleNames.Student] = [],
    };

    private static readonly Capability[] FacultyOwnOnly = [ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit];

    [Fact]
    public async Task Default_roles_grant_exactly_the_spec_capabilities_inside_their_scope_only()
    {
        var home = await _world.SectionAsync();
        var other = await _world.SectionAsync();

        foreach (var (role, granted) in InScopeGrants)
        {
            var scope = role switch
            {
                RoleNames.SuperAdmin or RoleNames.Principal or RoleNames.ExamController => Scope.Institution,
                RoleNames.Hod or RoleNames.SubHod or RoleNames.Faculty => home.Department,
                RoleNames.ClassCoordinator => home.Year,
                _ => home.Batch,
            };
            var user = await _world.UserAsync(role, scope);

            var failures = await _world.AsCaller(user, async (caller, _) =>
            {
                var wrong = new List<string>();
                foreach (var cap in Enum.GetValues<Capability>())
                {
                    var expectedInside = granted.Contains(cap);
                    var expectedOutside = expectedInside && scope == Scope.Institution;
                    if (await caller.CanAsync(cap, home.Batch) != expectedInside) wrong.Add($"{role}/{cap} inside");
                    if (await caller.CanAsync(cap, other.Batch) != expectedOutside) wrong.Add($"{role}/{cap} outside");
                }
                return wrong;
            });
            Assert.Empty(failures);
        }
    }

    [Fact]
    public async Task OwnOnly_grants_apply_to_own_exams_anywhere_and_never_to_colleagues_exams()
    {
        var s = await _world.SectionAsync();
        var me = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var colleague = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var mine = await _world.ExamAsync(me.Id, s.Batch);
        var theirs = await _world.ExamAsync(colleague.Id, s.Batch);

        await _world.AsCaller(me, async (c, _) =>
        {
            foreach (var cap in FacultyOwnOnly)
            {
                Assert.True(await c.CanOnExamAsync(cap, mine), $"{cap} on own exam");
                Assert.False(await c.CanOnExamAsync(cap, theirs), $"{cap} on colleague's exam");
            }
            Assert.True(await c.CanOnExamAsync(ManageExams, mine));   // creator rule
            Assert.False(await c.CanOnExamAsync(ManageExams, theirs));
            return true;
        });
    }

    [Fact]
    public async Task A_multi_role_user_gets_the_union_each_within_its_own_scope()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, a.Department);
        await _world.AssignAsync(user.Id, RoleNames.ClassCoordinator, b.Year);

        await _world.AsCaller(user, async (c, _) =>
        {
            Assert.True(await c.CanAsync(ManageUsers, b.Batch));
            Assert.False(await c.CanAsync(ManageUsers, a.Batch));
            Assert.True(await c.CanAsync(CreateExams, a.Batch));
            Assert.True(await c.CanAsync(CreateExams, b.Batch));
            return true;
        });
    }

    [Fact]
    public async Task Expired_and_future_assignments_grant_nothing_until_they_are_in_force()
    {
        var s = await _world.SectionAsync();
        var expired = await _world.UserAsync(RoleNames.Hod, s.Department, validTo: _world.Today.AddDays(-1));
        var future = await _world.UserAsync(RoleNames.Hod, s.Department, validFrom: _world.Today.AddDays(1));

        Assert.False(await _world.AsCaller(expired, (c, _) => c.CanAsync(ManageExams, s.Batch)));
        Assert.False(await _world.AsCaller(future, (c, _) => c.CanAsync(ManageExams, s.Batch)));

        api.Time.Advance(TimeSpan.FromDays(2));
        Assert.True(await _world.AsCaller(future, (c, _) => c.CanAsync(ManageExams, s.Batch)));
    }

    [Fact]
    public async Task Deactivated_users_have_no_permissions()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.UserAsync(RoleNames.Hod, s.Department, active: false);
        Assert.False(await _world.AsCaller(hod, (c, _) => c.CanAsync(ManageExams, s.Batch)));
    }

    [Fact]
    public async Task Removing_a_role_takes_effect_on_the_very_next_request()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var hodAssignment = await _world.AssignAsync(user.Id, RoleNames.Hod, s.Department);
        var client = await _world.LoginAsync(user);

        var before = await (await client.GetAsync("/api/auth/me")).ReadAsync<MeResponse>();
        Assert.Contains(before.Capabilities, c => c.Capability == AssignRoles);

        await _world.WithDb(db => db.RoleAssignments.Where(a => a.Id == hodAssignment).ExecuteDeleteAsync());

        var after = await (await client.GetAsync("/api/auth/me")).ReadAsync<MeResponse>();
        Assert.DoesNotContain(after.Capabilities, c => c.Capability == AssignRoles);
    }

    [Fact]
    public async Task User_management_respects_rank_scope_and_self()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var hod = await _world.UserAsync(RoleNames.Hod, a.Department);
        var facultyA = await _world.UserAsync(RoleNames.Faculty, a.Department);
        var facultyB = await _world.UserAsync(RoleNames.Faculty, b.Department);
        var principal = await _world.UserAsync(RoleNames.Principal, Scope.Institution);
        var superAdmin = await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution);
        var otherSuperAdmin = await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution);

        Assert.True(await CanManage(hod, facultyA));
        Assert.False(await CanManage(hod, facultyB));   // outside scope
        Assert.False(await CanManage(hod, principal));  // senior
        Assert.False(await CanManage(hod, hod));        // self
        Assert.False(await CanManage(principal, superAdmin));
        Assert.True(await CanManage(superAdmin, otherSuperAdmin)); // only Super Admins manage Super Admins
    }

    private Task<bool> CanManage(TestUser actor, TestUser target) => _world.AsCaller(actor, async (c, db) =>
    {
        var user = await db.Users.Include(u => u.Assignments).ThenInclude(x => x.Role).SingleAsync(u => u.Id == target.Id);
        return await c.CanManageUserAsync(ManageUsers, user);
    });
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter PermissionTests`
Expected: build fails — `CanAsync`, `CanOnExamAsync`, `CanManageUserAsync` don't exist.

- [ ] **Step 3: Add the permission members to `Caller`**

Append inside the `Caller` class (after `ActiveAssignmentsAsync`):

```csharp
    public async Task<bool> IsStaffAsync() => (await ActiveAssignmentsAsync()).Any(a => !a.Role.IsStudent);

    public async Task<Scope?> StudentScopeAsync() =>
        (await ActiveAssignmentsAsync()).Where(a => a.Role.IsStudent).Select(a => (Scope?)a.Scope).FirstOrDefault();

    public async Task<bool> IsSuperAdminAsync() => (await ActiveAssignmentsAsync()).Any(a => a.Role.IsSystem);

    public async Task<int> HighestRankAsync()
    {
        var active = await ActiveAssignmentsAsync();
        return active.Count == 0 ? -1 : active.Max(a => a.Role.Rank);
    }

    public async Task<bool> HasAnywhereAsync(Capability cap) =>
        (await ActiveAssignmentsAsync()).Any(a => a.Role.Capabilities.Any(c => c.Capability == cap));

    // Spec §5.3 step 2.
    public async Task<bool> CanAsync(Capability cap, Scope target, int? examCreatorId = null)
    {
        foreach (var a in await ActiveAssignmentsAsync())
        foreach (var grant in a.Role.Capabilities.Where(c => c.Capability == cap))
        {
            if (grant.Reach == Reach.InScope && a.Scope.Contains(target))
                return true;
            if (grant.Reach == Reach.OwnOnly && examCreatorId is not null && examCreatorId == UserId)
                return true;
        }
        return false;
    }

    // Spec §5.3 step 3: creators can always manage and view their own exam; everything else comes from the grid.
    public async Task<bool> CanOnExamAsync(Capability cap, Exam exam) =>
        (exam.CreatedBy == UserId && cap is Capability.ManageExams or Capability.ViewResults)
        || await CanAsync(cap, exam.Target, exam.CreatedBy);

    // Spec §5.5: lower rank only, except Super Admins may handle the Super Admin role.
    public async Task<bool> OutranksAsync(Role role) =>
        role.Rank < await HighestRankAsync() || (role.IsSystem && await IsSuperAdminAsync());

    // Nobody manages themselves; every role the target holds must be outranked. Target needs Assignments + Role loaded.
    public async Task<bool> OutranksUserAsync(User target)
    {
        if (target.Id == UserId)
            return false;
        foreach (var a in target.Assignments)
            if (!await OutranksAsync(a.Role))
                return false;
        return true;
    }

    public async Task<bool> CanManageUserAsync(Capability cap, User target)
    {
        if (!await OutranksUserAsync(target))
            return false;
        if (target.Assignments.Count == 0)
            return await CanAsync(cap, Scope.Institution);
        foreach (var a in target.Assignments)
            if (await CanAsync(cap, a.Scope))
                return true;
        return false;
    }

    public async Task RequireAsync(Capability cap, Scope target, string message)
    {
        if (!await CanAsync(cap, target))
            throw ApiException.Forbidden(message);
    }

    public Task RequireInstitutionAsync(Capability cap) =>
        RequireAsync(cap, Scope.Institution, $"You need the {cap} permission for the whole institution.");

    public async Task RequireOnExamAsync(Capability cap, Exam exam, string message)
    {
        if (!await CanOnExamAsync(cap, exam))
            throw ApiException.Forbidden(message);
    }

    public async Task RequireStaffAsync()
    {
        if (!await IsStaffAsync())
            throw ApiException.Forbidden("Only staff can do this.");
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter PermissionTests`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add -A server
git commit -m "feat(api): scoped capability engine with OwnOnly reach, creator rule and rank checks"
```

---
### Task 5: Masters and global settings

Departments/classes/batches CRUD (`ManageMasters`) and the global switches (`ManageSettings`), all audited (spec §9 Masters, Settings).

**Files:**
- Create: `server/Secucode.Contracts/MasterContracts.cs`
- Create: `server/Secucode.Api/Features/Masters/MasterEndpoints.cs`, `Features/Masters/SettingsEndpoints.cs`
- Modify: `server/Secucode.Api/Endpoints.cs`
- Test: `server/Secucode.Api.Tests/MasterAndSettingsTests.cs`

**Interfaces:**
- Consumes: `Caller.RequireInstitutionAsync`, `Caller.RequireStaffAsync`, `AuditWriter.Record`, `MasterEntity`.
- Produces: `MasterDto(int Id, string Name)`, `MasterRequest(string Name)`, `SettingsDto(bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension)`; routes `GET/POST /branches|/classes|/batches`, `PUT/DELETE /{path}/{id}`, `GET/PUT /settings`; audit actions `branches.create|update|delete` (and `classes.*`, `batches.*`), `settings.update`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/MasterAndSettingsTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class MasterAndSettingsTests(ApiFixture api)
{
    private readonly World _world = new(api);

    [Fact]
    public async Task Super_admin_manages_masters_and_every_change_is_audited()
    {
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        var name = World.Unique("Civil");

        var created = await (await admin.PostJsonAsync("/api/branches", new MasterRequest(name))).ReadAsync<MasterDto>(HttpStatusCode.Created);
        Assert.Equal(name, created.Name);
        Assert.Equal("branches.create", (await _world.LastAuditAsync()).Action);

        var renamed = await (await admin.PutJsonAsync($"/api/branches/{created.Id}", new MasterRequest(name + " Eng"))).ReadAsync<MasterDto>();
        Assert.Equal(name + " Eng", renamed.Name);
        var update = await _world.LastAuditAsync();
        Assert.Equal("branches.update", update.Action);
        Assert.Contains(name, update.BeforeJson);

        await (await admin.PostJsonAsync("/api/branches", new MasterRequest(name + " Eng"))).ExpectProblemAsync(HttpStatusCode.Conflict);

        await (await admin.DeleteAsync($"/api/branches/{created.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("branches.delete", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Masters_in_use_cannot_be_deleted()
    {
        var s = await _world.SectionAsync();
        await _world.UserAsync(RoleNames.Faculty, s.Department);
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));

        await (await admin.DeleteAsync($"/api/branches/{s.BranchId}")).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Everyone_can_list_masters_but_only_ManageMasters_can_change_them()
    {
        var s = await _world.SectionAsync();
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, s.Batch));
        var principal = await _world.LoginAsync(await _world.UserAsync(RoleNames.Principal, Scope.Institution));

        var list = await (await student.GetAsync("/api/branches")).ReadAsync<List<MasterDto>>();
        Assert.Contains(list, b => b.Id == s.BranchId);

        await (await principal.PostJsonAsync("/api/classes", new MasterRequest(World.Unique("X")))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Master_names_are_validated()
    {
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        Assert.Equal(["name"], await (await admin.PostJsonAsync("/api/batches", new MasterRequest("  "))).ValidationFieldsAsync());
    }

    [Fact]
    public async Task Staff_read_settings_and_only_ManageSettings_changes_them_with_an_audit_row()
    {
        var s = await _world.SectionAsync();
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, s.Batch));
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        try
        {
            await (await student.GetAsync("/api/settings")).ExpectProblemAsync(HttpStatusCode.Forbidden);
            Assert.True((await (await faculty.GetAsync("/api/settings")).ReadAsync<SettingsDto>()).AllowAttemptReset);

            var off = new SettingsDto(true, false, true, true);
            await (await hod.PutJsonAsync("/api/settings", off)).ExpectProblemAsync(HttpStatusCode.Forbidden);
            Assert.False((await (await admin.PutJsonAsync("/api/settings", off)).ReadAsync<SettingsDto>()).AllowAttemptReset);

            var audit = await _world.LastAuditAsync();
            Assert.Equal("settings.update", audit.Action);
            // MySQL normalises JSON columns (spacing, key order), so parse rather than string-match.
            Assert.True(JsonDocument.Parse(audit.BeforeJson!).RootElement.GetProperty("allowAttemptReset").GetBoolean());
            Assert.False(JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("allowAttemptReset").GetBoolean());
        }
        finally
        {
            await _world.SetSwitchesAsync(true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter MasterAndSettingsTests`
Expected: build fails — `MasterRequest` doesn't exist.

- [ ] **Step 3: Add the contracts**

`server/Secucode.Contracts/MasterContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record MasterDto(int Id, string Name);

public sealed record MasterRequest(string Name);

public sealed record SettingsDto(bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension);
```

- [ ] **Step 4: Add the endpoints**

`server/Secucode.Api/Features/Masters/MasterEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Masters;

public static class MasterEndpoints
{
    public static void MapMasters(this IEndpointRouteBuilder api)
    {
        Map<Branch>(api, "branches", "Department",
            (db, id) => db.RoleAssignments.AnyAsync(a => a.BranchId == id),
            (db, id) => db.Exams.AnyAsync(e => e.BranchId == id));
        Map<SchoolClass>(api, "classes", "Class",
            (db, id) => db.RoleAssignments.AnyAsync(a => a.ClassId == id),
            (db, id) => db.Exams.AnyAsync(e => e.ClassId == id));
        Map<Batch>(api, "batches", "Batch",
            (db, id) => db.RoleAssignments.AnyAsync(a => a.BatchId == id),
            (db, id) => db.Exams.AnyAsync(e => e.BatchId == id));
    }

    private static void Map<T>(IEndpointRouteBuilder api, string path, string entityType,
        params Func<AppDbContext, int, Task<bool>>[] usages) where T : MasterEntity, new()
    {
        var isDepartment = typeof(T) == typeof(Branch); // department changes are visible to department-scoped auditors
        var g = api.MapGroup("/" + path);

        g.MapGet("", async (AppDbContext db) =>
            await db.Set<T>().OrderBy(x => x.Name).Select(x => new MasterDto(x.Id, x.Name)).ToListAsync());

        g.MapPost("", async (MasterRequest req, AppDbContext db, Caller caller, AuditWriter audit) =>
        {
            await caller.RequireInstitutionAsync(Capability.ManageMasters);
            var name = ValidName(req);
            if (await db.Set<T>().AnyAsync(x => x.Name == name))
                throw ApiException.Conflict($"'{name}' already exists.");

            await using var tx = await db.Database.BeginTransactionAsync();
            var item = new T { Name = name };
            db.Set<T>().Add(item);
            await db.SaveChangesAsync();
            audit.Record(caller.UserId, $"{path}.create", entityType, item.Id, after: new { item.Name }, branchId: isDepartment ? item.Id : null);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Created($"/api/{path}/{item.Id}", new MasterDto(item.Id, item.Name));
        });

        g.MapPut("/{id:int}", async (int id, MasterRequest req, AppDbContext db, Caller caller, AuditWriter audit) =>
        {
            await caller.RequireInstitutionAsync(Capability.ManageMasters);
            var item = await db.Set<T>().FindAsync(id) ?? throw ApiException.NotFound(entityType);
            var name = ValidName(req);
            if (await db.Set<T>().AnyAsync(x => x.Name == name && x.Id != id))
                throw ApiException.Conflict($"'{name}' already exists.");

            var before = new { item.Name };
            item.Name = name;
            audit.Record(caller.UserId, $"{path}.update", entityType, item.Id, before, new { item.Name }, branchId: isDepartment ? item.Id : null);
            await db.SaveChangesAsync();
            return new MasterDto(item.Id, item.Name);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, Caller caller, AuditWriter audit) =>
        {
            await caller.RequireInstitutionAsync(Capability.ManageMasters);
            var item = await db.Set<T>().FindAsync(id) ?? throw ApiException.NotFound(entityType);
            foreach (var inUse in usages)
                if (await inUse(db, id))
                    throw ApiException.Conflict($"This {entityType.ToLowerInvariant()} is in use and can't be deleted.");

            db.Set<T>().Remove(item);
            audit.Record(caller.UserId, $"{path}.delete", entityType, item.Id, before: new { item.Name }, branchId: isDepartment ? item.Id : null);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static string ValidName(MasterRequest req)
    {
        var name = req.Name?.Trim() ?? "";
        new Validator().Rule(name.Length is >= 1 and <= 100, "name", "Name must be 1 to 100 characters.").ThrowIfInvalid();
        return name;
    }
}
```

`server/Secucode.Api/Features/Masters/SettingsEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Masters;

public static class SettingsEndpoints
{
    public static void MapSettings(this IEndpointRouteBuilder api)
    {
        api.MapGet("/settings", async (AppDbContext db, Caller caller) =>
        {
            await caller.RequireStaffAsync();
            return ToDto(await db.SystemSettings.AsNoTracking().SingleAsync());
        });

        api.MapPut("/settings", async (SettingsDto req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time) =>
        {
            await caller.RequireInstitutionAsync(Capability.ManageSettings);
            var s = await db.SystemSettings.SingleAsync();
            var before = ToDto(s);
            s.AllowQuestionEditAfterAttempts = req.AllowQuestionEditAfterAttempts;
            s.AllowAttemptReset = req.AllowAttemptReset;
            s.AllowAttemptReopen = req.AllowAttemptReopen;
            s.AllowTimeExtension = req.AllowTimeExtension;
            s.UpdatedAt = time.UtcNow();
            s.UpdatedBy = caller.UserId;
            audit.Record(caller.UserId, "settings.update", "SystemSettings", s.Id, before, req);
            await db.SaveChangesAsync();
            return ToDto(s);
        });
    }

    private static SettingsDto ToDto(SystemSettings s) =>
        new(s.AllowQuestionEditAfterAttempts, s.AllowAttemptReset, s.AllowAttemptReopen, s.AllowTimeExtension);
}
```

In `Endpoints.MapSecucodeEndpoints` add after `api.MapAuth();`:

```csharp
        api.MapMasters();
        api.MapSettings();
```

and add `using Secucode.Api.Features.Masters;`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter MasterAndSettingsTests`
Expected: 5 passed.

- [ ] **Step 6: Commit**

```bash
git add -A server
git commit -m "feat(api): masters and global settings endpoints with audit"
```

---

### Task 6: Users

User CRUD, password/PIN resets and deactivation with the rank/scope/self rules (spec §5.5, §9 Users). Introduces `AssignmentRules`, reused by Task 7.

**Files:**
- Create: `server/Secucode.Contracts/UserContracts.cs`
- Create: `server/Secucode.Api/Features/Users/AssignmentRules.cs`, `Features/Users/UserEndpoints.cs`
- Modify: `server/Secucode.Api/Endpoints.cs`
- Test: `server/Secucode.Api.Tests/UserTests.cs`

**Interfaces:**
- Consumes: Task 4 `Caller` permission members; `UserMapping.ToDto`.
- Produces: `CreateUserRequest(string Username, string Password, string FullName, string? Email, string? Pin, IReadOnlyList<AssignmentRequest> Assignments)`, `UpdateUserRequest(string FullName, string? Email, bool IsActive)`, `ResetPasswordRequest(string NewPassword)`, `ResetPinRequest(string NewPin)`, `AssignmentRequest(int RoleId, int? BranchId, int? ClassId, int? BatchId, DateOnly? ValidFrom, DateOnly? ValidTo)`, `UpdateAssignmentRequest(DateOnly? ValidFrom, DateOnly? ValidTo)`; `AssignmentRules.BuildAsync(AppDbContext, Caller, AssignmentRequest, IReadOnlyCollection<RoleAssignment> existing, bool userHasPin, bool forNewUser, DateTime now) → RoleAssignment`, `AssignmentRules.CheckPermissionAsync(Caller, Role, Scope, bool forNewUser)`, `AssignmentRules.Snapshot(RoleAssignment)`; `UserEndpoints.FindAsync(AppDbContext, int) → User?` (with assignments + roles); audit actions `user.create|update|deactivate|reset_password|reset_pin`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/UserTests.cs`:

```csharp
using System.Net;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class UserTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private HttpClient Anonymous => api.Factory.CreateClient();

    private async Task<CreateUserRequest> NewUser(string role, Scope scope, string? pin = null) =>
        new(World.Unique("new"), "Secret1", "New Person", "new@college.edu", pin,
            [new AssignmentRequest(await _world.RoleIdAsync(role), scope.BranchId, scope.ClassId, scope.BatchId, null, null)]);

    [Fact]
    public async Task HOD_creates_faculty_in_own_department_who_can_then_log_in()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var req = await NewUser(RoleNames.Faculty, s.Department);

        var created = await (await hod.PostJsonAsync("/api/users", req)).ReadAsync<UserDto>(HttpStatusCode.Created);

        Assert.Equal(RoleNames.Faculty, Assert.Single(created.Assignments).RoleName);
        var audit = await _world.LastAuditAsync();
        Assert.Equal("user.create", audit.Action);
        Assert.Equal(s.BranchId, audit.BranchId);
        Assert.DoesNotContain("Secret1", audit.AfterJson);
        await (await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(req.Username, "Secret1", ClientKind.Web))).ExpectAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HOD_cannot_create_staff_in_another_department()
    {
        var mine = await _world.SectionAsync();
        var other = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, mine.Department));
        await (await hod.PostJsonAsync("/api/users", await NewUser(RoleNames.Faculty, other.Department))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Class_coordinator_enrolls_students_but_cannot_create_staff()
    {
        var s = await _world.SectionAsync();
        var cc = await _world.LoginAsync(await _world.UserAsync(RoleNames.ClassCoordinator, s.Year));

        await (await cc.PostJsonAsync("/api/users", await NewUser(RoleNames.Student, s.Batch, pin: "4321"))).ExpectAsync(HttpStatusCode.Created);
        await (await cc.PostJsonAsync("/api/users", await NewUser(RoleNames.Faculty, s.Year))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Students_need_a_pin_and_cannot_also_hold_staff_roles()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));

        var noPin = await (await hod.PostJsonAsync("/api/users", await NewUser(RoleNames.Student, s.Batch))).ValidationFieldsAsync();
        Assert.Contains("pin", noPin);

        var both = await NewUser(RoleNames.Student, s.Batch, pin: "1111");
        both = both with { Assignments = [.. both.Assignments, new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), s.BranchId, null, null, null, null)] };
        await (await hod.PostJsonAsync("/api/users", both)).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_input_reports_every_bad_field_and_duplicate_usernames_conflict()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var bad = new CreateUserRequest("a b", "123", "", "not-an-email", "12", []);

        var fields = await (await hod.PostJsonAsync("/api/users", bad)).ValidationFieldsAsync();
        Assert.Equal(["assignments", "email", "fullName", "password", "pin", "username"], fields.OrderBy(f => f).ToArray());

        var existing = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var dup = (await NewUser(RoleNames.Faculty, s.Department)) with { Username = existing.Username };
        await (await hod.PostJsonAsync("/api/users", dup)).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_user_list_only_shows_people_the_caller_can_manage()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var mine = await _world.UserAsync(RoleNames.Faculty, a.Department);
        var notMine = await _world.UserAsync(RoleNames.Faculty, b.Department);
        var senior = await _world.UserAsync(RoleNames.Principal, Scope.Institution);

        var page = await (await hod.GetAsync($"/api/users?branchId={a.BranchId}")).ReadAsync<PageDto<UserDto>>();
        Assert.Contains(page.Items, u => u.Id == mine.Id);

        var all = await (await hod.GetAsync("/api/users?search=u")).ReadAsync<PageDto<UserDto>>();
        Assert.DoesNotContain(all.Items, u => u.Id == notMine.Id || u.Id == senior.Id);

        var faculty = await _world.LoginAsync(mine);
        await (await faculty.GetAsync("/api/users")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deactivating_a_user_blocks_login_and_kills_existing_sessions()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var target = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var session = await _world.LoginResponseAsync(target);

        await (await hod.DeleteAsync($"/api/users/{target.Id}")).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal("user.deactivate", (await _world.LastAuditAsync()).Action);
        await (await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(target.Username, World.Password, ClientKind.Web))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Password_reset_revokes_sessions_and_the_new_password_works()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var target = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var session = await _world.LoginResponseAsync(target);

        await (await hod.PostJsonAsync($"/api/users/{target.Id}/reset-password", new ResetPasswordRequest("NewPass9"))).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal("user.reset_password", (await _world.LastAuditAsync()).Action);
        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        await _world.LoginResponseAsync(target, password: "NewPass9");
    }

    [Fact]
    public async Task Pin_reset_validates_digits()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);

        Assert.Equal(["newPin"], await (await hod.PostJsonAsync($"/api/users/{student.Id}/reset-pin", new ResetPinRequest("12ab"))).ValidationFieldsAsync());
        await (await hod.PostJsonAsync($"/api/users/{student.Id}/reset-pin", new ResetPinRequest("987654"))).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("user.reset_pin", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Nobody_manages_themselves_or_anyone_senior()
    {
        var s = await _world.SectionAsync();
        var hodUser = await _world.UserAsync(RoleNames.Hod, s.Department);
        var hod = await _world.LoginAsync(hodUser);
        var principal = await _world.UserAsync(RoleNames.Principal, Scope.Institution);

        await (await hod.PutJsonAsync($"/api/users/{hodUser.Id}", new UpdateUserRequest("Me", null, true))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.DeleteAsync($"/api/users/{principal.Id}")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_super_admins_can_deactivate_super_admins()
    {
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        var principal = await _world.LoginAsync(await _world.UserAsync(RoleNames.Principal, Scope.Institution));
        var target = await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution);

        await (await principal.DeleteAsync($"/api/users/{target.Id}")).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await admin.DeleteAsync($"/api/users/{target.Id}")).ExpectAsync(HttpStatusCode.NoContent);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter UserTests`
Expected: build fails — `CreateUserRequest` doesn't exist.

- [ ] **Step 3: Add the contracts**

`server/Secucode.Contracts/UserContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record AssignmentRequest(int RoleId, int? BranchId, int? ClassId, int? BatchId, DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record UpdateAssignmentRequest(DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record CreateUserRequest(string Username, string Password, string FullName, string? Email, string? Pin,
    IReadOnlyList<AssignmentRequest> Assignments);

public sealed record UpdateUserRequest(string FullName, string? Email, bool IsActive);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record ResetPinRequest(string NewPin);
```

- [ ] **Step 4: Add the assignment rules**

`server/Secucode.Api/Features/Users/AssignmentRules.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Users;

// Shared by user creation (Task 6) and the assignment endpoints (Task 7). Spec §5.5.
public static class AssignmentRules
{
    public static async Task<RoleAssignment> BuildAsync(AppDbContext db, Caller caller, AssignmentRequest req,
        IReadOnlyCollection<RoleAssignment> existing, bool userHasPin, bool forNewUser, DateTime now)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == req.RoleId)
            ?? throw ApiException.Validation("roleId", "That role doesn't exist.");
        var scope = new Scope(req.BranchId, req.ClassId, req.BatchId);

        new Validator()
            .Rule(scope.IsValidShape, "scope", "A class needs a department, and a batch needs a class.")
            .Rule(role.AllowedScopeLevels.HasFlag(scope.Level), "scope", $"{role.Name} can't be assigned at {scope.Level} level.")
            .Rule(req.ValidFrom is null || req.ValidTo is null || req.ValidTo >= req.ValidFrom, "validTo", "End date must be on or after the start date.")
            .Rule(!role.IsSystem || req.ValidTo is null, "validTo", "Super Admin assignments can't have an end date.")
            .ThrowIfInvalid();
        await EnsureScopeExistsAsync(db, scope);
        await CheckPermissionAsync(caller, role, scope, forNewUser);

        if (role.IsStudent ? existing.Count > 0 : existing.Any(a => a.Role.IsStudent))
            throw ApiException.Conflict(role.IsStudent ? "A student can't hold any other role." : "A student can't also hold staff roles.");
        if (role.IsStudent && !userHasPin)
            throw ApiException.Validation("pin", "Students need a PIN.");

        return new RoleAssignment
        {
            Role = role, RoleId = role.Id, BranchId = scope.BranchId, ClassId = scope.ClassId, BatchId = scope.BatchId,
            ValidFrom = req.ValidFrom, ValidTo = req.ValidTo, CreatedBy = caller.UserId, CreatedAt = now,
        };
    }

    // Student assignments need ManageUsers; every other role needs AssignRoles; creating a user always needs ManageUsers.
    public static async Task CheckPermissionAsync(Caller caller, Role role, Scope scope, bool forNewUser)
    {
        if (!await caller.OutranksAsync(role))
            throw ApiException.Forbidden("You can only assign roles below your own.");
        if ((forNewUser || role.IsStudent) && !await caller.CanAsync(Capability.ManageUsers, scope))
            throw ApiException.Forbidden("You can't manage users in that department, class or batch.");
        if (!role.IsStudent && !await caller.CanAsync(Capability.AssignRoles, scope))
            throw ApiException.Forbidden("You can't assign roles in that department, class or batch.");
    }

    public static object Snapshot(RoleAssignment a) =>
        new { a.UserId, Role = a.Role.Name, a.BranchId, a.ClassId, a.BatchId, a.ValidFrom, a.ValidTo };

    private static async Task EnsureScopeExistsAsync(AppDbContext db, Scope s)
    {
        var v = new Validator();
        if (s.BranchId is int b) v.Rule(await db.Branches.AnyAsync(x => x.Id == b), "branchId", "That department doesn't exist.");
        if (s.ClassId is int c) v.Rule(await db.Classes.AnyAsync(x => x.Id == c), "classId", "That class doesn't exist.");
        if (s.BatchId is int t) v.Rule(await db.Batches.AnyAsync(x => x.Id == t), "batchId", "That batch doesn't exist.");
        v.ThrowIfInvalid();
    }
}
```

- [ ] **Step 5: Add the user endpoints**

`server/Secucode.Api/Features/Users/UserEndpoints.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Users;

public static partial class UserEndpoints
{
    private const int PageSize = 50;

    public static void MapUsers(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/users");
        g.MapGet("", ListAsync);
        g.MapPost("", CreateAsync);
        g.MapGet("/{id:int}", GetAsync);
        g.MapPut("/{id:int}", UpdateAsync);
        g.MapDelete("/{id:int}", DeactivateAsync);
        g.MapPost("/{id:int}/reset-password", ResetPasswordAsync);
        g.MapPost("/{id:int}/reset-pin", ResetPinAsync);
    }

    public static Task<User?> FindAsync(AppDbContext db, int id) =>
        db.Users.Include(u => u.Assignments).ThenInclude(a => a.Role).SingleOrDefaultAsync(u => u.Id == id);

    private static async Task<PageDto<UserDto>> ListAsync(AppDbContext db, Caller caller, int? roleId, int? branchId, string? search, int? page)
    {
        if (!await caller.HasAnywhereAsync(Capability.ManageUsers) && !await caller.HasAnywhereAsync(Capability.AssignRoles))
            throw ApiException.Forbidden("You can't view users.");

        var q = db.Users.Include(u => u.Assignments).ThenInclude(a => a.Role).AsNoTracking();
        if (roleId is not null) q = q.Where(u => u.Assignments.Any(a => a.RoleId == roleId));
        if (branchId is not null) q = q.Where(u => u.Assignments.Any(a => a.BranchId == branchId));
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(u => u.Username.Contains(search) || u.FullName.Contains(search));

        // ponytail: visibility is filtered in memory; fine for one college, push into SQL if users reach tens of thousands
        var visible = new List<User>();
        foreach (var u in await q.OrderBy(u => u.Username).ToListAsync())
            if (await caller.CanManageUserAsync(Capability.ManageUsers, u) || await caller.CanManageUserAsync(Capability.AssignRoles, u))
                visible.Add(u);

        var p = Math.Max(page ?? 1, 1);
        var items = visible.Skip((p - 1) * PageSize).Take(PageSize).Select(u => u.ToDto(caller.Today)).ToList();
        return new PageDto<UserDto>(items, visible.Count, p, PageSize);
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var username = req.Username?.Trim() ?? "";
        new Validator()
            .Rule(UsernamePattern().IsMatch(username), "username", "Username must be 3-100 letters, digits, dots, dashes or underscores.")
            .Rule((req.Password?.Length ?? 0) >= 6, "password", "Password must be at least 6 characters.")
            .Rule(!string.IsNullOrWhiteSpace(req.FullName) && req.FullName.Trim().Length <= 200, "fullName", "Full name is required (up to 200 characters).")
            .Rule(ValidEmail(req.Email), "email", "That email address isn't valid.")
            .Rule(req.Pin is null || PinPattern().IsMatch(req.Pin), "pin", "PIN must be 4 to 6 digits.")
            .Rule(req.Assignments is { Count: > 0 }, "assignments", "Give the user at least one role.")
            .ThrowIfInvalid();
        if (await db.Users.AnyAsync(u => u.Username == username))
            throw ApiException.Conflict($"The username '{username}' is taken.");

        var now = time.UtcNow();
        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            PinHash = req.Pin is null ? null : BCrypt.Net.BCrypt.HashPassword(req.Pin),
            FullName = req.FullName!.Trim(),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            CreatedAt = now,
        };
        foreach (var a in req.Assignments!)
            user.Assignments.Add(await AssignmentRules.BuildAsync(db, caller, a, user.Assignments, user.PinHash is not null, forNewUser: true, now));

        await using var tx = await db.Database.BeginTransactionAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        audit.Record(caller.UserId, "user.create", "User", user.Id, after: Snapshot(user), branchId: user.Assignments[0].BranchId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/users/{user.Id}", user.ToDto(caller.Today));
    }

    private static async Task<UserDto> GetAsync(int id, AppDbContext db, Caller caller)
    {
        var user = await FindAsync(db, id) ?? throw ApiException.NotFound("User");
        if (!await caller.CanManageUserAsync(Capability.ManageUsers, user) && !await caller.CanManageUserAsync(Capability.AssignRoles, user))
            throw ApiException.NotFound("User");
        return user.ToDto(caller.Today);
    }

    private static async Task<UserDto> UpdateAsync(int id, UpdateUserRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var user = await ManageableAsync(db, caller, id);
        new Validator()
            .Rule(!string.IsNullOrWhiteSpace(req.FullName) && req.FullName.Trim().Length <= 200, "fullName", "Full name is required (up to 200 characters).")
            .Rule(ValidEmail(req.Email), "email", "That email address isn't valid.")
            .ThrowIfInvalid();

        var before = Snapshot(user);
        user.FullName = req.FullName.Trim();
        user.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        if (user.IsActive && !req.IsActive)
            await RevokeSessionsAsync(db, user.Id, time);
        user.IsActive = req.IsActive;
        audit.Record(caller.UserId, "user.update", "User", user.Id, before, Snapshot(user), branchId: user.Assignments.FirstOrDefault()?.BranchId);
        await db.SaveChangesAsync();
        return user.ToDto(caller.Today);
    }

    private static async Task<IResult> DeactivateAsync(int id, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var user = await ManageableAsync(db, caller, id);
        var before = Snapshot(user);
        user.IsActive = false;
        await RevokeSessionsAsync(db, user.Id, time);
        audit.Record(caller.UserId, "user.deactivate", "User", user.Id, before, Snapshot(user), branchId: user.Assignments.FirstOrDefault()?.BranchId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(int id, ResetPasswordRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var user = await ManageableAsync(db, caller, id);
        new Validator().Rule((req.NewPassword?.Length ?? 0) >= 6, "newPassword", "Password must be at least 6 characters.").ThrowIfInvalid();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await RevokeSessionsAsync(db, user.Id, time);
        audit.Record(caller.UserId, "user.reset_password", "User", user.Id, branchId: user.Assignments.FirstOrDefault()?.BranchId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPinAsync(int id, ResetPinRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var user = await ManageableAsync(db, caller, id);
        new Validator().Rule(req.NewPin is not null && PinPattern().IsMatch(req.NewPin), "newPin", "PIN must be 4 to 6 digits.").ThrowIfInvalid();
        user.PinHash = BCrypt.Net.BCrypt.HashPassword(req.NewPin);
        audit.Record(caller.UserId, "user.reset_pin", "User", user.Id, branchId: user.Assignments.FirstOrDefault()?.BranchId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<User> ManageableAsync(AppDbContext db, Caller caller, int id)
    {
        var user = await FindAsync(db, id) ?? throw ApiException.NotFound("User");
        if (!await caller.CanManageUserAsync(Capability.ManageUsers, user))
            throw ApiException.Forbidden("You can't manage this user.");
        return user;
    }

    private static Task RevokeSessionsAsync(AppDbContext db, int userId, TimeProvider time)
    {
        var now = time.UtcNow();
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
    }

    private static object Snapshot(User u) => new
    {
        u.Username, u.FullName, u.Email, u.IsActive,
        Roles = u.Assignments.Select(a => new { Role = a.Role.Name, a.BranchId, a.ClassId, a.BatchId, a.ValidFrom, a.ValidTo }).ToList(),
    };

    private static bool ValidEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) || (email.Contains('@') && email.Trim().Length <= 200);

    [GeneratedRegex("^[A-Za-z0-9._-]{3,100}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^\d{4,6}$")]
    private static partial Regex PinPattern();
}
```

In `Endpoints.MapSecucodeEndpoints` add `api.MapUsers();` and `using Secucode.Api.Features.Users;`.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter UserTests`
Expected: 11 passed.

- [ ] **Step 7: Commit**

```bash
git add -A server
git commit -m "feat(api): user management with rank, scope and self rules"
```

---

### Task 7: Roles, capability grid, role assignments

Configurable roles and grants plus assignment endpoints (spec §5.5, §9 Roles and assignments).

**Files:**
- Create: `server/Secucode.Contracts/RoleContracts.cs`
- Create: `server/Secucode.Api/Features/Roles/RoleEndpoints.cs`, `Features/Roles/AssignmentEndpoints.cs`
- Modify: `server/Secucode.Api/Endpoints.cs`
- Test: `server/Secucode.Api.Tests/RoleTests.cs`

**Interfaces:**
- Consumes: `AssignmentRules.BuildAsync/CheckPermissionAsync/Snapshot`, `UserEndpoints.FindAsync`, `Caller` members.
- Produces: `RoleDto(int Id, string Name, string? Description, int Rank, bool IsSystem, bool IsStudent, ScopeLevels AllowedScopeLevels, IReadOnlyList<GrantDto> Grants)`, `GrantDto(Capability Capability, Reach Reach)`, `RoleRequest(string Name, string? Description, int Rank, ScopeLevels AllowedScopeLevels)`; routes `GET /capabilities`, `GET/POST /roles`, `PUT/DELETE /roles/{id}`, `PUT /roles/{id}/capabilities`, `GET/POST /users/{id}/assignments`, `PUT/DELETE /assignments/{id}`; audit actions `role.create|update|delete|grants`, `assignment.create|update|delete`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/RoleTests.cs`:

```csharp
using System.Net;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class RoleTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<HttpClient> SuperAdmin() => await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));

    [Fact]
    public async Task Staff_can_read_roles_and_capabilities_but_students_cannot()
    {
        var s = await _world.SectionAsync();
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, s.Batch));

        var roles = await (await faculty.GetAsync("/api/roles")).ReadAsync<List<RoleDto>>();
        Assert.Contains(roles, r => r.Name == RoleNames.Hod && r.Rank == 700);
        var caps = await (await faculty.GetAsync("/api/capabilities")).ReadAsync<List<Capability>>();
        Assert.Equal(Enum.GetValues<Capability>().Length, caps.Count);
        await (await student.GetAsync("/api/roles")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Super_admin_creates_a_custom_role_and_edits_its_grid()
    {
        var admin = await SuperAdmin();
        var role = await (await admin.PostJsonAsync("/api/roles", new RoleRequest(World.Unique("Lab In-charge"), "Runs labs", 350, ScopeLevels.Department)))
            .ReadAsync<RoleDto>(HttpStatusCode.Created);

        var updated = await (await admin.PutJsonAsync($"/api/roles/{role.Id}/capabilities",
                new List<GrantDto> { new(Capability.ViewResults, Reach.InScope), new(Capability.ResetAttempts, Reach.OwnOnly) }))
            .ReadAsync<RoleDto>();

        Assert.Equal(2, updated.Grants.Count);
        var audit = await _world.LastAuditAsync();
        Assert.Equal("role.grants", audit.Action);
        Assert.Contains("ResetAttempts", audit.AfterJson);
    }

    [Fact]
    public async Task You_cannot_grant_what_you_do_not_hold_or_touch_roles_at_your_rank()
    {
        var roleAdminRole = await _world.CustomRoleAsync(World.Unique("Role Admin"), 500, ScopeLevels.Institution, (Capability.ManageRoles, Reach.InScope));
        var roleAdmin = await _world.BareUserAsync();
        await _world.WithDb(db => { db.RoleAssignments.Add(new() { UserId = roleAdmin.Id, RoleId = roleAdminRole, CreatedAt = _world.Now }); return db.SaveChangesAsync(); });
        var client = await _world.LoginAsync(roleAdmin);

        var low = await (await client.PostJsonAsync("/api/roles", new RoleRequest(World.Unique("Helper"), null, 100, ScopeLevels.Department))).ReadAsync<RoleDto>(HttpStatusCode.Created);
        await (await client.PutJsonAsync($"/api/roles/{low.Id}/capabilities", new List<GrantDto> { new(Capability.ViewAudit, Reach.InScope) }))
            .ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await client.PutJsonAsync($"/api/roles/{low.Id}/capabilities", new List<GrantDto> { new(Capability.ManageRoles, Reach.InScope) }))
            .ExpectAsync(HttpStatusCode.OK);

        await (await client.PostJsonAsync("/api/roles", new RoleRequest(World.Unique("Too High"), null, 500, ScopeLevels.Department))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        var hodId = await _world.RoleIdAsync(RoleNames.Hod);
        await (await client.PutJsonAsync($"/api/roles/{hodId}", new RoleRequest(RoleNames.Hod, null, 700, ScopeLevels.Department))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Super_admin_and_student_roles_are_protected()
    {
        var admin = await SuperAdmin();
        var superId = await _world.RoleIdAsync(RoleNames.SuperAdmin);
        var studentId = await _world.RoleIdAsync(RoleNames.Student);

        await (await admin.PutJsonAsync($"/api/roles/{superId}", new RoleRequest("Boss", null, 999, ScopeLevels.Institution))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await admin.DeleteAsync($"/api/roles/{superId}")).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await admin.PutJsonAsync($"/api/roles/{studentId}/capabilities", new List<GrantDto> { new(Capability.ViewResults, Reach.InScope) })).ValidationFieldsAsync();
        await (await admin.DeleteAsync($"/api/roles/{studentId}")).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Roles_in_use_cannot_be_deleted()
    {
        var admin = await SuperAdmin();
        var s = await _world.SectionAsync();
        var name = World.Unique("Temp Role");
        var roleId = await _world.CustomRoleAsync(name, 200, ScopeLevels.Department);
        var holder = await _world.UserAsync(name, s.Department);

        await (await admin.DeleteAsync($"/api/roles/{roleId}")).ExpectProblemAsync(HttpStatusCode.Conflict);
        await _world.WithDb(db => db.RoleAssignments.Where(a => a.UserId == holder.Id).ExecuteDeleteAsync());
        await (await admin.DeleteAsync($"/api/roles/{roleId}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("role.delete", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task HOD_assigns_lower_roles_inside_the_department_only()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var target = await _world.UserAsync(RoleNames.Faculty, a.Department);

        var sub = await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.SubHod), a.BranchId, null, null, null, null))).ReadAsync<AssignmentDto>(HttpStatusCode.Created);
        Assert.Equal(RoleNames.SubHod, sub.RoleName);
        Assert.Equal("assignment.create", (await _world.LastAuditAsync()).Action);

        await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Hod), a.BranchId, null, null, null, null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), b.BranchId, null, null, null, null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.SubHod), a.BranchId, a.ClassId, null, null, null))).ValidationFieldsAsync(); // wrong level
    }

    [Fact]
    public async Task Coordinators_enroll_students_and_students_cannot_hold_staff_roles()
    {
        var s = await _world.SectionAsync();
        var cc = await _world.LoginAsync(await _world.UserAsync(RoleNames.ClassCoordinator, s.Year));
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var newcomer = await _world.BareUserAsync(withPin: true);
        var studentRole = await _world.RoleIdAsync(RoleNames.Student);

        await (await cc.PostJsonAsync($"/api/users/{newcomer.Id}/assignments",
            new AssignmentRequest(studentRole, s.BranchId, s.ClassId, s.BatchId, null, null))).ExpectAsync(HttpStatusCode.Created);
        await (await hod.PostJsonAsync($"/api/users/{newcomer.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), s.BranchId, null, null, null, null))).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Nobody_changes_their_own_roles()
    {
        var s = await _world.SectionAsync();
        var hodUser = await _world.UserAsync(RoleNames.Hod, s.Department);
        var hod = await _world.LoginAsync(hodUser);
        var own = await _world.WithDb(db => db.RoleAssignments.SingleAsync(a => a.UserId == hodUser.Id));

        await (await hod.PostJsonAsync($"/api/users/{hodUser.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), s.BranchId, null, null, null, null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.DeleteAsync($"/api/assignments/{own.Id}")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Time_limited_assignments_stop_granting_after_their_end_date()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var target = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var created = await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.SubHod), s.BranchId, null, null, null, _world.Today.AddDays(1))))
            .ReadAsync<AssignmentDto>(HttpStatusCode.Created);
        Assert.True(created.IsActive);

        api.Time.Advance(TimeSpan.FromDays(2));
        var me = await (await (await _world.LoginAsync(target)).GetAsync("/api/auth/me")).ReadAsync<MeResponse>();
        Assert.DoesNotContain(me.Capabilities, c => c.RoleName == RoleNames.SubHod);
    }

    [Fact]
    public async Task Super_admin_assignments_never_end_and_updates_and_removals_are_audited()
    {
        var admin = await SuperAdmin();
        var newcomer = await _world.BareUserAsync();
        var superId = await _world.RoleIdAsync(RoleNames.SuperAdmin);

        await (await admin.PostJsonAsync($"/api/users/{newcomer.Id}/assignments",
            new AssignmentRequest(superId, null, null, null, null, _world.Today.AddDays(30)))).ValidationFieldsAsync();
        var a = await (await admin.PostJsonAsync($"/api/users/{newcomer.Id}/assignments",
            new AssignmentRequest(superId, null, null, null, null, null))).ReadAsync<AssignmentDto>(HttpStatusCode.Created);

        await (await admin.PutJsonAsync($"/api/assignments/{a.Id}", new UpdateAssignmentRequest(null, _world.Today.AddDays(5)))).ValidationFieldsAsync();
        await (await admin.PutJsonAsync($"/api/assignments/{a.Id}", new UpdateAssignmentRequest(_world.Today, null))).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("assignment.update", (await _world.LastAuditAsync()).Action);
        await (await admin.DeleteAsync($"/api/assignments/{a.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("assignment.delete", (await _world.LastAuditAsync()).Action);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter RoleTests`
Expected: build fails — `RoleRequest` doesn't exist.

- [ ] **Step 3: Add the contracts**

`server/Secucode.Contracts/RoleContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record GrantDto(Capability Capability, Reach Reach);

public sealed record RoleDto(int Id, string Name, string? Description, int Rank, bool IsSystem, bool IsStudent,
    ScopeLevels AllowedScopeLevels, IReadOnlyList<GrantDto> Grants);

public sealed record RoleRequest(string Name, string? Description, int Rank, ScopeLevels AllowedScopeLevels);
```

- [ ] **Step 4: Add the role endpoints**

`server/Secucode.Api/Features/Roles/RoleEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Roles;

public static class RoleEndpoints
{
    private const ScopeLevels AllLevels = ScopeLevels.Institution | ScopeLevels.Department | ScopeLevels.Year | ScopeLevels.Batch;

    public static void MapRoles(this IEndpointRouteBuilder api)
    {
        api.MapGet("/capabilities", async (Caller caller) =>
        {
            await caller.RequireStaffAsync();
            return Enum.GetValues<Capability>();
        });

        var g = api.MapGroup("/roles");
        g.MapGet("", async (AppDbContext db, Caller caller) =>
        {
            await caller.RequireStaffAsync();
            var roles = await db.Roles.Include(r => r.Capabilities).AsNoTracking().OrderByDescending(r => r.Rank).ThenBy(r => r.Name).ToListAsync();
            return roles.Select(ToDto).ToList();
        });
        g.MapPost("", CreateAsync);
        g.MapPut("/{id:int}", UpdateAsync);
        g.MapDelete("/{id:int}", DeleteAsync);
        g.MapPut("/{id:int}/capabilities", SetGrantsAsync);
    }

    public static RoleDto ToDto(Role r) => new(r.Id, r.Name, r.Description, r.Rank, r.IsSystem, r.IsStudent, r.AllowedScopeLevels,
        r.Capabilities.OrderBy(c => c.Capability).Select(c => new GrantDto(c.Capability, c.Reach)).ToList());

    private static async Task<IResult> CreateAsync(RoleRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        await caller.RequireInstitutionAsync(Capability.ManageRoles);
        var name = Validate(req, isStudent: false);
        await EnsureBelowCallerAsync(caller, req.Rank);
        if (await db.Roles.AnyAsync(r => r.Name == name))
            throw ApiException.Conflict($"A role named '{name}' already exists.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var role = new Role { Name = name, Description = req.Description?.Trim(), Rank = req.Rank, AllowedScopeLevels = req.AllowedScopeLevels, CreatedAt = time.UtcNow() };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        audit.Record(caller.UserId, "role.create", "Role", role.Id, after: ToDto(role));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/roles/{role.Id}", ToDto(role));
    }

    private static async Task<RoleDto> UpdateAsync(int id, RoleRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var role = await EditableAsync(db, caller, id);
        var name = Validate(req, role.IsStudent);
        await EnsureBelowCallerAsync(caller, req.Rank);
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != id))
            throw ApiException.Conflict($"A role named '{name}' already exists.");

        var before = ToDto(role);
        role.Name = name;
        role.Description = req.Description?.Trim();
        role.Rank = req.Rank;
        role.AllowedScopeLevels = req.AllowedScopeLevels;
        audit.Record(caller.UserId, "role.update", "Role", role.Id, before, ToDto(role));
        await db.SaveChangesAsync();
        return ToDto(role);
    }

    private static async Task<IResult> DeleteAsync(int id, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var role = await EditableAsync(db, caller, id);
        if (role.IsStudent)
            throw ApiException.Conflict("The Student role can't be deleted.");
        if (await db.RoleAssignments.AnyAsync(a => a.RoleId == id))
            throw ApiException.Conflict("This role is still assigned to people. Remove those assignments first.");

        var before = ToDto(role);
        db.Roles.Remove(role);
        audit.Record(caller.UserId, "role.delete", "Role", id, before);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<RoleDto> SetGrantsAsync(int id, List<GrantDto> grants, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var role = await EditableAsync(db, caller, id);
        new Validator()
            .Rule(!role.IsStudent || grants.Count == 0, "grants", "Students can't be given capabilities.")
            .Rule(grants.Select(g => g.Capability).Distinct().Count() == grants.Count, "grants", "Each capability can appear only once.")
            .Rule(grants.All(g => Enum.IsDefined(g.Capability) && Enum.IsDefined(g.Reach)), "grants", "Unknown capability or reach.")
            .ThrowIfInvalid();

        // Adding a capability, or widening OwnOnly to InScope, requires holding it for the whole institution.
        foreach (var g in grants)
        {
            var old = role.Capabilities.FirstOrDefault(c => c.Capability == g.Capability);
            var widening = old is null || (old.Reach == Reach.OwnOnly && g.Reach == Reach.InScope);
            if (widening && !await caller.CanAsync(g.Capability, Scope.Institution))
                throw ApiException.Forbidden($"You can't grant {g.Capability} because you don't hold it for the whole institution.");
        }

        var before = ToDto(role).Grants;
        role.Capabilities.RemoveAll(c => grants.All(g => g.Capability != c.Capability));
        foreach (var g in grants)
        {
            var existing = role.Capabilities.FirstOrDefault(c => c.Capability == g.Capability);
            if (existing is null)
                role.Capabilities.Add(new RoleCapability { RoleId = role.Id, Capability = g.Capability, Reach = g.Reach });
            else
                existing.Reach = g.Reach;
        }
        audit.Record(caller.UserId, "role.grants", "Role", role.Id, before, ToDto(role).Grants);
        await db.SaveChangesAsync();
        return ToDto(role);
    }

    private static async Task<Role> EditableAsync(AppDbContext db, Caller caller, int id)
    {
        await caller.RequireInstitutionAsync(Capability.ManageRoles);
        var role = await db.Roles.Include(r => r.Capabilities).SingleOrDefaultAsync(r => r.Id == id) ?? throw ApiException.NotFound("Role");
        if (role.IsSystem)
            throw ApiException.Forbidden("The Super Admin role can't be changed.");
        await EnsureBelowCallerAsync(caller, role.Rank);
        return role;
    }

    private static async Task EnsureBelowCallerAsync(Caller caller, int rank)
    {
        if (rank >= await caller.HighestRankAsync())
            throw ApiException.Forbidden("You can only manage roles ranked below your own.");
    }

    private static string Validate(RoleRequest req, bool isStudent)
    {
        var name = req.Name?.Trim() ?? "";
        var v = new Validator()
            .Rule(name.Length is >= 2 and <= 100, "name", "Name must be 2 to 100 characters.")
            .Rule((req.Description?.Length ?? 0) <= 500, "description", "Description can be up to 500 characters.");
        if (isStudent)
            v.Rule(req.Rank == 0, "rank", "The Student role's rank is fixed at 0.")
             .Rule(req.AllowedScopeLevels == ScopeLevels.Batch, "allowedScopeLevels", "Students are always assigned to a batch.");
        else
            v.Rule(req.Rank is >= 1 and <= 999, "rank", "Rank must be between 1 and 999.")
             .Rule(req.AllowedScopeLevels != ScopeLevels.None && (req.AllowedScopeLevels & ~AllLevels) == 0, "allowedScopeLevels", "Pick at least one valid scope level.");
        v.ThrowIfInvalid();
        return name;
    }
}
```

`server/Secucode.Api/Features/Roles/AssignmentEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Users;
using Secucode.Contracts;

namespace Secucode.Api.Features.Roles;

public static class AssignmentEndpoints
{
    public static void MapAssignments(this IEndpointRouteBuilder api)
    {
        api.MapGet("/users/{id:int}/assignments", ListAsync);
        api.MapPost("/users/{id:int}/assignments", AddAsync);
        api.MapPut("/assignments/{id:int}", UpdateAsync);
        api.MapDelete("/assignments/{id:int}", DeleteAsync);
    }

    private static async Task<List<AssignmentDto>> ListAsync(int id, AppDbContext db, Caller caller)
    {
        var user = await UserEndpoints.FindAsync(db, id) ?? throw ApiException.NotFound("User");
        if (!await caller.CanManageUserAsync(Capability.ManageUsers, user) && !await caller.CanManageUserAsync(Capability.AssignRoles, user))
            throw ApiException.NotFound("User");
        return user.Assignments.Select(a => a.ToDto(caller.Today)).ToList();
    }

    private static async Task<IResult> AddAsync(int id, AssignmentRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var user = await UserEndpoints.FindAsync(db, id) ?? throw ApiException.NotFound("User");
        if (user.Id == caller.UserId)
            throw ApiException.Forbidden("You can't change your own roles.");
        if (!await caller.OutranksUserAsync(user))
            throw ApiException.Forbidden("You can't change roles for someone at or above your rank.");

        var assignment = await AssignmentRules.BuildAsync(db, caller, req, user.Assignments, user.PinHash is not null, forNewUser: false, time.UtcNow());

        await using var tx = await db.Database.BeginTransactionAsync();
        user.Assignments.Add(assignment);
        await db.SaveChangesAsync();
        audit.Record(caller.UserId, "assignment.create", "RoleAssignment", assignment.Id, after: AssignmentRules.Snapshot(assignment), branchId: assignment.BranchId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/assignments/{assignment.Id}", assignment.ToDto(caller.Today));
    }

    private static async Task<AssignmentDto> UpdateAsync(int id, UpdateAssignmentRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var a = await EditableAsync(db, caller, id);
        new Validator()
            .Rule(req.ValidFrom is null || req.ValidTo is null || req.ValidTo >= req.ValidFrom, "validTo", "End date must be on or after the start date.")
            .Rule(!a.Role.IsSystem || req.ValidTo is null, "validTo", "Super Admin assignments can't have an end date.")
            .ThrowIfInvalid();

        var before = AssignmentRules.Snapshot(a);
        a.ValidFrom = req.ValidFrom;
        a.ValidTo = req.ValidTo;
        audit.Record(caller.UserId, "assignment.update", "RoleAssignment", a.Id, before, AssignmentRules.Snapshot(a), branchId: a.BranchId);
        await db.SaveChangesAsync();
        return a.ToDto(caller.Today);
    }

    private static async Task<IResult> DeleteAsync(int id, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var a = await EditableAsync(db, caller, id);
        var before = AssignmentRules.Snapshot(a);
        db.RoleAssignments.Remove(a);
        audit.Record(caller.UserId, "assignment.delete", "RoleAssignment", a.Id, before, branchId: a.BranchId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<RoleAssignment> EditableAsync(AppDbContext db, Caller caller, int id)
    {
        var a = await db.RoleAssignments.Include(x => x.Role)
            .Include(x => x.User).ThenInclude(u => u.Assignments).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Assignment");
        if (a.UserId == caller.UserId)
            throw ApiException.Forbidden("You can't change your own roles.");
        if (!await caller.OutranksUserAsync(a.User))
            throw ApiException.Forbidden("You can't change roles for someone at or above your rank.");
        await AssignmentRules.CheckPermissionAsync(caller, a.Role, a.Scope, forNewUser: false);
        return a;
    }
}
```

In `Endpoints.MapSecucodeEndpoints` add `api.MapRoles();` and `api.MapAssignments();` with `using Secucode.Api.Features.Roles;`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter RoleTests`
Expected: 10 passed.

- [ ] **Step 6: Commit**

```bash
git add -A server
git commit -m "feat(api): configurable roles, capability grid and scoped role assignments"
```

---

### Task 8: Exams and questions

Exam CRUD with per-exam flags, the edit-after-attempts rule with question snapshots, global/per-exam switches, and role-aware listing (spec §6, §9 Exams).

**Files:**
- Create: `server/Secucode.Contracts/ExamContracts.cs`
- Create: `server/Secucode.Api/Features/Exams/Switches.cs`, `Features/Exams/ExamMapping.cs`, `Features/Exams/ExamEndpoints.cs`
- Modify: `server/Secucode.Api/Endpoints.cs`
- Test: `server/Secucode.Api.Tests/ExamTests.cs`

**Interfaces:**
- Consumes: `Caller`, `AuditWriter`, `Validator`.
- Produces: `QuestionInput(int? Id, string Text)`, `QuestionDto(int Id, string Text, int SortOrder)`, `ExamRequest(string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, bool IsActive, int BranchId, int? ClassId, int? BatchId, bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension, IReadOnlyList<QuestionInput> Questions)`, `ExamDto(int Id, string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, bool IsActive, int BranchId, int? ClassId, int? BatchId, int CreatedBy, string CreatedByName, bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension, IReadOnlyList<QuestionDto>? Questions)`, `StudentExamDto(int Id, string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, int? InProgressAttemptId)`, `SetActiveRequest(bool IsActive)`; `Switch` enum `{ QuestionEdit, Reset, Reopen, Extension }` and `Switches.EnsureOnAsync(AppDbContext, Exam, Switch)`; `ExamMapping.ToDtoAsync(AppDbContext, Exam, bool includeQuestions)`; audit actions `exam.create|update|set_active|delete`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/ExamTests.cs`:

```csharp
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ExamTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private ExamRequest Request(Scope target, params string[] questions) => new(
        World.Unique("Midterm"), _world.Now.AddHours(1), _world.Now.AddHours(3), 60, ExamMode.DesktopOnly, true,
        target.BranchId!.Value, target.ClassId, target.BatchId, true, true, true, true,
        (questions.Length == 0 ? ["Reverse a string", "Sum of digits"] : questions).Select(q => new QuestionInput(null, q)).ToList());

    [Fact]
    public async Task Faculty_create_exams_inside_their_scope_only_and_creation_is_audited()
    {
        var mine = await _world.SectionAsync();
        var other = await _world.SectionAsync();
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, mine.Department));

        var exam = await (await faculty.PostJsonAsync("/api/exams", Request(mine.Batch))).ReadAsync<ExamDto>(HttpStatusCode.Created);
        Assert.Equal(2, exam.Questions!.Count);
        var audit = await _world.LastAuditAsync();
        Assert.Equal(("exam.create", exam.Id, mine.BranchId), (audit.Action, audit.ExamId, audit.BranchId));

        await (await faculty.PostJsonAsync("/api/exams", Request(other.Batch))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Coordinators_create_exams_for_their_year_only()
    {
        var s = await _world.SectionAsync();
        var otherYear = await _world.SectionAsync();
        var cc = await _world.LoginAsync(await _world.UserAsync(RoleNames.ClassCoordinator, s.Year));

        await (await cc.PostJsonAsync("/api/exams", Request(s.Batch))).ExpectAsync(HttpStatusCode.Created);
        await (await cc.PostJsonAsync("/api/exams", Request(s.Department))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await cc.PostJsonAsync("/api/exams", Request(otherYear.Year))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_exams_report_their_bad_fields()
    {
        var s = await _world.SectionAsync();
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var bad = Request(s.Batch, "  ") with { Title = "", EndsAt = _world.Now, DurationMinutes = 0 };

        var fields = await (await faculty.PostJsonAsync("/api/exams", bad)).ValidationFieldsAsync();
        Assert.Equal(["durationMinutes", "endsAt", "questions", "title"], fields.OrderBy(f => f).ToArray());
    }

    [Fact]
    public async Task Times_without_a_zone_are_treated_as_utc()
    {
        var s = await _world.SectionAsync();
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var json = $$"""
            {"title":"Zone test","startsAt":"2030-01-01T10:00:00","endsAt":"2030-01-01T12:00:00","durationMinutes":60,
             "mode":"WebAllowed","isActive":true,"branchId":{{s.BranchId}},"classId":null,"batchId":null,
             "allowQuestionEditAfterAttempts":true,"allowAttemptReset":true,"allowAttemptReopen":true,"allowTimeExtension":true,
             "questions":[{"id":null,"text":"Q"}]}
            """;

        var body = await (await faculty.PostAsync("/api/exams", new StringContent(json, Encoding.UTF8, "application/json"))).ExpectAsync(HttpStatusCode.Created);
        Assert.Equal("2030-01-01T10:00:00Z", body.GetProperty("startsAt").GetString());
    }

    [Fact]
    public async Task Staff_see_exams_they_created_or_oversee()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, a.Department);
        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, a.Department));
        var hodA = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var hodB = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, b.Department));
        var exam = await _world.ExamAsync(author.Id, a.Batch);

        Assert.Contains(await List(await _world.LoginAsync(author)), e => e.Id == exam.Id);
        Assert.Contains(await List(hodA), e => e.Id == exam.Id);
        Assert.DoesNotContain(await List(colleague), e => e.Id == exam.Id);
        Assert.DoesNotContain(await List(hodB), e => e.Id == exam.Id);

        var detail = await (await hodA.GetAsync($"/api/exams/{exam.Id}")).ReadAsync<ExamDto>();
        Assert.Equal(2, detail.Questions!.Count);
        await (await hodB.GetAsync($"/api/exams/{exam.Id}")).ExpectProblemAsync(HttpStatusCode.NotFound);

        static async Task<List<ExamDto>> List(HttpClient c) => await (await c.GetAsync("/api/exams")).ReadAsync<List<ExamDto>>();
    }

    [Fact]
    public async Task Students_see_active_exams_for_their_batch_without_questions()
    {
        var s = await _world.SectionAsync();
        var other = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, s.Batch));
        var forMe = await _world.ExamAsync(author.Id, s.Year);
        var inactive = await _world.ExamAsync(author.Id, s.Batch, configure: e => e.IsActive = false);
        var notMine = await _world.ExamAsync(author.Id, other.Batch);

        var list = await (await student.GetAsync("/api/exams")).ReadAsync<List<StudentExamDto>>();
        Assert.Contains(list, e => e.Id == forMe.Id);
        Assert.DoesNotContain(list, e => e.Id == inactive.Id || e.Id == notMine.Id);

        var body = await (await student.GetAsync($"/api/exams/{forMe.Id}")).ExpectAsync(HttpStatusCode.OK);
        Assert.False(body.TryGetProperty("questions", out _));
    }

    [Fact]
    public async Task Only_the_creator_or_ManageExams_can_edit_and_edits_are_audited()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var subHod = await _world.LoginAsync(await _world.UserAsync(RoleNames.SubHod, s.Department));
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var edit = Request(s.Batch, "New Q") with { Title = "Renamed" };

        await (await colleague.PutJsonAsync($"/api/exams/{exam.Id}", edit)).ExpectProblemAsync(HttpStatusCode.Forbidden);
        var updated = await (await subHod.PutJsonAsync($"/api/exams/{exam.Id}", edit)).ReadAsync<ExamDto>();

        Assert.Equal("Renamed", updated.Title);
        Assert.Equal(["New Q"], updated.Questions!.Select(q => q.Text));
        var audit = await _world.LastAuditAsync();
        Assert.Equal("exam.update", audit.Action);
        Assert.Contains("Question 1", audit.BeforeJson);
    }

    [Fact]
    public async Task Questions_can_change_after_attempts_only_with_the_switch_and_snapshots_never_change()
    {
        var s = await _world.SectionAsync();
        var authorUser = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var author = await _world.LoginAsync(authorUser);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(authorUser.Id, s.Batch, questions: 1);
        var attempt = await _world.AttemptAsync(exam.Id, student.Id);
        var original = (await (await author.GetAsync($"/api/exams/{exam.Id}")).ReadAsync<ExamDto>()).Questions!.Single();
        var replaceQuestion = Request(s.Batch, "Brand new question") with { Title = exam.Title };

        try
        {
            await _world.SetSwitchesAsync(false);
            var globalOff = await (await author.PutJsonAsync($"/api/exams/{exam.Id}", replaceQuestion)).ExpectProblemAsync(HttpStatusCode.Forbidden);
            Assert.Contains("whole institution", globalOff);

            // Non-question edits are still fine while the switch is off.
            var keepQuestions = replaceQuestion with { Title = "Retitled", Questions = [new QuestionInput(original.Id, original.Text)] };
            await (await author.PutJsonAsync($"/api/exams/{exam.Id}", keepQuestions)).ExpectAsync(HttpStatusCode.OK);

            await _world.SetSwitchesAsync(true);
            await _world.WithDb(db => db.Exams.Where(e => e.Id == exam.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.AllowQuestionEditAfterAttempts, false)));
            var examOff = await (await author.PutJsonAsync($"/api/exams/{exam.Id}", replaceQuestion with { AllowQuestionEditAfterAttempts = false })).ExpectProblemAsync(HttpStatusCode.Forbidden);
            Assert.Contains("this exam", examOff);

            await _world.WithDb(db => db.Exams.Where(e => e.Id == exam.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.AllowQuestionEditAfterAttempts, true)));
            var updated = await (await author.PutJsonAsync($"/api/exams/{exam.Id}", replaceQuestion)).ReadAsync<ExamDto>();
            Assert.Equal(["Brand new question"], updated.Questions!.Select(q => q.Text));

            var reloaded = await _world.ReloadAttemptAsync(attempt.Id);
            Assert.Equal(attempt.QuestionText, reloaded.QuestionText);
            var oldQuestionDeleted = await _world.WithDb(db => db.Questions.Where(q => q.Id == original.Id).Select(q => q.IsDeleted).SingleAsync());
            Assert.True(oldQuestionDeleted);
        }
        finally
        {
            await _world.SetSwitchesAsync(true);
        }
    }

    [Fact]
    public async Task Exams_with_attempts_cannot_be_deleted_but_can_be_deactivated()
    {
        var s = await _world.SectionAsync();
        var authorUser = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var author = await _world.LoginAsync(authorUser);
        var used = await _world.ExamAsync(authorUser.Id, s.Batch);
        var unused = await _world.ExamAsync(authorUser.Id, s.Batch);
        await _world.AttemptAsync(used.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id);

        await (await author.DeleteAsync($"/api/exams/{used.Id}")).ExpectProblemAsync(HttpStatusCode.Conflict);
        var off = await (await author.PatchJsonAsync($"/api/exams/{used.Id}/active", new SetActiveRequest(false))).ReadAsync<ExamDto>();
        Assert.False(off.IsActive);
        Assert.Equal("exam.set_active", (await _world.LastAuditAsync()).Action);

        await (await author.DeleteAsync($"/api/exams/{unused.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("exam.delete", (await _world.LastAuditAsync()).Action);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter ExamTests`
Expected: build fails — `ExamRequest` doesn't exist.

- [ ] **Step 3: Add the contracts**

`server/Secucode.Contracts/ExamContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record QuestionInput(int? Id, string Text);

public sealed record QuestionDto(int Id, string Text, int SortOrder);

// Target: BranchId alone = whole department; + ClassId = that year; + BatchId = that batch.
public sealed record ExamRequest(string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, bool IsActive,
    int BranchId, int? ClassId, int? BatchId,
    bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension,
    IReadOnlyList<QuestionInput> Questions);

public sealed record ExamDto(int Id, string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, bool IsActive,
    int BranchId, int? ClassId, int? BatchId, int CreatedBy, string CreatedByName,
    bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension,
    IReadOnlyList<QuestionDto>? Questions);

public sealed record StudentExamDto(int Id, string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, int? InProgressAttemptId);

public sealed record SetActiveRequest(bool IsActive);
```

- [ ] **Step 4: Add switches and mapping**

`server/Secucode.Api/Features/Exams/Switches.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;

namespace Secucode.Api.Features.Exams;

public enum Switch { QuestionEdit, Reset, Reopen, Extension }

// Spec §6: effective switch = global AND per-exam.
public static class Switches
{
    public static async Task EnsureOnAsync(AppDbContext db, Exam exam, Switch which)
    {
        var s = await db.SystemSettings.AsNoTracking().SingleAsync();
        var (global, local, label) = which switch
        {
            Switch.QuestionEdit => (s.AllowQuestionEditAfterAttempts, exam.AllowQuestionEditAfterAttempts, "Editing questions after attempts is"),
            Switch.Reset => (s.AllowAttemptReset, exam.AllowAttemptReset, "Attempt resets are"),
            Switch.Reopen => (s.AllowAttemptReopen, exam.AllowAttemptReopen, "Reopening attempts is"),
            _ => (s.AllowTimeExtension, exam.AllowTimeExtension, "Time extensions are"),
        };
        if (!global)
            throw ApiException.Forbidden($"{label} disabled for the whole institution.");
        if (!local)
            throw ApiException.Forbidden($"{label} disabled for this exam.");
    }
}
```

`server/Secucode.Api/Features/Exams/ExamMapping.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Exams;

public static class ExamMapping
{
    public static async Task<ExamDto> ToDtoAsync(AppDbContext db, Exam e, bool includeQuestions)
    {
        var creator = await db.Users.Where(u => u.Id == e.CreatedBy).Select(u => u.FullName).SingleAsync();
        return ToDto(e, creator, includeQuestions);
    }

    public static ExamDto ToDto(Exam e, string creatorName, bool includeQuestions) => new(
        e.Id, e.Title, e.StartsAt, e.EndsAt, e.DurationMinutes, e.Mode, e.IsActive, e.BranchId, e.ClassId, e.BatchId,
        e.CreatedBy, creatorName, e.AllowQuestionEditAfterAttempts, e.AllowAttemptReset, e.AllowAttemptReopen, e.AllowTimeExtension,
        includeQuestions
            ? e.Questions.Where(q => !q.IsDeleted).OrderBy(q => q.SortOrder).Select(q => new QuestionDto(q.Id, q.Text, q.SortOrder)).ToList()
            : null);

    public static object Snapshot(Exam e) => new
    {
        e.Title, e.StartsAt, e.EndsAt, e.DurationMinutes, e.Mode, e.IsActive, e.BranchId, e.ClassId, e.BatchId,
        e.AllowQuestionEditAfterAttempts, e.AllowAttemptReset, e.AllowAttemptReopen, e.AllowTimeExtension,
        Questions = e.Questions.Where(q => !q.IsDeleted).OrderBy(q => q.SortOrder).Select(q => q.Text).ToList(),
    };
}
```

- [ ] **Step 5: Add the exam endpoints**

`server/Secucode.Api/Features/Exams/ExamEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Exams;

public static class ExamEndpoints
{
    private sealed record ValidExam(DateTime StartsAt, DateTime EndsAt, Scope Target, List<QuestionInput> Questions);

    public static void MapExams(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/exams");
        g.MapGet("", ListAsync);
        g.MapGet("/{id:int}", GetAsync);
        g.MapPost("", CreateAsync);
        g.MapPut("/{id:int}", UpdateAsync);
        g.MapPatch("/{id:int}/active", SetActiveAsync);
        g.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(AppDbContext db, Caller caller, TimeProvider time, DateTime? from, DateTime? to, int? branchId)
    {
        if (!await caller.IsStaffAsync())
        {
            var scope = await caller.StudentScopeAsync() ?? throw ApiException.Forbidden("You have no active role.");
            return Results.Ok(await StudentListAsync(db, caller.UserId, scope, time.UtcNow()));
        }

        var q = db.Exams.AsNoTracking();
        if (from is not null) q = q.Where(e => e.EndsAt >= ToUtc(from.Value));
        if (to is not null) q = q.Where(e => e.StartsAt <= ToUtc(to.Value));
        if (branchId is not null) q = q.Where(e => e.BranchId == branchId);
        var rows = await q.OrderBy(e => e.StartsAt)
            .Select(e => new { Exam = e, Creator = db.Users.Where(u => u.Id == e.CreatedBy).Select(u => u.FullName).First() })
            .ToListAsync();

        // ponytail: visibility filtered in memory; fine for a college's exam volume, push into SQL if it grows large
        var visible = new List<ExamDto>();
        foreach (var r in rows)
            if (await caller.CanOnExamAsync(Capability.ViewResults, r.Exam) || await caller.CanOnExamAsync(Capability.ManageExams, r.Exam))
                visible.Add(ExamMapping.ToDto(r.Exam, r.Creator, includeQuestions: false));
        return Results.Ok(visible);
    }

    private static async Task<List<StudentExamDto>> StudentListAsync(AppDbContext db, int studentId, Scope scope, DateTime now)
    {
        var attempts = await db.Attempts.Where(a => a.StudentId == studentId)
            .Select(a => new { a.Id, a.ExamId, a.AttemptNumber, a.Status, a.AllowanceMinutes }).ToListAsync();
        var latest = attempts.GroupBy(a => a.ExamId).ToDictionary(g => g.Key, g => g.MaxBy(a => a.AttemptNumber)!);
        var allowanceExamIds = latest.Values.Where(a => a.Status == AttemptStatus.Voided && a.AllowanceMinutes != null).Select(a => a.ExamId).ToList();

        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.IsActive && (e.EndsAt > now || allowanceExamIds.Contains(e.Id)))
            .OrderBy(e => e.StartsAt).ToListAsync();

        return exams
            .Where(e => e.Target.Contains(scope))
            .Where(e => !latest.TryGetValue(e.Id, out var a) || a.Status is AttemptStatus.InProgress or AttemptStatus.Voided)
            .Select(e => new StudentExamDto(e.Id, e.Title, e.StartsAt, e.EndsAt, e.DurationMinutes, e.Mode,
                latest.TryGetValue(e.Id, out var a) && a.Status == AttemptStatus.InProgress ? a.Id : null))
            .ToList();
    }

    private static async Task<IResult> GetAsync(int id, AppDbContext db, Caller caller)
    {
        var exam = await db.Exams.Include(e => e.Questions).AsNoTracking().SingleOrDefaultAsync(e => e.Id == id)
            ?? throw ApiException.NotFound("Exam");

        if (await caller.IsStaffAsync())
        {
            if (!await caller.CanOnExamAsync(Capability.ViewResults, exam) && !await caller.CanOnExamAsync(Capability.ManageExams, exam))
                throw ApiException.NotFound("Exam");
            return Results.Ok(await ExamMapping.ToDtoAsync(db, exam, includeQuestions: true));
        }

        var scope = await caller.StudentScopeAsync();
        if (scope is null || !exam.IsActive || !exam.Target.Contains(scope.Value))
            throw ApiException.NotFound("Exam");
        var userId = caller.UserId;
        var inProgress = await db.Attempts.Where(a => a.ExamId == id && a.StudentId == userId && a.Status == AttemptStatus.InProgress)
            .Select(a => (int?)a.Id).FirstOrDefaultAsync();
        return Results.Ok(new StudentExamDto(exam.Id, exam.Title, exam.StartsAt, exam.EndsAt, exam.DurationMinutes, exam.Mode, inProgress));
    }

    private static async Task<IResult> CreateAsync(ExamRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var input = Validate(req);
        await caller.RequireAsync(Capability.CreateExams, input.Target, "You can't create exams for that department, class or batch.");
        await EnsureTargetExistsAsync(db, input.Target);

        var exam = new Exam { CreatedBy = caller.UserId, CreatedAt = time.UtcNow() };
        Apply(exam, req, input);
        for (var i = 0; i < input.Questions.Count; i++)
            exam.Questions.Add(new Question { Text = input.Questions[i].Text, SortOrder = i });

        await using var tx = await db.Database.BeginTransactionAsync();
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        audit.Record(caller.UserId, "exam.create", "Exam", exam.Id, after: ExamMapping.Snapshot(exam), examId: exam.Id, branchId: exam.BranchId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/exams/{exam.Id}", await ExamMapping.ToDtoAsync(db, exam, includeQuestions: true));
    }

    private static async Task<ExamDto> UpdateAsync(int id, ExamRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ManageExams, exam, "You can't edit this exam.");
        var input = Validate(req);

        if (input.Target != exam.Target)
        {
            if (!await caller.CanAsync(Capability.CreateExams, input.Target) && !await caller.CanAsync(Capability.ManageExams, input.Target))
                throw ApiException.Forbidden("You can't move this exam to that department, class or batch.");
            await EnsureTargetExistsAsync(db, input.Target);
        }

        var usedQuestionIds = (await db.Attempts.Where(a => a.ExamId == id).Select(a => a.QuestionId).Distinct().ToListAsync()).ToHashSet();
        if (usedQuestionIds.Count > 0 && QuestionsChanged(exam, input.Questions))
        {
            await caller.RequireOnExamAsync(Capability.EditQuestionsAfterAttempts, exam, "You can't edit questions after students have started.");
            await Switches.EnsureOnAsync(db, exam, Switch.QuestionEdit);
        }

        var before = ExamMapping.Snapshot(exam);
        Apply(exam, req, input);
        SyncQuestions(exam, input.Questions, usedQuestionIds);
        audit.Record(caller.UserId, "exam.update", "Exam", exam.Id, before, ExamMapping.Snapshot(exam), examId: exam.Id, branchId: exam.BranchId);
        await db.SaveChangesAsync();
        return await ExamMapping.ToDtoAsync(db, exam, includeQuestions: true);
    }

    private static async Task<ExamDto> SetActiveAsync(int id, SetActiveRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ManageExams, exam, "You can't change this exam.");
        var before = new { exam.IsActive };
        exam.IsActive = req.IsActive;
        audit.Record(caller.UserId, "exam.set_active", "Exam", exam.Id, before, new { exam.IsActive }, examId: exam.Id, branchId: exam.BranchId);
        await db.SaveChangesAsync();
        return await ExamMapping.ToDtoAsync(db, exam, includeQuestions: true);
    }

    private static async Task<IResult> DeleteAsync(int id, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ManageExams, exam, "You can't delete this exam.");
        if (await db.Attempts.AnyAsync(a => a.ExamId == id))
            throw ApiException.Conflict("Students have attempted this exam, so it can't be deleted. Deactivate it instead.");

        var before = ExamMapping.Snapshot(exam);
        db.Exams.Remove(exam);
        audit.Record(caller.UserId, "exam.delete", "Exam", id, before, examId: id, branchId: exam.BranchId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static ValidExam Validate(ExamRequest req)
    {
        var questions = (req.Questions ?? [])
            .Where(q => !string.IsNullOrWhiteSpace(q.Text))
            .Select(q => q with { Text = q.Text.Trim() })
            .ToList();
        var starts = ToUtc(req.StartsAt);
        var ends = ToUtc(req.EndsAt);
        var target = new Scope(req.BranchId, req.ClassId, req.BatchId);

        new Validator()
            .Rule(!string.IsNullOrWhiteSpace(req.Title) && req.Title.Trim().Length <= 255, "title", "Title is required (up to 255 characters).")
            .Rule(ends > starts, "endsAt", "The exam must end after it starts.")
            .Rule(req.DurationMinutes is >= 1 and <= 1440, "durationMinutes", "Duration must be between 1 and 1440 minutes.")
            .Rule(target.IsValidShape, "batchId", "A batch needs a class.")
            .Rule(Enum.IsDefined(req.Mode), "mode", "Unknown exam mode.")
            .Rule(questions.Count > 0, "questions", "Add at least one question.")
            .Rule(questions.All(q => q.Text.Length <= 10_000), "questions", "Each question can be up to 10,000 characters.")
            .ThrowIfInvalid();
        return new ValidExam(starts, ends, target, questions);
    }

    // Clients may send times without a zone (e.g. from a form). We store UTC, so treat unzoned values as UTC.
    private static DateTime ToUtc(DateTime d) => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static void Apply(Exam exam, ExamRequest req, ValidExam input)
    {
        exam.Title = req.Title.Trim();
        exam.StartsAt = input.StartsAt;
        exam.EndsAt = input.EndsAt;
        exam.DurationMinutes = req.DurationMinutes;
        exam.Mode = req.Mode;
        exam.IsActive = req.IsActive;
        exam.BranchId = input.Target.BranchId!.Value;
        exam.ClassId = input.Target.ClassId;
        exam.BatchId = input.Target.BatchId;
        exam.AllowQuestionEditAfterAttempts = req.AllowQuestionEditAfterAttempts;
        exam.AllowAttemptReset = req.AllowAttemptReset;
        exam.AllowAttemptReopen = req.AllowAttemptReopen;
        exam.AllowTimeExtension = req.AllowTimeExtension;
    }

    private static bool QuestionsChanged(Exam exam, List<QuestionInput> inputs)
    {
        var current = exam.Questions.Where(q => !q.IsDeleted).OrderBy(q => q.SortOrder).ToList();
        return current.Count != inputs.Count || current.Where((q, i) => inputs[i].Id != q.Id || inputs[i].Text != q.Text).Any();
    }

    // Matches inputs to existing questions by id; questions given to an attempt are soft-deleted, never removed.
    private static void SyncQuestions(Exam exam, List<QuestionInput> inputs, HashSet<int> usedQuestionIds)
    {
        var live = exam.Questions.Where(q => !q.IsDeleted).ToDictionary(q => q.Id);
        var kept = new HashSet<int>();
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].Id is int qid && live.TryGetValue(qid, out var q))
            {
                q.Text = inputs[i].Text;
                q.SortOrder = i;
                kept.Add(qid);
            }
            else
            {
                exam.Questions.Add(new Question { Text = inputs[i].Text, SortOrder = i });
            }
        }
        foreach (var q in live.Values.Where(q => !kept.Contains(q.Id)))
        {
            if (usedQuestionIds.Contains(q.Id))
                q.IsDeleted = true;
            else
                exam.Questions.Remove(q); // orphan of a required relationship → deleted on save
        }
    }

    private static async Task EnsureTargetExistsAsync(AppDbContext db, Scope s)
    {
        var v = new Validator();
        v.Rule(s.BranchId is int b && await db.Branches.AnyAsync(x => x.Id == b), "branchId", "That department doesn't exist.");
        if (s.ClassId is int c) v.Rule(await db.Classes.AnyAsync(x => x.Id == c), "classId", "That class doesn't exist.");
        if (s.BatchId is int t) v.Rule(await db.Batches.AnyAsync(x => x.Id == t), "batchId", "That batch doesn't exist.");
        v.ThrowIfInvalid();
    }
}
```

In `Endpoints.MapSecucodeEndpoints` add `api.MapExams();` and `using Secucode.Api.Features.Exams;`.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter ExamTests`
Expected: 9 passed.

- [ ] **Step 7: Commit**

```bash
git add -A server
git commit -m "feat(api): exams with scoped access, per-exam switches and question snapshots"
```

---
### Task 9: Code runner — Local mode and `/run`

`ICodeRunner` with the process-based Local implementation (today's desktop `CodeExecutor` behaviour moved server-side), the `/run` endpoint and its per-user rate limit (spec §10, §9 Code runner).

**Files:**
- Create: `server/Secucode.Contracts/RunContracts.cs`
- Create: `server/Secucode.Api/Features/Runner/RunnerOptions.cs`, `ICodeRunner.cs`, `LocalCodeRunner.cs`, `RunnerEndpoints.cs`
- Modify: `server/Secucode.Api/ServiceSetup.cs`, `server/Secucode.Api/Endpoints.cs`
- Test: `server/Secucode.Api.Tests/LocalRunnerTests.cs`

**Interfaces:**
- Consumes: `ErrorHandling.WriteAsync`, `Validator`.
- Produces: `RunRequest(string Language, string Code, string? Stdin)`, `RunResult(string Stdout, string Stderr, int ExitCode, bool TimedOut, long DurationMs)`; `RunnerOptions` (`Mode`, `DockerEndpoint`, `CompileTimeoutSeconds`, `RunTimeoutSeconds`, `MaxOutputBytes`, `MaxCodeBytes`, `Languages`), `LanguageOptions` (`Image`, `FileName`, `Compile`, `Run`, `LocalCompile`, `LocalRun`); `ICodeRunner.RunAsync(RunRequest, CancellationToken) → Task<RunResult>`; rate-limit policy name `"run"`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/LocalRunnerTests.cs`:

```csharp
using System.Net;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

// Needs `python` and `node` on PATH (the test host runs the Local runner).
[Collection(ApiCollection.Name)]
public sealed class LocalRunnerTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<HttpClient> SomeoneAsync() =>
        await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department));

    private static async Task<RunResult> Run(HttpClient c, string language, string code, string? stdin = null) =>
        await (await c.PostJsonAsync("/api/run", new RunRequest(language, code, stdin))).ReadAsync<RunResult>();

    [Fact]
    public async Task Python_runs_and_reads_stdin()
    {
        var r = await Run(await SomeoneAsync(), "Python", "n=int(input()); print(sum(range(1,n+1)))", "100");
        Assert.Equal("5050", r.Stdout.Trim());
        Assert.Equal(0, r.ExitCode);
        Assert.False(r.TimedOut);
    }

    [Fact]
    public async Task JavaScript_runs()
    {
        Assert.Equal("42", (await Run(await SomeoneAsync(), "javascript", "console.log(6*7)")).Stdout.Trim());
    }

    [Fact]
    public async Task Runaway_programs_time_out()
    {
        Assert.True((await Run(await SomeoneAsync(), "Python", "while True: pass")).TimedOut);
    }

    [Fact]
    public async Task Output_is_capped()
    {
        var r = await Run(await SomeoneAsync(), "Python", "print('x' * 200000)");
        Assert.InRange(r.Stdout.Length, 1, 65536);
    }

    [Fact]
    public async Task Runtime_errors_are_reported()
    {
        var r = await Run(await SomeoneAsync(), "Python", "raise ValueError('boom')");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("ValueError", r.Stderr);
    }

    [Fact]
    public async Task Unsupported_languages_are_rejected()
    {
        var c = await SomeoneAsync();
        Assert.Equal(["language"], await (await c.PostJsonAsync("/api/run", new RunRequest("Cobol", "x", null))).ValidationFieldsAsync());
    }

    [Fact]
    public async Task Running_code_requires_sign_in()
    {
        await (await api.Factory.CreateClient().PostJsonAsync("/api/run", new RunRequest("Python", "print(1)", null)))
            .ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Each_user_gets_ten_runs_per_minute()
    {
        var c = await SomeoneAsync();
        for (var i = 0; i < 10; i++)
            await (await c.PostJsonAsync("/api/run", new RunRequest("Python", "print(1)", null))).ExpectAsync(HttpStatusCode.OK);
        await (await c.PostJsonAsync("/api/run", new RunRequest("Python", "print(1)", null))).ExpectProblemAsync(HttpStatusCode.TooManyRequests);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter LocalRunnerTests`
Expected: build fails — `RunRequest` doesn't exist.

- [ ] **Step 3: Add contracts, options and the runner interface**

`server/Secucode.Contracts/RunContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record RunRequest(string Language, string Code, string? Stdin);

public sealed record RunResult(string Stdout, string Stderr, int ExitCode, bool TimedOut, long DurationMs);
```

`server/Secucode.Api/Features/Runner/RunnerOptions.cs`:

```csharp
namespace Secucode.Api.Features.Runner;

public sealed class RunnerOptions
{
    public string Mode { get; set; } = "Local"; // "Local" or "Docker"
    public string? DockerEndpoint { get; set; }  // null = Docker's default for this OS
    public int CompileTimeoutSeconds { get; set; } = 10;
    public int RunTimeoutSeconds { get; set; } = 5;
    public int MaxOutputBytes { get; set; } = 65536;
    public int MaxCodeBytes { get; set; } = 65536;

    // Adding a language is configuration only (spec §10). Keys are case-insensitive.
    public Dictionary<string, LanguageOptions> Languages { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Python"] = new() { Image = "python:3.12-slim", FileName = "main.py", Run = "python main.py" },
        ["JavaScript"] = new() { Image = "node:22-slim", FileName = "main.js", Run = "node main.js" },
        ["Java"] = new() { Image = "eclipse-temurin:21-jdk", FileName = "Main.java", Run = "java Main.java" },
        ["C++"] = new()
        {
            Image = "gcc:14", FileName = "main.cpp", Compile = "g++ -O2 -o main main.cpp", Run = "./main",
            LocalCompile = "g++ -O2 -o main.exe main.cpp", LocalRun = "./main.exe",
        },
    };
}

public sealed class LanguageOptions
{
    public string Image { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Compile { get; set; }
    public string Run { get; set; } = "";
    public string? LocalCompile { get; set; } // override for Local mode (e.g. Windows needs main.exe)
    public string? LocalRun { get; set; }
}
```

`server/Secucode.Api/Features/Runner/ICodeRunner.cs`:

```csharp
using Secucode.Contracts;

namespace Secucode.Api.Features.Runner;

public interface ICodeRunner
{
    // The request is already validated: Language is a configured key, sizes are within limits.
    Task<RunResult> RunAsync(RunRequest request, CancellationToken ct);
}
```

- [ ] **Step 4: Add the Local runner**

`server/Secucode.Api/Features/Runner/LocalCodeRunner.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Secucode.Contracts;

namespace Secucode.Api.Features.Runner;

// Runs code as a child process of the API in a throwaway folder.
// ponytail: no isolation — dev only. Production uses DockerCodeRunner (Runner:Mode = Docker).
public sealed class LocalCodeRunner(IOptions<RunnerOptions> options) : ICodeRunner
{
    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken ct)
    {
        var o = options.Value;
        var lang = o.Languages[request.Language];
        var dir = Directory.CreateTempSubdirectory("secucode_run_").FullName;
        var clock = Stopwatch.StartNew();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, lang.FileName), request.Code, ct);

            var compile = lang.LocalCompile ?? lang.Compile;
            if (compile is not null)
            {
                var c = await ExecAsync(compile, dir, "", TimeSpan.FromSeconds(o.CompileTimeoutSeconds), o.MaxOutputBytes, ct);
                if (c.TimedOut || c.ExitCode != 0)
                    return c with { DurationMs = clock.ElapsedMilliseconds };
            }

            var r = await ExecAsync(lang.LocalRun ?? lang.Run, dir, request.Stdin ?? "", TimeSpan.FromSeconds(o.RunTimeoutSeconds), o.MaxOutputBytes, ct);
            return r with { DurationMs = clock.ElapsedMilliseconds };
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* a just-killed exe can stay locked for a moment */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<RunResult> ExecAsync(string command, string dir, string stdin, TimeSpan timeout, int maxChars, CancellationToken ct)
    {
        var (file, args) = Split(command, dir);
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = dir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment.Remove("FORCE_COLOR");
        psi.Environment["NO_COLOR"] = "1";

        Process proc;
        try
        {
            proc = Process.Start(psi)!;
        }
        catch (Win32Exception)
        {
            return new RunResult("", $"'{Path.GetFileName(file)}' is not installed on the server.", -1, false, 0);
        }

        using (proc)
        {
            // Read both streams concurrently so a chatty program can't fill a pipe and deadlock.
            var stdoutTask = ReadCappedAsync(proc.StandardOutput, maxChars);
            var stderrTask = ReadCappedAsync(proc.StandardError, maxChars);
            try
            {
                await proc.StandardInput.WriteAsync(stdin);
                proc.StandardInput.Close(); // EOF so input() doesn't wait forever
            }
            catch (IOException) { /* exited without reading stdin */ }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var timedOut = false;
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync(CancellationToken.None);
            }
            return new RunResult(await stdoutTask, await stderrTask, timedOut ? -1 : proc.ExitCode, timedOut, 0);
        }
    }

    // Keeps draining after the cap so the child never blocks on a full pipe.
    private static async Task<string> ReadCappedAsync(StreamReader reader, int maxChars)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        int n;
        while ((n = await reader.ReadAsync(buffer)) > 0)
            if (sb.Length < maxChars)
                sb.Append(buffer, 0, Math.Min(n, maxChars - sb.Length));
        return sb.ToString();
    }

    // "./main.exe" resolves inside the run folder; anything else is looked up on PATH.
    private static (string File, string Args) Split(string command, string dir)
    {
        var space = command.IndexOf(' ');
        var file = space < 0 ? command : command[..space];
        var args = space < 0 ? "" : command[(space + 1)..];
        if (file.StartsWith("./"))
            file = Path.Combine(dir, file[2..]);
        return (file, args);
    }
}
```

- [ ] **Step 5: Add the endpoint and rate limit**

`server/Secucode.Api/Features/Runner/RunnerEndpoints.cs`:

```csharp
using System.Text;
using Microsoft.Extensions.Options;
using Secucode.Api.Common;
using Secucode.Contracts;

namespace Secucode.Api.Features.Runner;

public static class RunnerEndpoints
{
    public const string RateLimitPolicy = "run";

    public static void MapRunner(this IEndpointRouteBuilder api)
    {
        api.MapPost("/run", async (RunRequest req, ICodeRunner runner, IOptions<RunnerOptions> options, CancellationToken ct) =>
        {
            var o = options.Value;
            new Validator()
                .Rule(req.Language is not null && o.Languages.ContainsKey(req.Language), "language", $"Supported languages: {string.Join(", ", o.Languages.Keys)}.")
                .Rule(req.Code is not null && Encoding.UTF8.GetByteCount(req.Code) <= o.MaxCodeBytes, "code", $"Code is required and must be under {o.MaxCodeBytes / 1024} KB.")
                .Rule(Encoding.UTF8.GetByteCount(req.Stdin ?? "") <= o.MaxCodeBytes, "stdin", $"Input must be under {o.MaxCodeBytes / 1024} KB.")
                .ThrowIfInvalid();
            return await runner.RunAsync(req, ct);
        }).RequireRateLimiting(RateLimitPolicy);
    }
}
```

In `ServiceSetup.AddSecucodeApi`, add before `return services;`:

```csharp
        services.Configure<RunnerOptions>(config.GetSection("Runner"));
        services.AddSingleton<ICodeRunner>(sp => ActivatorUtilities.CreateInstance<LocalCodeRunner>(sp));
        services.AddRateLimiter(o =>
        {
            o.OnRejected = (ctx, _) => new ValueTask(ErrorHandling.WriteAsync(ctx.HttpContext, 429, "Too many requests",
                "You can run code at most 10 times per minute.", null));
            o.AddPolicy(RunnerEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirstValue("sub") ?? http.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
```

Add usings: `using System.Security.Claims;`, `using System.Threading.RateLimiting;`, `using Microsoft.AspNetCore.RateLimiting;`, `using Secucode.Api.Features.Runner;`.

In `UseSecucodeApi`, add `app.UseRateLimiter();` right after the `Caller` middleware (it needs the authenticated user).
In `Endpoints.MapSecucodeEndpoints` add `api.MapRunner();` and `using Secucode.Api.Features.Runner;`.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter LocalRunnerTests`
Expected: 8 passed. (If `python` isn't on PATH, the Python tests fail with "'python' is not installed on the server." — install Python or add `Runner:Languages:Python:LocalRun` pointing at your interpreter.)

- [ ] **Step 7: Commit**

```bash
git add -A server
git commit -m "feat(api): code runner with local mode, limits and per-user rate limiting"
```

---

### Task 10: Code runner — Docker mode

Sandboxed runs: one throwaway container per run with no network, 256 MB, 0.5 CPU, pids 64, read-only root, non-root user (spec §10).

**Files:**
- Create: `server/Secucode.Api/Features/Runner/DockerCodeRunner.cs`
- Modify: `server/Secucode.Api/ServiceSetup.cs`
- Test: `server/Secucode.Api.Tests/DockerRunnerTests.cs`

**Interfaces:**
- Consumes: `ICodeRunner`, `RunnerOptions`, `LanguageOptions`.
- Produces: `DockerCodeRunner(IOptions<RunnerOptions>)` implementing `ICodeRunner`; selected when `Runner:Mode` is `Docker`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/DockerRunnerTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using Secucode.Api.Features.Runner;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

// Pulls python:3.12-slim and gcc:14 on first run (gcc is ~1 GB), so the first run is slow.
[Trait("Category", "Docker")]
public sealed class DockerRunnerTests : IDisposable
{
    private readonly DockerCodeRunner _runner = new(Options.Create(new RunnerOptions { Mode = "Docker" }));

    public void Dispose() => _runner.Dispose();

    private Task<RunResult> Run(string language, string code, string? stdin = null) =>
        _runner.RunAsync(new RunRequest(language, code, stdin), CancellationToken.None);

    [Fact]
    public async Task Python_runs_with_stdin_as_an_unprivileged_user()
    {
        var r = await Run("Python", "import os\nn=int(input())\nprint(n*2, os.getuid())", "21");
        Assert.Equal("42 65534", r.Stdout.Trim());
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Cpp_compiles_and_runs_and_compile_errors_are_reported()
    {
        Assert.Equal("ok", (await Run("C++", "#include <iostream>\nint main(){std::cout<<\"ok\";}")).Stdout);

        var bad = await Run("C++", "int main( { }");
        Assert.NotEqual(0, bad.ExitCode);
        Assert.Contains("error", bad.Stderr);
    }

    [Fact]
    public async Task Runaway_programs_time_out()
    {
        Assert.True((await Run("Python", "while True: pass")).TimedOut);
    }

    [Fact]
    public async Task There_is_no_network()
    {
        var r = await Run("Python", "import urllib.request\nurllib.request.urlopen('http://1.1.1.1', timeout=3)");
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public async Task Memory_is_limited()
    {
        var r = await Run("Python", "x = b'x' * (1024 * 1024 * 1024)\nprint('allocated')"); // writes every byte, so the limit really bites
        Assert.NotEqual(0, r.ExitCode);
        Assert.DoesNotContain("allocated", r.Stdout);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter Category=Docker`
Expected: build fails — `DockerCodeRunner` doesn't exist.

- [ ] **Step 3: Add the Docker runner**

`server/Secucode.Api/Features/Runner/DockerCodeRunner.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;
using Secucode.Contracts;

namespace Secucode.Api.Features.Runner;

// One container per run: no network, 256 MB, half a CPU, 64 processes, read-only root, user "nobody".
public sealed class DockerCodeRunner : ICodeRunner, IDisposable
{
    private readonly IOptions<RunnerOptions> _options;
    private readonly DockerClient _docker;
    private readonly ConcurrentDictionary<string, Task> _pulls = new();

    public DockerCodeRunner(IOptions<RunnerOptions> options)
    {
        _options = options;
        var endpoint = options.Value.DockerEndpoint;
        _docker = (endpoint is null ? new DockerClientConfiguration() : new DockerClientConfiguration(new Uri(endpoint))).CreateClient();
    }

    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken ct)
    {
        var o = _options.Value;
        var lang = o.Languages[request.Language];
        await EnsureImageAsync(lang.Image);

        // Code and stdin travel as base64 env vars, so the root filesystem can stay read-only.
        var created = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = lang.Image,
            Cmd = ["sh", "-c", BuildScript(lang, o)],
            WorkingDir = "/work",
            User = "65534:65534",
            NetworkDisabled = true,
            Env =
            [
                $"CODE_B64={Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Code))}",
                $"STDIN_B64={Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Stdin ?? ""))}",
                "HOME=/work",
                "NO_COLOR=1",
            ],
            HostConfig = new HostConfig
            {
                NetworkMode = "none",
                Memory = 256L * 1024 * 1024,
                MemorySwap = 256L * 1024 * 1024,
                NanoCPUs = 500_000_000,
                PidsLimit = 64,
                ReadonlyRootfs = true,
                Tmpfs = new Dictionary<string, string>
                {
                    ["/work"] = "rw,exec,nosuid,size=64m,mode=1777",
                    ["/tmp"] = "rw,exec,nosuid,size=64m,mode=1777",
                },
            },
        }, ct);

        var clock = Stopwatch.StartNew();
        try
        {
            await _docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(o.CompileTimeoutSeconds + o.RunTimeoutSeconds + 5)); // backstop over the in-container timeouts
            long exitCode;
            var killed = false;
            try
            {
                exitCode = (await _docker.Containers.WaitContainerAsync(created.ID, cts.Token)).StatusCode;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                killed = true;
                exitCode = -1;
                await _docker.Containers.KillContainerAsync(created.ID, new ContainerKillParameters(), CancellationToken.None);
            }

            var (stdout, stderr) = await ReadLogsAsync(created.ID, o.MaxOutputBytes);
            return new RunResult(stdout, stderr, (int)exitCode, killed || exitCode == 124, clock.ElapsedMilliseconds); // 124 = `timeout` fired
        }
        finally
        {
            await _docker.Containers.RemoveContainerAsync(created.ID, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
        }
    }

    public void Dispose() => _docker.Dispose();

    private static string BuildScript(LanguageOptions lang, RunnerOptions o)
    {
        var compile = lang.Compile is null ? "" : $"timeout {o.CompileTimeoutSeconds} {lang.Compile} || exit $?; ";
        return $"echo \"$CODE_B64\" | base64 -d > {lang.FileName} && echo \"$STDIN_B64\" | base64 -d > /tmp/stdin.txt && "
             + $"{compile}timeout {o.RunTimeoutSeconds} {lang.Run} < /tmp/stdin.txt";
    }

    private async Task<(string Stdout, string Stderr)> ReadLogsAsync(string id, int max)
    {
        using var stream = await _docker.Containers.GetContainerLogsAsync(id, tty: false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true }, CancellationToken.None);
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, CancellationToken.None);
            if (read.EOF)
                break;
            var target = read.Target == MultiplexedStream.TargetStream.StandardOut ? stdout : stderr;
            var room = max - (int)target.Length;
            if (room > 0)
                target.Write(buffer, 0, Math.Min(room, read.Count));
        }
        return (Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()));
    }

    // Pull each image once per process; a failed pull is retried on the next run.
    private async Task EnsureImageAsync(string image)
    {
        var pull = _pulls.GetOrAdd(image, PullIfMissingAsync);
        try
        {
            await pull;
        }
        catch
        {
            _pulls.TryRemove(image, out _);
            throw;
        }
    }

    private async Task PullIfMissingAsync(string image)
    {
        var existing = await _docker.Images.ListImagesAsync(new ImagesListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>> { ["reference"] = new Dictionary<string, bool> { [image] = true } },
        });
        if (existing.Count > 0)
            return;
        var parts = image.Split(':', 2);
        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = parts[0], Tag = parts.Length > 1 ? parts[1] : "latest" }, null, new Progress<JSONMessage>());
    }
}
```

- [ ] **Step 4: Select the runner from configuration**

In `ServiceSetup.AddSecucodeApi` replace the `ICodeRunner` registration with:

```csharp
        services.AddSingleton<ICodeRunner>(sp =>
            sp.GetRequiredService<IOptions<RunnerOptions>>().Value.Mode.Equals("Docker", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<DockerCodeRunner>(sp)
                : ActivatorUtilities.CreateInstance<LocalCodeRunner>(sp));
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter Category=Docker`
Expected: 5 passed (first run downloads images).

- [ ] **Step 6: Commit**

```bash
git add -A server
git commit -m "feat(api): sandboxed Docker code runner"
```

---

### Task 11: Live hub

SignalR hub `/hubs/exams` and the `LiveNotifier` that pushes attempt changes to an exam's staff group and to the student's private group (spec §9 Live hub).

**Files:**
- Create: `server/Secucode.Contracts/LiveContracts.cs`
- Create: `server/Secucode.Api/Features/Live/ExamHub.cs`, `Features/Live/LiveNotifier.cs`
- Modify: `server/Secucode.Api/ServiceSetup.cs`, `server/Secucode.Api/Endpoints.cs`
- Create: `server/Secucode.Api.Tests/Infrastructure/LiveClient.cs`
- Test: `server/Secucode.Api.Tests/LiveHubTests.cs`

**Interfaces:**
- Consumes: `Caller.CanOnExamAsync`, entities.
- Produces: `LiveAttemptMessage(int AttemptId, int ExamId, int StudentId, AttemptStatus Status, DateTime DeadlineAt, int Strikes, AttemptEventType? EventType)`; `LiveMessages` constants `AttemptStarted, EventRecorded, CodeSaved, AttemptSubmitted, DeadlineChanged, AttemptReopened, AttemptVoided` and `All`; hub methods `JoinExam(int examId)`, `LeaveExam(int examId)`; `LiveNotifier` methods `AttemptStartedAsync(Attempt)`, `EventRecordedAsync(Attempt, AttemptEventType)`, `CodeSavedAsync(Attempt)`, `AttemptSubmittedAsync(Attempt)`, `DeadlineChangedAsync(Attempt)`, `AttemptReopenedAsync(Attempt)`, `AttemptVoidedAsync(Attempt)`; test `LiveClient.ConnectAsync(ApiFixture, string token)`, `JoinExamAsync`, `WaitForAsync(string method, Func<LiveAttemptMessage,bool>?)`.

- [ ] **Step 1: Write the test client and failing tests**

`server/Secucode.Api.Tests/Infrastructure/LiveClient.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Contracts;

namespace Secucode.Api.Tests.Infrastructure;

public sealed class LiveClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ConcurrentQueue<(string Method, LiveAttemptMessage Message)> _received = new();

    private LiveClient(HubConnection connection)
    {
        _connection = connection;
        foreach (var method in LiveMessages.All)
            _connection.On<LiveAttemptMessage>(method, m => _received.Enqueue((method, m)));
    }

    public static async Task<LiveClient> ConnectAsync(ApiFixture api, string accessToken)
    {
        var server = api.Factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/exams"), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        var client = new LiveClient(connection);
        await connection.StartAsync();
        return client;
    }

    public Task JoinExamAsync(int examId) => _connection.InvokeAsync("JoinExam", examId);

    public async Task<LiveAttemptMessage> WaitForAsync(string method, Func<LiveAttemptMessage, bool>? match = null, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            foreach (var (m, message) in _received)
                if (m == method && (match?.Invoke(message) ?? true))
                    return message;
            await Task.Delay(50);
        }
        throw new TimeoutException($"No {method} message within {timeoutMs} ms.");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
```

`server/Secucode.Api.Tests/LiveHubTests.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Live;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class LiveHubTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private LiveNotifier Notifier => api.Factory.Services.GetRequiredService<LiveNotifier>();

    [Fact]
    public async Task Exam_staff_receive_live_messages_and_outsiders_cannot_join()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, a.Department);
        var outsider = await _world.UserAsync(RoleNames.Faculty, b.Department);
        var exam = await _world.ExamAsync(author.Id, a.Batch);

        await using var staff = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(author)).AccessToken);
        await staff.JoinExamAsync(exam.Id);
        await Notifier.AttemptStartedAsync(new Attempt { Id = 987654, ExamId = exam.Id, StudentId = 1, Status = AttemptStatus.InProgress, DeadlineAt = _world.Now });
        Assert.Equal(exam.Id, (await staff.WaitForAsync(LiveMessages.AttemptStarted, m => m.AttemptId == 987654)).ExamId);

        await using var other = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(outsider)).AccessToken);
        var ex = await Assert.ThrowsAsync<HubException>(() => other.JoinExamAsync(exam.Id));
        Assert.Contains("can't monitor", ex.Message);
    }

    [Fact]
    public async Task Students_receive_changes_to_their_own_attempts()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch);

        await using var live = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(student)).AccessToken);
        await Notifier.DeadlineChangedAsync(new Attempt { Id = 123456, ExamId = exam.Id, StudentId = student.Id, Status = AttemptStatus.InProgress, DeadlineAt = _world.Now.AddMinutes(45) });

        var msg = await live.WaitForAsync(LiveMessages.DeadlineChanged, m => m.AttemptId == 123456);
        Assert.Equal(student.Id, msg.StudentId);
    }

    [Fact]
    public async Task The_hub_requires_a_token()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => LiveClient.ConnectAsync(api, ""));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter LiveHubTests`
Expected: build fails — `LiveMessages`, `LiveNotifier` don't exist.

- [ ] **Step 3: Add the contracts**

`server/Secucode.Contracts/LiveContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record LiveAttemptMessage(int AttemptId, int ExamId, int StudentId, AttemptStatus Status, DateTime DeadlineAt, int Strikes,
    AttemptEventType? EventType);

// SignalR method names pushed by /hubs/exams.
public static class LiveMessages
{
    public const string AttemptStarted = "AttemptStarted";
    public const string EventRecorded = "EventRecorded";
    public const string CodeSaved = "CodeSaved";
    public const string AttemptSubmitted = "AttemptSubmitted";
    public const string DeadlineChanged = "DeadlineChanged";
    public const string AttemptReopened = "AttemptReopened";
    public const string AttemptVoided = "AttemptVoided";

    public static readonly string[] All = [AttemptStarted, EventRecorded, CodeSaved, AttemptSubmitted, DeadlineChanged, AttemptReopened, AttemptVoided];
}
```

- [ ] **Step 4: Add the hub and notifier**

`server/Secucode.Api/Features/Live/ExamHub.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Data;
using Secucode.Contracts;

namespace Secucode.Api.Features.Live;

public static class LiveGroups
{
    public static string Exam(int examId) => $"exam-{examId}";
    public static string User(int userId) => $"user-{userId}";
}

// Authentication comes from the fallback policy on the mapped hub (Task 3).
public sealed class ExamHub(AppDbContext db, Caller caller) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Everyone joins a private group so their own attempt changes (deadline, reopen, void) reach them.
        if (int.TryParse(Context.User?.FindFirstValue("sub"), out var userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.User(userId));
        await base.OnConnectedAsync();
    }

    public async Task JoinExam(int examId)
    {
        caller.Principal = Context.User!;
        var exam = await db.Exams.AsNoTracking().SingleOrDefaultAsync(e => e.Id == examId) ?? throw new HubException("Exam not found.");
        if (!await caller.CanOnExamAsync(Capability.ViewResults, exam))
            throw new HubException("You can't monitor this exam.");
        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Exam(examId));
    }

    public Task LeaveExam(int examId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroups.Exam(examId));
}
```

`server/Secucode.Api/Features/Live/LiveNotifier.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Live;

// Call only after SaveChanges succeeds, so clients never hear about changes that rolled back.
public sealed class LiveNotifier(IHubContext<ExamHub> hub)
{
    public Task AttemptStartedAsync(Attempt a) => ToExam(LiveMessages.AttemptStarted, a, null);
    public Task EventRecordedAsync(Attempt a, AttemptEventType type) => ToExam(LiveMessages.EventRecorded, a, type);
    public Task CodeSavedAsync(Attempt a) => ToExam(LiveMessages.CodeSaved, a, null);
    public Task AttemptSubmittedAsync(Attempt a) => ToExam(LiveMessages.AttemptSubmitted, a, null);
    public Task DeadlineChangedAsync(Attempt a) => ToExamAndStudent(LiveMessages.DeadlineChanged, a);
    public Task AttemptReopenedAsync(Attempt a) => ToExamAndStudent(LiveMessages.AttemptReopened, a);
    public Task AttemptVoidedAsync(Attempt a) => ToExamAndStudent(LiveMessages.AttemptVoided, a);

    private static LiveAttemptMessage Message(Attempt a, AttemptEventType? type) =>
        new(a.Id, a.ExamId, a.StudentId, a.Status, a.DeadlineAt, a.Strikes, type);

    private Task ToExam(string method, Attempt a, AttemptEventType? type) =>
        hub.Clients.Group(LiveGroups.Exam(a.ExamId)).SendAsync(method, Message(a, type));

    private Task ToExamAndStudent(string method, Attempt a) =>
        hub.Clients.Groups(LiveGroups.Exam(a.ExamId), LiveGroups.User(a.StudentId)).SendAsync(method, Message(a, null));
}
```

In `ServiceSetup.AddSecucodeApi`, add before `return services;`:

```csharp
        services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddSingleton<LiveNotifier>();
```

and `using Secucode.Api.Features.Live;`.
In `Endpoints.MapSecucodeEndpoints`, add at the end `app.MapHub<ExamHub>("/hubs/exams");` with `using Secucode.Api.Features.Live;`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter LiveHubTests`
Expected: 3 passed.

- [ ] **Step 6: Commit**

```bash
git add -A server
git commit -m "feat(api): live exam hub with staff and per-student groups"
```

---

### Task 12: Student attempts and evidence

The server-authoritative attempt lifecycle (spec §8 steps 1–3, 5): start with PIN, eligibility, mode rule and capped deadline; autosave; proctoring events and strikes; evidence upload/download; submit with grace; staff listing.

**Files:**
- Create: `server/Secucode.Contracts/AttemptContracts.cs`
- Create: `server/Secucode.Api/Features/Attempts/AttemptMapping.cs`, `Features/Attempts/EvidenceStore.cs`, `Features/Attempts/AttemptEndpoints.cs`
- Modify: `server/Secucode.Api/ServiceSetup.cs`, `server/Secucode.Api/Endpoints.cs`
- Test: `server/Secucode.Api.Tests/AttemptTests.cs`

**Interfaces:**
- Consumes: `Caller`, `LiveNotifier`, entities.
- Produces: `StartAttemptRequest(string Pin)`, `AttemptDto(int Id, int ExamId, int StudentId, int AttemptNumber, string QuestionText, AttemptStatus Status, ClientKind Client, DateTime StartedAt, DateTime DeadlineAt, DateTime? SubmittedAt, string? Language, string Code, int Strikes, string? SubmitReason, DateTime ServerTime)`, `AttemptEventDto(long Id, AttemptEventType Type, DateTime OccurredAt, string? Detail, long? EvidenceId)`, `EvidenceDto(long Id, EvidenceKind Kind, string ContentType, long SizeBytes, DateTime CreatedAt)`, `AttemptDetailDto(AttemptDto Attempt, string StudentName, IReadOnlyList<AttemptEventDto> Events, IReadOnlyList<EvidenceDto> Evidence)`, `AttemptSummaryDto(int Id, int StudentId, string StudentName, int AttemptNumber, AttemptStatus Status, ClientKind Client, DateTime StartedAt, DateTime DeadlineAt, DateTime? SubmittedAt, int Strikes)`, `SaveCodeRequest(string Code, string Language)`, `AttemptEventRequest(AttemptEventType Type, string? Detail, long? EvidenceId)`, `SubmitRequest(string Code, string Language, string? Reason)`; `AttemptEndpoints.SubmitGrace` (30 s); `AttemptMapping.ToDto(this Attempt, DateTime serverTime)`, `.ToDto(this AttemptEvent)`, `.ToDto(this EvidenceFile)`; `EvidenceOptions.Root`, `EvidenceStore.SaveAsync(int examId, int attemptId, Stream, string extension, CancellationToken) → string relativePath`, `EvidenceStore.OpenRead(string)`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/AttemptTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AttemptTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private sealed record Setup(Section Section, TestUser Author, TestUser Student, int ExamId);

    private async Task<Setup> SetupAsync(ExamMode mode = ExamMode.WebAllowed, TimeSpan? startsIn = null, TimeSpan? window = null, int durationMinutes = 30)
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Year, mode, startsIn, window, durationMinutes);
        return new Setup(s, author, student, exam.Id);
    }

    private static Task<HttpResponseMessage> Start(HttpClient c, int examId, string pin = World.Pin) =>
        c.PostJsonAsync($"/api/exams/{examId}/attempts", new StartAttemptRequest(pin));

    [Fact]
    public async Task Starting_snapshots_a_question_and_caps_the_deadline_at_the_window_end()
    {
        var x = await SetupAsync(window: TimeSpan.FromMinutes(15), durationMinutes: 60); // window ends 10 minutes from now
        var student = await _world.LoginAsync(x.Student, ClientKind.Desktop);

        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        Assert.Contains(attempt.QuestionText, new[] { "Question 1", "Question 2" });
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.Equal(ClientKind.Desktop, attempt.Client);
        Assert.Equal(_world.Now.AddMinutes(10), attempt.DeadlineAt, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_wrong_pin_or_a_staff_account_cannot_start()
    {
        var x = await SetupAsync();
        await (await Start(await _world.LoginAsync(x.Student), x.ExamId, pin: "0000")).ExpectProblemAsync(HttpStatusCode.Forbidden);
        Assert.Contains("Only students", await (await Start(await _world.LoginAsync(x.Author), x.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Students_outside_the_target_get_404()
    {
        var x = await SetupAsync();
        var other = await _world.SectionAsync();
        var outsider = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, other.Batch));
        await (await Start(outsider, x.ExamId)).ExpectProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_exam_window_and_active_flag_are_enforced()
    {
        var early = await SetupAsync(startsIn: TimeSpan.FromHours(1));
        Assert.Contains("hasn't started", await (await Start(await _world.LoginAsync(early.Student), early.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden));

        var late = await SetupAsync(startsIn: TimeSpan.FromHours(-3), window: TimeSpan.FromHours(1));
        Assert.Contains("ended", await (await Start(await _world.LoginAsync(late.Student), late.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden));

        var off = await SetupAsync();
        await _world.WithDb(db => db.Exams.Where(e => e.Id == off.ExamId).ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, false)));
        await (await Start(await _world.LoginAsync(off.Student), off.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Desktop_only_exams_reject_the_web_client()
    {
        var x = await SetupAsync(ExamMode.DesktopOnly);
        var web = await (await Start(await _world.LoginAsync(x.Student, ClientKind.Web), x.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden);
        Assert.Contains("desktop app", web);
        await (await Start(await _world.LoginAsync(x.Student, ClientKind.Desktop), x.ExamId)).ExpectAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Only_one_attempt_at_a_time_even_when_requests_race()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);

        var results = await Task.WhenAll(Start(student, x.ExamId), Start(student, x.ExamId));
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).OrderBy(c => c));
        await (await Start(student, x.ExamId)).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Autosave_events_and_submit_follow_the_server_clock()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        await (await student.PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("print('नमस्ते 🙂')", "Python"))).ExpectAsync(HttpStatusCode.NoContent);
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/events", new AttemptEventRequest(AttemptEventType.FaceMissing, null, null))).ExpectAsync(HttpStatusCode.Created);
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/events", new AttemptEventRequest(AttemptEventType.AudioUnavailable, "no mic", null))).ExpectAsync(HttpStatusCode.Created);

        var detail = await (await student.GetAsync($"/api/attempts/{attempt.Id}")).ReadAsync<AttemptDetailDto>();
        Assert.Equal("print('नमस्ते 🙂')", detail.Attempt.Code); // utf8mb4 round-trip
        Assert.Equal(1, detail.Attempt.Strikes);                  // AudioUnavailable isn't a strike
        Assert.Equal(2, detail.Events.Count);

        api.Time.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(20)); // past the deadline, inside the 30 s grace
        student = await _world.LoginAsync(x.Student);                           // the 15-minute access token expired meanwhile
        var submitted = await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/submit", new SubmitRequest("print(2)", "Python", null))).ReadAsync<AttemptDto>();
        Assert.Equal(AttemptStatus.Submitted, submitted.Status);

        await (await student.PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("x", "Python"))).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submitting_after_the_grace_period_is_rejected()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        api.Time.Advance(TimeSpan.FromMinutes(31));
        student = await _world.LoginAsync(x.Student); // fresh token; the old one expired
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/submit", new SubmitRequest("x", "Python", null))).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_student_deactivated_mid_exam_can_no_longer_write()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        await _world.WithDb(db => db.Users.Where(u => u.Id == x.Student.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

        await (await student.PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("x", "Python"))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/submit", new SubmitRequest("x", "Python", null))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_exam_whose_questions_were_all_removed_cannot_be_started()
    {
        var x = await SetupAsync();
        await _world.WithDb(db => db.Questions.Where(q => q.ExamId == x.ExamId).ExecuteUpdateAsync(s => s.SetProperty(q => q.IsDeleted, true)));
        await (await Start(await _world.LoginAsync(x.Student), x.ExamId)).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Evidence_is_validated_stored_and_served_only_to_the_right_people()
    {
        var x = await SetupAsync();
        var studentClient = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(studentClient, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        var face = await (await studentClient.PostAsync($"/api/attempts/{attempt.Id}/evidence", Upload("ReferenceFace", png, "image/png"))).ReadAsync<EvidenceDto>(HttpStatusCode.Created);
        var webcam = await (await studentClient.PostAsync($"/api/attempts/{attempt.Id}/evidence", Upload("Webcam", png, "image/png"))).ReadAsync<EvidenceDto>(HttpStatusCode.Created);
        await (await studentClient.PostAsync($"/api/attempts/{attempt.Id}/evidence", Upload("Webcam", png, "application/pdf"))).ValidationFieldsAsync();
        await (await studentClient.PostAsync($"/api/attempts/{attempt.Id}/evidence", Upload("Webcam", new byte[10 * 1024 * 1024 + 1], "image/png"))).ValidationFieldsAsync();

        var author = await _world.LoginAsync(x.Author);
        Assert.Equal(png, await (await author.GetAsync($"/api/evidence/{webcam.Id}")).Content.ReadAsByteArrayAsync());

        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, x.Section.Department));
        await (await colleague.GetAsync($"/api/evidence/{webcam.Id}")).ExpectProblemAsync(HttpStatusCode.NotFound);

        await (await studentClient.GetAsync($"/api/evidence/{face.Id}")).ExpectAsync(HttpStatusCode.OK);
        await (await studentClient.GetAsync($"/api/evidence/{webcam.Id}")).ExpectProblemAsync(HttpStatusCode.NotFound);

        static MultipartFormDataContent Upload(string kind, byte[] bytes, string contentType)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return new MultipartFormDataContent { { new StringContent(kind), "kind" }, { file, "file", "capture.bin" } };
        }
    }

    [Fact]
    public async Task Staff_with_ViewResults_list_attempts_and_watch_them_start_live()
    {
        var x = await SetupAsync();
        var authorLogin = await _world.LoginResponseAsync(x.Author);
        await using var live = await LiveClient.ConnectAsync(api, authorLogin.AccessToken);
        await live.JoinExamAsync(x.ExamId);

        var attempt = await (await Start(await _world.LoginAsync(x.Student), x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);
        await live.WaitForAsync(LiveMessages.AttemptStarted, m => m.AttemptId == attempt.Id);

        var list = await (await _world.Client(authorLogin.AccessToken).GetAsync($"/api/exams/{x.ExamId}/attempts")).ReadAsync<List<AttemptSummaryDto>>();
        Assert.Equal(attempt.Id, Assert.Single(list).Id);

        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, x.Section.Department));
        await (await colleague.GetAsync($"/api/exams/{x.ExamId}/attempts")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter AttemptTests`
Expected: build fails — `StartAttemptRequest` doesn't exist.

- [ ] **Step 3: Add the contracts**

`server/Secucode.Contracts/AttemptContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record StartAttemptRequest(string Pin);

public sealed record AttemptDto(int Id, int ExamId, int StudentId, int AttemptNumber, string QuestionText, AttemptStatus Status, ClientKind Client,
    DateTime StartedAt, DateTime DeadlineAt, DateTime? SubmittedAt, string? Language, string Code, int Strikes, string? SubmitReason,
    DateTime ServerTime);

public sealed record AttemptEventDto(long Id, AttemptEventType Type, DateTime OccurredAt, string? Detail, long? EvidenceId);

public sealed record EvidenceDto(long Id, EvidenceKind Kind, string ContentType, long SizeBytes, DateTime CreatedAt);

public sealed record AttemptDetailDto(AttemptDto Attempt, string StudentName, IReadOnlyList<AttemptEventDto> Events, IReadOnlyList<EvidenceDto> Evidence);

public sealed record AttemptSummaryDto(int Id, int StudentId, string StudentName, int AttemptNumber, AttemptStatus Status, ClientKind Client,
    DateTime StartedAt, DateTime DeadlineAt, DateTime? SubmittedAt, int Strikes);

public sealed record SaveCodeRequest(string Code, string Language);

public sealed record AttemptEventRequest(AttemptEventType Type, string? Detail, long? EvidenceId);

public sealed record SubmitRequest(string Code, string Language, string? Reason);
```

- [ ] **Step 4: Add mapping and evidence storage**

`server/Secucode.Api/Features/Attempts/AttemptMapping.cs`:

```csharp
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Attempts;

public static class AttemptMapping
{
    public static AttemptDto ToDto(this Attempt a, DateTime serverTime) => new(a.Id, a.ExamId, a.StudentId, a.AttemptNumber, a.QuestionText,
        a.Status, a.Client, a.StartedAt, a.DeadlineAt, a.SubmittedAt, a.Language, a.Code, a.Strikes, a.SubmitReason, serverTime);

    public static AttemptEventDto ToDto(this AttemptEvent e) => new(e.Id, e.Type, e.OccurredAt, e.Detail, e.EvidenceId);

    public static EvidenceDto ToDto(this EvidenceFile f) => new(f.Id, f.Kind, f.ContentType, f.SizeBytes, f.CreatedAt);
}
```

`server/Secucode.Api/Features/Attempts/EvidenceStore.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Secucode.Api.Features.Attempts;

public sealed class EvidenceOptions
{
    public string Root { get; set; } = "evidence"; // relative paths resolve against the app's content root
}

// ponytail: local disk volume; swap for blob storage (S3/Azure) when the API runs on more than one machine.
public sealed class EvidenceStore(IOptions<EvidenceOptions> options, IHostEnvironment env)
{
    private string Root => Path.GetFullPath(options.Value.Root, env.ContentRootPath);

    // File names are always server-generated; nothing from the request reaches the path.
    public async Task<string> SaveAsync(int examId, int attemptId, Stream content, string extension, CancellationToken ct)
    {
        var relative = Path.Combine(examId.ToString(), attemptId.ToString(), $"{Guid.NewGuid():N}{extension}");
        var full = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);
        return relative.Replace('\\', '/');
    }

    public Stream OpenRead(string relativePath) => File.OpenRead(Path.Combine(Root, relativePath));
}
```

- [ ] **Step 5: Add the attempt endpoints**

`server/Secucode.Api/Features/Attempts/AttemptEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Live;
using Secucode.Contracts;

namespace Secucode.Api.Features.Attempts;

public static class AttemptEndpoints
{
    public static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);
    private const long MaxEvidenceBytes = 10 * 1024 * 1024;
    private const int MaxCodeChars = 256 * 1024;

    private static readonly HashSet<AttemptEventType> StrikeTypes =
    [
        AttemptEventType.FaceMissing, AttemptEventType.MultipleFaces, AttemptEventType.FaceMismatch, AttemptEventType.FocusLost,
        AttemptEventType.Minimized, AttemptEventType.TabHidden, AttemptEventType.FullscreenExited,
    ];

    private static readonly Dictionary<string, string> EvidenceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["audio/wav"] = ".wav", ["audio/x-wav"] = ".wav",
    };

    public static void MapAttempts(this IEndpointRouteBuilder api)
    {
        api.MapPost("/exams/{examId:int}/attempts", StartAsync);
        api.MapGet("/exams/{examId:int}/attempts", ListForExamAsync);
        var g = api.MapGroup("/attempts/{id:int}");
        g.MapGet("", GetAsync);
        g.MapPut("/code", SaveCodeAsync);
        g.MapPost("/events", RecordEventAsync);
        g.MapPost("/evidence", UploadEvidenceAsync).DisableAntiforgery(); // bearer-token API: no cookies to forge
        g.MapPost("/submit", SubmitAsync);
        api.MapGet("/evidence/{id:long}", DownloadEvidenceAsync);
    }

    // Spec §8 step 1, checks in the spec's order.
    private static async Task<IResult> StartAsync(int examId, StartAttemptRequest req, AppDbContext db, Caller caller, TimeProvider time, LiveNotifier live)
    {
        var me = await ActiveUserAsync(db, caller);
        var scope = await caller.StudentScopeAsync() ?? throw ApiException.Forbidden("Only students can take exams.");
        if (me.PinHash is null || string.IsNullOrEmpty(req.Pin) || !BCrypt.Net.BCrypt.Verify(req.Pin, me.PinHash))
            throw ApiException.Forbidden("Incorrect PIN.");

        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == examId);
        if (exam is null || !exam.Target.Contains(scope))
            throw ApiException.NotFound("Exam");
        if (!exam.IsActive)
            throw ApiException.Forbidden("This exam is not active.");

        var now = time.UtcNow();
        var mine = await db.Attempts.Where(a => a.ExamId == examId && a.StudentId == me.Id).OrderBy(a => a.AttemptNumber).ToListAsync();
        var current = mine.LastOrDefault(a => a.Status != AttemptStatus.Voided);
        if (current is not null)
            throw ApiException.Conflict(current.Status == AttemptStatus.InProgress
                ? $"You already have an attempt in progress (id {current.Id})."
                : "You have already submitted this exam.");

        var allowance = mine.LastOrDefault() is { Status: AttemptStatus.Voided, AllowanceMinutes: int extra } ? extra : (int?)null;
        var windowOpen = now >= exam.StartsAt && now < exam.EndsAt;
        if (!windowOpen && allowance is null)
            throw ApiException.Forbidden(now < exam.StartsAt ? "This exam hasn't started yet." : "This exam has ended.");
        if (caller.Client == ClientKind.Web && exam.Mode == ExamMode.DesktopOnly)
            throw ApiException.Forbidden("This exam can only be taken in the SecuCode desktop app.");

        var questions = exam.Questions.Where(q => !q.IsDeleted).ToList();
        if (questions.Count == 0)
            throw ApiException.Conflict("This exam has no questions yet.");
        var question = questions[Random.Shared.Next(questions.Count)];

        var normalDeadline = now.AddMinutes(exam.DurationMinutes);
        var attempt = new Attempt
        {
            ExamId = examId,
            StudentId = me.Id,
            AttemptNumber = mine.Count + 1, // unique index turns a racing duplicate into a 409
            QuestionId = question.Id,
            QuestionText = question.Text,
            Client = caller.Client,
            StartedAt = now,
            DeadlineAt = windowOpen ? (normalDeadline < exam.EndsAt ? normalDeadline : exam.EndsAt) : now.AddMinutes(allowance!.Value),
            Status = AttemptStatus.InProgress,
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        await live.AttemptStartedAsync(attempt);
        return Results.Created($"/api/attempts/{attempt.Id}", attempt.ToDto(now));
    }

    private static async Task<AttemptDetailDto> GetAsync(int id, AppDbContext db, Caller caller, TimeProvider time)
    {
        var a = await db.Attempts.Include(x => x.Exam).Include(x => x.Student).Include(x => x.Events).Include(x => x.Evidence)
            .AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Attempt");
        if (a.StudentId != caller.UserId && !await caller.CanOnExamAsync(Capability.ViewResults, a.Exam))
            throw ApiException.NotFound("Attempt");
        return new AttemptDetailDto(a.ToDto(time.UtcNow()), a.Student.FullName,
            a.Events.OrderBy(e => e.OccurredAt).Select(e => e.ToDto()).ToList(),
            a.Evidence.OrderBy(e => e.CreatedAt).Select(e => e.ToDto()).ToList());
    }

    private static async Task<IResult> SaveCodeAsync(int id, SaveCodeRequest req, AppDbContext db, Caller caller, TimeProvider time, LiveNotifier live)
    {
        var a = await WritableAsync(db, caller, id, time);
        ValidateCode(req.Code, req.Language);
        a.Code = req.Code;
        a.Language = req.Language;
        a.LastSavedAt = time.UtcNow();
        await db.SaveChangesAsync();
        await live.CodeSavedAsync(a);
        return Results.NoContent();
    }

    private static async Task<IResult> RecordEventAsync(int id, AttemptEventRequest req, AppDbContext db, Caller caller, TimeProvider time, LiveNotifier live)
    {
        var a = await WritableAsync(db, caller, id, time);
        new Validator()
            .Rule(Enum.IsDefined(req.Type), "type", "Unknown event type.")
            .Rule((req.Detail?.Length ?? 0) <= 500, "detail", "Detail can be up to 500 characters.")
            .Rule(req.EvidenceId is null || await db.EvidenceFiles.AnyAsync(f => f.Id == req.EvidenceId && f.AttemptId == id), "evidenceId", "That evidence doesn't belong to this attempt.")
            .ThrowIfInvalid();

        var e = new AttemptEvent { AttemptId = id, Type = req.Type, OccurredAt = time.UtcNow(), Detail = req.Detail, EvidenceId = req.EvidenceId };
        db.AttemptEvents.Add(e);
        if (StrikeTypes.Contains(req.Type))
            a.Strikes++;
        await db.SaveChangesAsync();
        await live.EventRecordedAsync(a, req.Type);
        return Results.Created($"/api/attempts/{id}", e.ToDto());
    }

    private static async Task<IResult> UploadEvidenceAsync(int id, [FromForm] string kind, IFormFile file, AppDbContext db, Caller caller,
        TimeProvider time, EvidenceStore store, CancellationToken ct)
    {
        var a = await WritableAsync(db, caller, id, time);
        var extension = EvidenceTypes.GetValueOrDefault(file.ContentType ?? "");
        new Validator()
            .Rule(Enum.TryParse<EvidenceKind>(kind, ignoreCase: true, out _), "kind", "Kind must be ReferenceFace, Webcam, Screenshot or Audio.")
            .Rule(file.Length is > 0 and <= MaxEvidenceBytes, "file", "File must be between 1 byte and 10 MB.")
            .Rule(extension is not null, "file", "Only JPEG, PNG and WAV files are accepted.")
            .ThrowIfInvalid();

        await using var content = file.OpenReadStream();
        var path = await store.SaveAsync(a.ExamId, a.Id, content, extension!, ct);
        var evidence = new EvidenceFile
        {
            AttemptId = a.Id, Kind = Enum.Parse<EvidenceKind>(kind, ignoreCase: true), StoragePath = path,
            ContentType = file.ContentType!, SizeBytes = file.Length, CreatedAt = time.UtcNow(),
        };
        db.EvidenceFiles.Add(evidence);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/evidence/{evidence.Id}", evidence.ToDto());
    }

    private static async Task<AttemptDto> SubmitAsync(int id, SubmitRequest req, AppDbContext db, Caller caller, TimeProvider time, LiveNotifier live)
    {
        var a = await WritableAsync(db, caller, id, time);
        ValidateCode(req.Code, req.Language);
        var now = time.UtcNow();
        a.Code = req.Code;
        a.Language = req.Language;
        a.LastSavedAt = now;
        a.Status = AttemptStatus.Submitted;
        a.SubmittedAt = now;
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? "Submitted by the student." : req.Reason.Trim();
        a.SubmitReason = reason.Length > 255 ? reason[..255] : reason;
        await db.SaveChangesAsync();
        await live.AttemptSubmittedAsync(a);
        return a.ToDto(now);
    }

    private static async Task<List<AttemptSummaryDto>> ListForExamAsync(int examId, AppDbContext db, Caller caller)
    {
        var exam = await db.Exams.AsNoTracking().SingleOrDefaultAsync(e => e.Id == examId) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ViewResults, exam, "You can't view results for this exam.");
        return await db.Attempts.Where(a => a.ExamId == examId).OrderBy(a => a.StartedAt)
            .Select(a => new AttemptSummaryDto(a.Id, a.StudentId, a.Student.FullName, a.AttemptNumber, a.Status, a.Client,
                a.StartedAt, a.DeadlineAt, a.SubmittedAt, a.Strikes))
            .ToListAsync();
    }

    private static async Task<IResult> DownloadEvidenceAsync(long id, AppDbContext db, Caller caller, EvidenceStore store)
    {
        var file = await db.EvidenceFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id) ?? throw ApiException.NotFound("Evidence");
        var attempt = await db.Attempts.Include(a => a.Exam).AsNoTracking().SingleAsync(a => a.Id == file.AttemptId);

        var ownReferenceFace = attempt.StudentId == caller.UserId && file.Kind == EvidenceKind.ReferenceFace;
        if (!ownReferenceFace && !await caller.CanOnExamAsync(Capability.ViewEvidence, attempt.Exam))
            throw ApiException.NotFound("Evidence");
        return Results.File(store.OpenRead(file.StoragePath), file.ContentType);
    }

    // A deactivated student keeps a valid access token for up to 15 minutes; stop them here.
    private static async Task<User> ActiveUserAsync(AppDbContext db, Caller caller)
    {
        var userId = caller.UserId;
        return await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw ApiException.Unauthorized("This account is deactivated.");
    }

    private static async Task<Attempt> WritableAsync(AppDbContext db, Caller caller, int id, TimeProvider time)
    {
        await ActiveUserAsync(db, caller);
        var a = await db.Attempts.SingleOrDefaultAsync(x => x.Id == id);
        if (a is null || a.StudentId != caller.UserId)
            throw ApiException.NotFound("Attempt");
        if (a.Status != AttemptStatus.InProgress)
            throw ApiException.Conflict("This attempt is closed.");
        if (time.UtcNow() > a.DeadlineAt + SubmitGrace)
            throw ApiException.Conflict("Time is up for this attempt.");
        return a;
    }

    private static void ValidateCode(string? code, string? language) => new Validator()
        .Rule(code is not null && code.Length <= MaxCodeChars, "code", "Code is required and must be under 256 KB.")
        .Rule(!string.IsNullOrWhiteSpace(language) && language.Length <= 20, "language", "Language is required.")
        .ThrowIfInvalid();
}
```

In `ServiceSetup.AddSecucodeApi`, add before `return services;`:

```csharp
        services.Configure<EvidenceOptions>(config.GetSection("Evidence"));
        services.AddSingleton<EvidenceStore>();
```

with `using Secucode.Api.Features.Attempts;`. In `Endpoints.MapSecucodeEndpoints` add `api.MapAttempts();` with the same using.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter AttemptTests`
Expected: 12 passed.

- [ ] **Step 7: Commit**

```bash
git add -A server
git commit -m "feat(api): server-authoritative exam attempts with evidence and live updates"
```

---
### Task 13: Auto-close expired attempts

Background service that auto-submits attempts past `deadline_at + 30 s` with their last saved code (spec §8 step 4).

**Files:**
- Create: `server/Secucode.Api/Features/Attempts/AutoCloseService.cs`
- Modify: `server/Secucode.Api/ServiceSetup.cs`
- Test: `server/Secucode.Api.Tests/AutoCloseTests.cs`

**Interfaces:**
- Consumes: `AttemptEndpoints.SubmitGrace`, `LiveNotifier.AttemptSubmittedAsync`.
- Produces: `AttemptOptions` (`AutoCloseEnabled`, `AutoCloseIntervalSeconds`), `AutoCloseService.CloseExpiredAsync(CancellationToken) → Task<int>` (registered as a singleton and as the hosted service).

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/AutoCloseTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Features.Attempts;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

// The hosted loop is disabled in tests (Attempts:AutoCloseEnabled=false); tests call the sweep directly.
[Collection(ApiCollection.Name)]
public sealed class AutoCloseTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private AutoCloseService Service => api.Factory.Services.GetRequiredService<AutoCloseService>();

    [Fact]
    public async Task Expired_attempts_are_auto_submitted_with_their_last_saved_code()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var expiring = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, deadlineIn: TimeSpan.FromMinutes(1));
        var running = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, deadlineIn: TimeSpan.FromMinutes(60));
        var done = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, AttemptStatus.Submitted, deadlineIn: TimeSpan.FromMinutes(-10));
        await using var live = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(author)).AccessToken);
        await live.JoinExamAsync(exam.Id);

        api.Time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(31));
        Assert.True(await Service.CloseExpiredAsync(CancellationToken.None) >= 1);

        var closed = await _world.ReloadAttemptAsync(expiring.Id);
        Assert.Equal(AttemptStatus.AutoSubmitted, closed.Status);
        Assert.Equal("Deadline passed", closed.SubmitReason);
        Assert.Equal("print(1)", closed.Code);
        Assert.NotNull(closed.SubmittedAt);
        Assert.Equal(AttemptStatus.InProgress, (await _world.ReloadAttemptAsync(running.Id)).Status);
        Assert.Equal(AttemptStatus.Submitted, (await _world.ReloadAttemptAsync(done.Id)).Status);
        await live.WaitForAsync(LiveMessages.AttemptSubmitted, m => m.AttemptId == expiring.Id);
    }

    [Fact]
    public async Task Attempts_inside_the_grace_period_stay_open()
    {
        var s = await _world.SectionAsync();
        var exam = await _world.ExamAsync((await _world.UserAsync(RoleNames.Faculty, s.Department)).Id, s.Batch);
        var attempt = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, deadlineIn: TimeSpan.FromMinutes(1));

        api.Time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(10));
        await Service.CloseExpiredAsync(CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, (await _world.ReloadAttemptAsync(attempt.Id)).Status);
    }

    [Fact]
    public async Task The_current_deadline_is_what_counts()
    {
        var s = await _world.SectionAsync();
        var exam = await _world.ExamAsync((await _world.UserAsync(RoleNames.Faculty, s.Department)).Id, s.Batch);
        var attempt = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, deadlineIn: TimeSpan.FromMinutes(1));
        await _world.WithDb(db => db.Attempts.Where(a => a.Id == attempt.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.DeadlineAt, a => a.DeadlineAt.AddMinutes(30))));

        api.Time.Advance(TimeSpan.FromMinutes(5));
        await Service.CloseExpiredAsync(CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, (await _world.ReloadAttemptAsync(attempt.Id)).Status);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter AutoCloseTests`
Expected: build fails — `AutoCloseService` doesn't exist.

- [ ] **Step 3: Add the service**

`server/Secucode.Api/Features/Attempts/AutoCloseService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Features.Live;
using Secucode.Contracts;

namespace Secucode.Api.Features.Attempts;

public sealed class AttemptOptions
{
    public bool AutoCloseEnabled { get; set; } = true;
    public int AutoCloseIntervalSeconds { get; set; } = 15;
}

// A crashed laptop or lost network can't leave an attempt open forever (spec §8 step 4).
public sealed class AutoCloseService(IServiceScopeFactory scopes, TimeProvider time, IOptions<AttemptOptions> options,
    LiveNotifier live, ILogger<AutoCloseService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AutoCloseEnabled)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.AutoCloseIntervalSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CloseExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Auto-closing expired attempts failed; will retry on the next tick.");
            }
        }
    }

    public async Task<int> CloseExpiredAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.UtcNow();
        var cutoff = now - AttemptEndpoints.SubmitGrace;

        var expired = await db.Attempts.Where(a => a.Status == AttemptStatus.InProgress && a.DeadlineAt < cutoff).ToListAsync(ct);
        foreach (var a in expired)
        {
            a.Status = AttemptStatus.AutoSubmitted;
            a.SubmittedAt = now;
            a.SubmitReason = "Deadline passed";
        }
        await db.SaveChangesAsync(ct);

        foreach (var a in expired)
            await live.AttemptSubmittedAsync(a);
        return expired.Count;
    }
}
```

In `ServiceSetup.AddSecucodeApi`, add before `return services;`:

```csharp
        services.Configure<AttemptOptions>(config.GetSection("Attempts"));
        services.AddSingleton<AutoCloseService>();
        services.AddHostedService(sp => sp.GetRequiredService<AutoCloseService>());
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter AutoCloseTests`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add -A server
git commit -m "feat(api): background auto-close of expired attempts"
```

---

### Task 14: Staff corrections

Reset, reopen and extend (one attempt or a whole exam), each gated by its capability and by the global + per-exam switch, audited with a mandatory reason and pushed live (spec §6).

**Files:**
- Create: `server/Secucode.Contracts/CorrectionContracts.cs`
- Create: `server/Secucode.Api/Features/Corrections/CorrectionEndpoints.cs`
- Modify: `server/Secucode.Api/Endpoints.cs`
- Test: `server/Secucode.Api.Tests/CorrectionTests.cs`

**Interfaces:**
- Consumes: `Caller.RequireOnExamAsync`, `Switches.EnsureOnAsync`, `AuditWriter`, `LiveNotifier`, `AttemptMapping.ToDto`, `AutoCloseService.CloseExpiredAsync` (tests).
- Produces: `ResetAttemptRequest(string Reason, int? ExtraMinutes)`, `ReopenAttemptRequest(string Reason, int ExtraMinutes)`, `ExtendAttemptRequest(int Minutes, string Reason)`, `ExtendExamRequest(int Minutes, string Reason, bool ExtendWindow)`, `ExtendExamResult(int AttemptsExtended, DateTime EndsAt)`; routes `POST /attempts/{id}/reset|reopen|extend`, `POST /exams/{id}/extend`; audit actions `attempt.reset|reopen|extend`, `exam.extend`.

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/CorrectionTests.cs`:

```csharp
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Attempts;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class CorrectionTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private sealed record Setup(Section Section, TestUser Author, TestUser Student, Exam Exam);

    private async Task<Setup> SetupAsync(TimeSpan? startsIn = null, TimeSpan? window = null)
    {
        await _world.SetSwitchesAsync(true);
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch, startsIn: startsIn, window: window);
        return new Setup(s, author, student, exam);
    }

    [Fact]
    public async Task Reset_voids_the_attempt_keeps_its_data_and_lets_the_student_start_again()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.Submitted);
        var author = await _world.LoginAsync(x.Author);

        var voided = await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("Webcam failed", null))).ReadAsync<AttemptDto>();

        Assert.Equal(AttemptStatus.Voided, voided.Status);
        var reloaded = await _world.ReloadAttemptAsync(attempt.Id);
        Assert.Equal(("print(1)", "Webcam failed"), (reloaded.Code, reloaded.VoidReason));
        var audit = await _world.LastAuditAsync();
        Assert.Equal(("attempt.reset", "Webcam failed", (int?)x.Exam.Id), (audit.Action, audit.Reason, audit.ExamId));

        var again = await (await (await _world.LoginAsync(x.Student)).PostJsonAsync($"/api/exams/{x.Exam.Id}/attempts", new StartAttemptRequest(World.Pin)))
            .ReadAsync<AttemptDto>(HttpStatusCode.Created);
        Assert.Equal(2, again.AttemptNumber);
    }

    [Fact]
    public async Task Resetting_after_the_window_needs_extra_minutes_which_become_the_new_time_allowance()
    {
        var x = await SetupAsync(startsIn: TimeSpan.FromHours(-3), window: TimeSpan.FromHours(1));
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.Submitted);
        var author = await _world.LoginAsync(x.Author);

        Assert.Equal(["extraMinutes"], await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("Power cut", null))).ValidationFieldsAsync());
        await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("Power cut", 20))).ExpectAsync(HttpStatusCode.OK);

        var restarted = await (await (await _world.LoginAsync(x.Student)).PostJsonAsync($"/api/exams/{x.Exam.Id}/attempts", new StartAttemptRequest(World.Pin)))
            .ReadAsync<AttemptDto>(HttpStatusCode.Created);
        Assert.Equal(_world.Now.AddMinutes(20), restarted.DeadlineAt, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Reopen_gives_a_submitted_attempt_more_time_and_keeps_the_code()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.AutoSubmitted);
        var author = await _world.LoginAsync(x.Author);

        var reopened = await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reopen", new ReopenAttemptRequest("Laptop crashed", 15))).ReadAsync<AttemptDto>();

        Assert.Equal(AttemptStatus.InProgress, reopened.Status);
        Assert.Equal("print(1)", reopened.Code);
        Assert.Equal(_world.Now.AddMinutes(15), reopened.DeadlineAt, TimeSpan.FromSeconds(2));
        await (await (await _world.LoginAsync(x.Student)).PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("print(2)", "Python")))
            .ExpectAsync(HttpStatusCode.NoContent);
        await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reopen", new ReopenAttemptRequest("Again", 5))).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Extending_one_student_moves_their_deadline_and_tells_them_live()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id);
        await using var studentLive = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(x.Student)).AccessToken);
        var author = await _world.LoginAsync(x.Author);

        var extended = await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(10, "Power cut"))).ReadAsync<AttemptDto>();

        Assert.Equal(attempt.DeadlineAt.AddMinutes(10), extended.DeadlineAt, TimeSpan.FromSeconds(1));
        var msg = await studentLive.WaitForAsync(LiveMessages.DeadlineChanged, m => m.AttemptId == attempt.Id);
        Assert.Equal(extended.DeadlineAt, msg.DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal("attempt.extend", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Extending_the_whole_exam_moves_every_running_attempt_and_optionally_the_window()
    {
        var x = await SetupAsync();
        var a1 = await _world.AttemptAsync(x.Exam.Id, x.Student.Id);
        var a2 = await _world.AttemptAsync(x.Exam.Id, (await _world.UserAsync(RoleNames.Student, x.Section.Batch)).Id);
        var done = await _world.AttemptAsync(x.Exam.Id, (await _world.UserAsync(RoleNames.Student, x.Section.Batch)).Id, AttemptStatus.Submitted);
        var author = await _world.LoginAsync(x.Author);
        var before = await _world.AuditCountAsync();

        var result = await (await author.PostJsonAsync($"/api/exams/{x.Exam.Id}/extend", new ExtendExamRequest(15, "Fire drill", ExtendWindow: true))).ReadAsync<ExtendExamResult>();

        Assert.Equal(2, result.AttemptsExtended);
        Assert.Equal(x.Exam.EndsAt.AddMinutes(15), result.EndsAt, TimeSpan.FromSeconds(1));
        Assert.Equal(a1.DeadlineAt.AddMinutes(15), (await _world.ReloadAttemptAsync(a1.Id)).DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal(a2.DeadlineAt.AddMinutes(15), (await _world.ReloadAttemptAsync(a2.Id)).DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal(done.DeadlineAt, (await _world.ReloadAttemptAsync(done.Id)).DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal(before + 1, await _world.AuditCountAsync());
        Assert.Equal("exam.extend", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Switches_turn_corrections_off_globally_or_for_one_exam()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.Submitted);
        var author = await _world.LoginAsync(x.Author);
        try
        {
            await _world.SetSwitchesAsync(false);
            Assert.Contains("whole institution",
                await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("x", null))).ExpectProblemAsync(HttpStatusCode.Forbidden));

            await _world.SetSwitchesAsync(true);
            await _world.WithDb(db => db.Exams.Where(e => e.Id == x.Exam.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.AllowAttemptReopen, false)));
            Assert.Contains("this exam",
                await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reopen", new ReopenAttemptRequest("x", 5))).ExpectProblemAsync(HttpStatusCode.Forbidden));
        }
        finally
        {
            await _world.SetSwitchesAsync(true);
        }
    }

    [Fact]
    public async Task Corrections_follow_the_capability_grid_even_for_the_exam_creator()
    {
        var x = await SetupAsync();
        var a1 = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.Submitted);
        var a2 = await _world.AttemptAsync(x.Exam.Id, (await _world.UserAsync(RoleNames.Student, x.Section.Batch)).Id, AttemptStatus.Submitted);
        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, x.Section.Department));
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, x.Section.Department));
        var author = await _world.LoginAsync(x.Author);
        var facultyRole = await _world.RoleIdAsync(RoleNames.Faculty);

        await (await colleague.PostJsonAsync($"/api/attempts/{a1.Id}/reset", new ResetAttemptRequest("x", null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.PostJsonAsync($"/api/attempts/{a1.Id}/reset", new ResetAttemptRequest("HOD approved", null))).ExpectAsync(HttpStatusCode.OK);

        try
        {
            await _world.WithDb(db => db.RoleCapabilities.Where(c => c.RoleId == facultyRole && c.Capability == Capability.ResetAttempts).ExecuteDeleteAsync());
            await (await author.PostJsonAsync($"/api/attempts/{a2.Id}/reset", new ResetAttemptRequest("x", null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        }
        finally
        {
            await _world.WithDb(async db =>
            {
                db.RoleCapabilities.Add(new RoleCapability { RoleId = facultyRole, Capability = Capability.ResetAttempts, Reach = Reach.OwnOnly });
                await db.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task Auto_close_respects_an_extension()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, deadlineIn: TimeSpan.FromMinutes(1));
        await (await (await _world.LoginAsync(x.Author)).PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(10, "Late start")))
            .ExpectAsync(HttpStatusCode.OK);

        api.Time.Advance(TimeSpan.FromMinutes(3));
        await api.Factory.Services.GetRequiredService<AutoCloseService>().CloseExpiredAsync(CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, (await _world.ReloadAttemptAsync(attempt.Id)).Status);
    }

    [Fact]
    public async Task A_reason_is_required()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id);
        var author = await _world.LoginAsync(x.Author);
        Assert.Equal(["reason"], await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(5, " "))).ValidationFieldsAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter CorrectionTests`
Expected: build fails — `ResetAttemptRequest` doesn't exist.

- [ ] **Step 3: Add the contracts**

`server/Secucode.Contracts/CorrectionContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record ResetAttemptRequest(string Reason, int? ExtraMinutes);

public sealed record ReopenAttemptRequest(string Reason, int ExtraMinutes);

public sealed record ExtendAttemptRequest(int Minutes, string Reason);

public sealed record ExtendExamRequest(int Minutes, string Reason, bool ExtendWindow);

public sealed record ExtendExamResult(int AttemptsExtended, DateTime EndsAt);
```

- [ ] **Step 4: Add the endpoints**

`server/Secucode.Api/Features/Corrections/CorrectionEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Attempts;
using Secucode.Api.Features.Exams;
using Secucode.Api.Features.Live;
using Secucode.Contracts;

namespace Secucode.Api.Features.Corrections;

// Spec §6. Order of checks: capability (403) → switch (403) → input (400) → state (409).
public static class CorrectionEndpoints
{
    public static void MapCorrections(this IEndpointRouteBuilder api)
    {
        api.MapPost("/attempts/{id:int}/reset", ResetAsync);
        api.MapPost("/attempts/{id:int}/reopen", ReopenAsync);
        api.MapPost("/attempts/{id:int}/extend", ExtendAttemptAsync);
        api.MapPost("/exams/{id:int}/extend", ExtendExamAsync);
    }

    private static async Task<AttemptDto> ResetAsync(int id, ResetAttemptRequest req, AppDbContext db, Caller caller, AuditWriter audit,
        TimeProvider time, LiveNotifier live)
    {
        var a = await LoadAsync(db, id);
        await caller.RequireOnExamAsync(Capability.ResetAttempts, a.Exam, "You can't reset attempts on this exam.");
        await Switches.EnsureOnAsync(db, a.Exam, Switch.Reset);
        var now = time.UtcNow();
        Reason(req.Reason)
            .Rule(req.ExtraMinutes is null or (>= 1 and <= 240), "extraMinutes", "Extra minutes must be between 1 and 240.")
            .Rule(now < a.Exam.EndsAt || req.ExtraMinutes is not null, "extraMinutes", "The exam window has closed, so give the student extra minutes.")
            .ThrowIfInvalid();
        if (a.Status == AttemptStatus.Voided)
            throw ApiException.Conflict("This attempt is already voided.");

        var before = Snapshot(a);
        a.Status = AttemptStatus.Voided;
        a.VoidedAt = now;
        a.VoidedBy = caller.UserId;
        a.VoidReason = req.Reason.Trim();
        a.AllowanceMinutes = req.ExtraMinutes;
        audit.Record(caller.UserId, "attempt.reset", "Attempt", a.Id, before, Snapshot(a), a.VoidReason, a.ExamId, a.Exam.BranchId);
        await db.SaveChangesAsync();
        await live.AttemptVoidedAsync(a);
        return a.ToDto(now);
    }

    private static async Task<AttemptDto> ReopenAsync(int id, ReopenAttemptRequest req, AppDbContext db, Caller caller, AuditWriter audit,
        TimeProvider time, LiveNotifier live)
    {
        var a = await LoadAsync(db, id);
        await caller.RequireOnExamAsync(Capability.ReopenAttempts, a.Exam, "You can't reopen attempts on this exam.");
        await Switches.EnsureOnAsync(db, a.Exam, Switch.Reopen);
        Reason(req.Reason).Rule(req.ExtraMinutes is >= 1 and <= 240, "extraMinutes", "Extra minutes must be between 1 and 240.").ThrowIfInvalid();
        if (a.Status is not (AttemptStatus.Submitted or AttemptStatus.AutoSubmitted))
            throw ApiException.Conflict("Only submitted attempts can be reopened.");

        var now = time.UtcNow();
        var before = Snapshot(a);
        a.Status = AttemptStatus.InProgress;
        a.DeadlineAt = now.AddMinutes(req.ExtraMinutes);
        a.SubmittedAt = null;
        a.SubmitReason = null;
        audit.Record(caller.UserId, "attempt.reopen", "Attempt", a.Id, before, Snapshot(a), req.Reason.Trim(), a.ExamId, a.Exam.BranchId);
        await db.SaveChangesAsync();
        await live.AttemptReopenedAsync(a);
        return a.ToDto(now);
    }

    private static async Task<AttemptDto> ExtendAttemptAsync(int id, ExtendAttemptRequest req, AppDbContext db, Caller caller, AuditWriter audit,
        TimeProvider time, LiveNotifier live)
    {
        var a = await LoadAsync(db, id);
        await caller.RequireOnExamAsync(Capability.ExtendTime, a.Exam, "You can't extend time on this exam.");
        await Switches.EnsureOnAsync(db, a.Exam, Switch.Extension);
        Reason(req.Reason).Rule(req.Minutes is >= 1 and <= 240, "minutes", "Minutes must be between 1 and 240.").ThrowIfInvalid();
        if (a.Status != AttemptStatus.InProgress)
            throw ApiException.Conflict("Only attempts in progress can be extended. Reopen it instead.");

        var before = Snapshot(a);
        a.DeadlineAt = a.DeadlineAt.AddMinutes(req.Minutes);
        audit.Record(caller.UserId, "attempt.extend", "Attempt", a.Id, before, Snapshot(a), req.Reason.Trim(), a.ExamId, a.Exam.BranchId);
        await db.SaveChangesAsync();
        await live.DeadlineChangedAsync(a);
        return a.ToDto(time.UtcNow());
    }

    private static async Task<ExtendExamResult> ExtendExamAsync(int id, ExtendExamRequest req, AppDbContext db, Caller caller, AuditWriter audit,
        LiveNotifier live)
    {
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ExtendTime, exam, "You can't extend time on this exam.");
        await Switches.EnsureOnAsync(db, exam, Switch.Extension);
        Reason(req.Reason).Rule(req.Minutes is >= 1 and <= 240, "minutes", "Minutes must be between 1 and 240.").ThrowIfInvalid();

        var running = await db.Attempts.Where(a => a.ExamId == id && a.Status == AttemptStatus.InProgress).ToListAsync();
        var before = new { exam.EndsAt, Attempts = running.Select(a => new { a.Id, a.DeadlineAt }).ToList() };
        foreach (var a in running)
            a.DeadlineAt = a.DeadlineAt.AddMinutes(req.Minutes);
        if (req.ExtendWindow)
            exam.EndsAt = exam.EndsAt.AddMinutes(req.Minutes);
        var after = new { exam.EndsAt, Attempts = running.Select(a => new { a.Id, a.DeadlineAt }).ToList() };
        audit.Record(caller.UserId, "exam.extend", "Exam", exam.Id, before, after, req.Reason.Trim(), exam.Id, exam.BranchId);
        await db.SaveChangesAsync();

        foreach (var a in running)
            await live.DeadlineChangedAsync(a);
        return new ExtendExamResult(running.Count, exam.EndsAt);
    }

    private static async Task<Attempt> LoadAsync(AppDbContext db, int id) =>
        await db.Attempts.Include(a => a.Exam).SingleOrDefaultAsync(a => a.Id == id) ?? throw ApiException.NotFound("Attempt");

    private static Validator Reason(string? reason) => new Validator()
        .Rule(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500, "reason", "A reason (up to 500 characters) is required.");

    private static object Snapshot(Attempt a) => new { a.Status, a.DeadlineAt, a.SubmittedAt, a.AllowanceMinutes };
}
```

In `Endpoints.MapSecucodeEndpoints` add `api.MapCorrections();` and `using Secucode.Api.Features.Corrections;`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter CorrectionTests`
Expected: 9 passed.

- [ ] **Step 6: Commit**

```bash
git add -A server
git commit -m "feat(api): audited staff corrections behind capability and switches"
```

---

### Task 15: Audit log endpoint and audit coverage

`GET /audit` with scope-filtered visibility (spec §7), plus one test that walks every audited endpoint to prove "exactly one row per change, none for failed changes".

**Files:**
- Create: `server/Secucode.Contracts/AuditContracts.cs`
- Create: `server/Secucode.Api/Features/Audit/AuditEndpoints.cs`
- Modify: `server/Secucode.Api/Endpoints.cs`
- Test: `server/Secucode.Api.Tests/AuditTests.cs`

**Interfaces:**
- Consumes: `Caller.ActiveAssignmentsAsync`, `Scope`, every endpoint from Tasks 3–14.
- Produces: `AuditEntryDto(long Id, int? ActorUserId, string? ActorName, string Action, string EntityType, string EntityId, int? ExamId, int? BranchId, string? Reason, string? BeforeJson, string? AfterJson, string? Client, string? Ip, DateTime OccurredAt)`; route `GET /audit?examId=&entityType=&entityId=&actorId=&from=&to=&page=` returning `PageDto<AuditEntryDto>` (50 per page, newest first).

- [ ] **Step 1: Write the failing tests**

`server/Secucode.Api.Tests/AuditTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using static System.Net.HttpStatusCode;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuditTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private ExamRequest NewExam(Section s) => new(World.Unique("Audit Exam"), _world.Now.AddMinutes(-5), _world.Now.AddHours(2), 30,
        ExamMode.WebAllowed, true, s.BranchId, s.ClassId, s.BatchId, true, true, true, true, [new QuestionInput(null, "Q1")]);

    private static async Task<PageDto<AuditEntryDto>> Audit(HttpClient c, string query) =>
        await (await c.GetAsync($"/api/audit?{query}")).ReadAsync<PageDto<AuditEntryDto>>();

    [Fact]
    public async Task Each_viewer_sees_only_the_audit_entries_in_their_reach()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var mine = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, a.Department));
        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, a.Department));
        var otherDept = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, b.Department));
        var hodA = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var principal = await _world.LoginAsync(await _world.UserAsync(RoleNames.Principal, Scope.Institution));
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, a.Batch));

        var myExam = (await (await mine.PostJsonAsync("/api/exams", NewExam(a))).ReadAsync<ExamDto>(Created)).Id;
        var colleagueExam = (await (await colleague.PostJsonAsync("/api/exams", NewExam(a))).ReadAsync<ExamDto>(Created)).Id;
        var otherExam = (await (await otherDept.PostJsonAsync("/api/exams", NewExam(b))).ReadAsync<ExamDto>(Created)).Id;
        var newUser = await (await hodA.PostJsonAsync("/api/users", new CreateUserRequest(World.Unique("aud"), "Secret1", "Dept Hire", null, null,
            [new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), a.BranchId, null, null, null, null)]))).ReadAsync<UserDto>(Created);

        Assert.Single((await Audit(mine, $"examId={myExam}")).Items);
        Assert.Empty((await Audit(mine, $"examId={colleagueExam}")).Items);      // OwnOnly
        Assert.Single((await Audit(hodA, $"examId={colleagueExam}")).Items);     // department scope
        Assert.Empty((await Audit(hodA, $"examId={otherExam}")).Items);
        Assert.Single((await Audit(hodA, $"entityType=User&entityId={newUser.Id}")).Items); // department-level entry without an exam
        Assert.Single((await Audit(principal, $"examId={otherExam}")).Items);    // institution scope
        await (await student.GetAsync("/api/audit")).ExpectProblemAsync(Forbidden);
    }

    [Fact]
    public async Task Every_audited_change_writes_exactly_one_row_and_failed_changes_write_none()
    {
        await _world.SetSwitchesAsync(true);
        var s = await _world.SectionAsync();
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        var authorUser = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var author = await _world.LoginAsync(authorUser);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var facultyRole = await _world.RoleIdAsync(RoleNames.Faculty);
        var anonymous = api.Factory.CreateClient();

        async Task<JsonElement> Audited(string action, HttpStatusCode expected, Func<Task<HttpResponseMessage>> call)
        {
            var before = await _world.AuditCountAsync();
            var body = await (await call()).ExpectAsync(expected);
            Assert.True(before + 1 == await _world.AuditCountAsync(), $"{action} should write exactly one audit row");
            Assert.Equal(action, (await _world.LastAuditAsync()).Action);
            return body;
        }

        async Task NotAudited(HttpStatusCode expected, Func<Task<HttpResponseMessage>> call)
        {
            var before = await _world.AuditCountAsync();
            await (await call()).ExpectAsync(expected);
            Assert.Equal(before, await _world.AuditCountAsync());
        }

        static int Id(JsonElement body) => body.GetProperty("id").GetInt32();

        // Masters and settings
        var branch = Id(await Audited("branches.create", Created, () => admin.PostJsonAsync("/api/branches", new MasterRequest(World.Unique("B")))));
        await Audited("branches.update", OK, () => admin.PutJsonAsync($"/api/branches/{branch}", new MasterRequest(World.Unique("B"))));
        await Audited("branches.delete", NoContent, () => admin.DeleteAsync($"/api/branches/{branch}"));
        await Audited("settings.update", OK, () => admin.PutJsonAsync("/api/settings", new SettingsDto(true, true, true, true)));

        // Users, roles, assignments
        var user = Id(await Audited("user.create", Created, () => admin.PostJsonAsync("/api/users", new CreateUserRequest(World.Unique("aud"), "Secret1", "Audit Person", null, null,
            [new AssignmentRequest(facultyRole, s.BranchId, null, null, null, null)]))));
        await Audited("user.update", OK, () => admin.PutJsonAsync($"/api/users/{user}", new UpdateUserRequest("Renamed", null, true)));
        await Audited("user.reset_password", NoContent, () => admin.PostJsonAsync($"/api/users/{user}/reset-password", new ResetPasswordRequest("Another1")));
        await Audited("user.reset_pin", NoContent, () => admin.PostJsonAsync($"/api/users/{user}/reset-pin", new ResetPinRequest("2468")));
        var role = Id(await Audited("role.create", Created, () => admin.PostJsonAsync("/api/roles", new RoleRequest(World.Unique("Aud Role"), null, 150, ScopeLevels.Department))));
        await Audited("role.update", OK, () => admin.PutJsonAsync($"/api/roles/{role}", new RoleRequest(World.Unique("Aud Role"), null, 160, ScopeLevels.Department)));
        await Audited("role.grants", OK, () => admin.PutJsonAsync($"/api/roles/{role}/capabilities", new List<GrantDto> { new(Capability.ViewResults, Reach.InScope) }));
        var assignment = Id(await Audited("assignment.create", Created, () => admin.PostJsonAsync($"/api/users/{user}/assignments", new AssignmentRequest(role, s.BranchId, null, null, null, null))));
        await Audited("assignment.update", OK, () => admin.PutJsonAsync($"/api/assignments/{assignment}", new UpdateAssignmentRequest(null, _world.Today.AddDays(30))));
        await Audited("assignment.delete", NoContent, () => admin.DeleteAsync($"/api/assignments/{assignment}"));
        await Audited("role.delete", NoContent, () => admin.DeleteAsync($"/api/roles/{role}"));
        await Audited("user.deactivate", NoContent, () => admin.DeleteAsync($"/api/users/{user}"));

        // Exams and corrections
        var exam = Id(await Audited("exam.create", Created, () => author.PostJsonAsync("/api/exams", NewExam(s))));
        await Audited("exam.update", OK, () => author.PutJsonAsync($"/api/exams/{exam}", NewExam(s)));
        await Audited("exam.set_active", OK, () => author.PatchJsonAsync($"/api/exams/{exam}/active", new SetActiveRequest(true)));
        var attempt = await _world.AttemptAsync(exam, student.Id);
        await Audited("attempt.extend", OK, () => author.PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(5, "Power cut")));
        await Audited("exam.extend", OK, () => author.PostJsonAsync($"/api/exams/{exam}/extend", new ExtendExamRequest(5, "Fire drill", false)));
        await _world.WithDb(db => db.Attempts.Where(a => a.Id == attempt.Id).ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AttemptStatus.Submitted)));
        await Audited("attempt.reopen", OK, () => author.PostJsonAsync($"/api/attempts/{attempt.Id}/reopen", new ReopenAttemptRequest("Crash", 10)));
        await Audited("attempt.reset", OK, () => author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("Webcam failed", null)));
        var spare = Id(await Audited("exam.create", Created, () => author.PostJsonAsync("/api/exams", NewExam(s))));
        await Audited("exam.delete", NoContent, () => author.DeleteAsync($"/api/exams/{spare}"));

        // Logins
        await Audited("auth.login", OK, () => anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(authorUser.Username, World.Password, ClientKind.Web)));
        await Audited("auth.login_failed", Unauthorized, () => anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(authorUser.Username, "nope", ClientKind.Web)));

        // Rejected changes leave no trace
        await NotAudited(Forbidden, () => author.PostJsonAsync("/api/branches", new MasterRequest(World.Unique("Nope"))));
        await NotAudited(BadRequest, () => admin.PostJsonAsync("/api/users", new CreateUserRequest("x", "1", "", null, null, [])));
        await NotAudited(Conflict, () => author.DeleteAsync($"/api/exams/{exam}"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter AuditTests`
Expected: build fails — `AuditEntryDto` doesn't exist.

- [ ] **Step 3: Add the contract and endpoint**

`server/Secucode.Contracts/AuditContracts.cs`:

```csharp
namespace Secucode.Contracts;

public sealed record AuditEntryDto(long Id, int? ActorUserId, string? ActorName, string Action, string EntityType, string EntityId,
    int? ExamId, int? BranchId, string? Reason, string? BeforeJson, string? AfterJson, string? Client, string? Ip, DateTime OccurredAt);
```

`server/Secucode.Api/Features/Audit/AuditEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Contracts;

namespace Secucode.Api.Features.Audit;

public static class AuditEndpoints
{
    private const int PageSize = 50;

    public static void MapAudit(this IEndpointRouteBuilder api) => api.MapGet("/audit", ListAsync);

    private static async Task<PageDto<AuditEntryDto>> ListAsync(AppDbContext db, Caller caller, int? examId, string? entityType,
        string? entityId, int? actorId, DateTime? from, DateTime? to, int? page)
    {
        var grants = (await caller.ActiveAssignmentsAsync())
            .SelectMany(a => a.Role.Capabilities.Where(c => c.Capability == Capability.ViewAudit).Select(c => (a.Scope, c.Reach)))
            .ToList();
        if (grants.Count == 0)
            throw ApiException.Forbidden("You can't view the audit log.");

        var q = db.AuditLog.AsNoTracking();
        if (examId is not null) q = q.Where(e => e.ExamId == examId);
        if (!string.IsNullOrEmpty(entityType)) q = q.Where(e => e.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId)) q = q.Where(e => e.EntityId == entityId);
        if (actorId is not null) q = q.Where(e => e.ActorUserId == actorId);
        if (from is not null) q = q.Where(e => e.OccurredAt >= from);
        if (to is not null) q = q.Where(e => e.OccurredAt <= to);

        // Spec §7: Institution InScope sees all; narrower InScope sees its exams (and department-level rows); OwnOnly sees own exams.
        if (!grants.Any(g => g.Reach == Reach.InScope && g.Scope.Level == ScopeLevels.Institution))
        {
            var inScope = grants.Where(g => g.Reach == Reach.InScope).Select(g => g.Scope).ToList();
            var ownOnly = grants.Any(g => g.Reach == Reach.OwnOnly);
            var me = caller.UserId;
            // ponytail: resolves exam targets in memory; fine for one college, add a department index on audit rows if it grows
            var exams = await db.Exams.AsNoTracking().Select(e => new { e.Id, e.BranchId, e.ClassId, e.BatchId, e.CreatedBy }).ToListAsync();
            var examIds = exams
                .Where(e => (ownOnly && e.CreatedBy == me) || inScope.Any(s => s.Contains(new Scope(e.BranchId, e.ClassId, e.BatchId))))
                .Select(e => (int?)e.Id).ToList();
            var departmentIds = inScope.Where(s => s.Level == ScopeLevels.Department).Select(s => s.BranchId).ToList();
            q = q.Where(e => (e.ExamId != null && examIds.Contains(e.ExamId))
                          || (e.ExamId == null && e.BranchId != null && departmentIds.Contains(e.BranchId)));
        }

        var p = Math.Max(page ?? 1, 1);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip((p - 1) * PageSize).Take(PageSize)
            .Select(e => new AuditEntryDto(e.Id, e.ActorUserId,
                db.Users.Where(u => u.Id == e.ActorUserId).Select(u => u.FullName).FirstOrDefault(),
                e.Action, e.EntityType, e.EntityId, e.ExamId, e.BranchId, e.Reason, e.BeforeJson, e.AfterJson, e.Client, e.Ip, e.OccurredAt))
            .ToListAsync();
        return new PageDto<AuditEntryDto>(items, total, p, PageSize);
    }
}
```

In `Endpoints.MapSecucodeEndpoints` add `api.MapAudit();` and `using Secucode.Api.Features.Audit;`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter AuditTests`
Expected: 2 passed. If the coverage test names an action that wrote 0 or 2 rows, fix that endpoint (it must call `audit.Record` exactly once and persist it with the change).

- [ ] **Step 5: Commit**

```bash
git add -A server
git commit -m "feat(api): scoped audit log endpoint with full audit coverage test"
```

---

### Task 16: Demo seed, Docker Compose, README

A `seed` command that creates a small believable college, container packaging, and run instructions (spec §3 Cross-cutting, Hosting).

**Files:**
- Modify: `server/Secucode.Api/Data/Seeder.cs`, `server/Secucode.Api/Program.cs`
- Create: `server/Secucode.Api/Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.env.example`
- Modify: `README.md`, `.gitignore`
- Test: `server/Secucode.Api.Tests/SeedTests.cs`

**Interfaces:**
- Consumes: `DefaultRoles` (via `EnsureReferenceDataAsync`), entities.
- Produces: `Seeder.SeedDemoAsync(AppDbContext, TimeProvider) → Task<bool>` (false if users already exist), `IServiceProvider.SeedDemoAsync()`; `dotnet run --project server/Secucode.Api -- seed`.

- [ ] **Step 1: Write the failing test**

`server/Secucode.Api.Tests/SeedTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Data;
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SeedTests(ApiFixture api)
{
    [Fact]
    public async Task Seed_builds_a_demo_college_once_on_an_empty_database()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(api.ConnectionString("seed_" + Guid.NewGuid().ToString("N")[..8]), DatabaseSetup.ServerVersion).Options;
        await using var db = new AppDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            await Seeder.EnsureReferenceDataAsync(db, api.Time);

            Assert.True(await Seeder.SeedDemoAsync(db, api.Time));

            Assert.Equal(8, await db.Roles.CountAsync());
            Assert.Equal(5, await db.Users.CountAsync());
            var hod = await db.Users.Include(u => u.Assignments).ThenInclude(a => a.Role).SingleAsync(u => u.Username == "hod.cse");
            Assert.Equal([RoleNames.Faculty, RoleNames.Hod], hod.Assignments.Select(a => a.Role.Name).OrderBy(n => n));
            Assert.True(BCrypt.Net.BCrypt.Verify("Hod@123", hod.PasswordHash));
            Assert.True(await db.Exams.AnyAsync(e => e.Title == "Sample Coding Test" && e.Questions.Count == 2));

            Assert.False(await Seeder.SeedDemoAsync(db, api.Time)); // idempotent
            Assert.Equal(5, await db.Users.CountAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test server/Secucode.Api.Tests --filter SeedTests`
Expected: build fails — `Seeder.SeedDemoAsync` doesn't exist.

- [ ] **Step 3: Add the demo seed**

Append to `Seeder` in `server/Secucode.Api/Data/Seeder.cs` (add `using Secucode.Contracts;`):

```csharp
    // A small believable college for demos. Does nothing if any user exists.
    public static async Task<bool> SeedDemoAsync(AppDbContext db, TimeProvider time)
    {
        if (await db.Users.AnyAsync())
            return false;

        var now = time.UtcNow();
        var cse = new Branch { Name = "Computer Engineering" };
        var mech = new Branch { Name = "Mechanical Engineering" };
        var te = new SchoolClass { Name = "TE" };
        var be = new SchoolClass { Name = "BE" };
        var a1 = new Batch { Name = "A1" };
        var a2 = new Batch { Name = "A2" };
        db.AddRange(cse, mech, te, be, a1, a2);
        await db.SaveChangesAsync();

        var roles = await db.Roles.ToDictionaryAsync(r => r.Name);
        User Person(string username, string password, string fullName, string? pin, params (string Role, Scope Scope)[] assignments)
        {
            var user = new User
            {
                Username = username, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                PinHash = pin is null ? null : BCrypt.Net.BCrypt.HashPassword(pin), FullName = fullName, CreatedAt = now,
            };
            foreach (var (role, scope) in assignments)
                user.Assignments.Add(new RoleAssignment
                {
                    RoleId = roles[role].Id, BranchId = scope.BranchId, ClassId = scope.ClassId, BatchId = scope.BatchId, CreatedAt = now,
                });
            return user;
        }

        var department = new Scope(cse.Id, null, null);
        var faculty = Person("faculty.cse", "Faculty@123", "Prof. Rahul Patil", null, (RoleNames.Faculty, department));
        db.Users.AddRange(
            Person("superadmin", "Admin@123", "Super Admin", null, (RoleNames.SuperAdmin, Scope.Institution)),
            Person("hod.cse", "Hod@123", "Dr. Asha Kulkarni", null, (RoleNames.Faculty, department), (RoleNames.Hod, department)),
            faculty,
            Person("student1", "Student@123", "Neha Sharma", "1234", (RoleNames.Student, new Scope(cse.Id, te.Id, a1.Id))),
            Person("student2", "Student@123", "Aman Verma", "1234", (RoleNames.Student, new Scope(cse.Id, te.Id, a1.Id))));
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            Title = "Sample Coding Test", StartsAt = now.AddMinutes(-5), EndsAt = now.AddHours(4), DurationMinutes = 60,
            Mode = ExamMode.WebAllowed, BranchId = cse.Id, ClassId = te.Id, CreatedBy = faculty.Id, CreatedAt = now,
        };
        exam.Questions.Add(new Question { Text = "Write a program that prints the numbers 1 to 10.", SortOrder = 0 });
        exam.Questions.Add(new Question { Text = "Read an integer n from input and print the sum of 1..n.", SortOrder = 1 });
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        return true;
    }

    public static async Task SeedDemoAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var created = await SeedDemoAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<TimeProvider>());
        Console.WriteLine(created
            ? "Demo data created. Sign in as superadmin / Admin@123, hod.cse / Hod@123, faculty.cse / Faculty@123, student1 / Student@123 (PIN 1234)."
            : "The database already has users, so demo data was not added.");
    }
```

In `Program.cs`, replace `app.Run();` with:

```csharp
if (args.Contains("seed"))
{
    await app.Services.SeedDemoAsync();
    return;
}
app.Run();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test server/Secucode.Api.Tests --filter SeedTests`
Expected: 1 passed.

- [ ] **Step 5: Add container packaging**

`server/Secucode.Api/Dockerfile` (build context = repository root):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/Secucode.Contracts/Secucode.Contracts.csproj server/Secucode.Contracts/
COPY server/Secucode.Api/Secucode.Api.csproj server/Secucode.Api/
RUN dotnet restore server/Secucode.Api/Secucode.Api.csproj
COPY server/Secucode.Contracts/ server/Secucode.Contracts/
COPY server/Secucode.Api/ server/Secucode.Api/
RUN dotnet publish server/Secucode.Api/Secucode.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Secucode.Api.dll"]
```

`.dockerignore`:

```
**/bin/
**/obj/
.git/
.vs/
clients/
docs/
```

`docker-compose.yml`:

```yaml
# Local or server deployment: MySQL + API. The API starts code-runner containers through the host's Docker.
# Security note: mounting docker.sock gives the API container control of the host's Docker daemon.
services:
  mysql:
    image: mysql:8.4
    environment:
      MYSQL_ROOT_PASSWORD: ${MYSQL_ROOT_PASSWORD:-secucode-root}
      MYSQL_DATABASE: secucode_api
    volumes:
      - mysql-data:/var/lib/mysql
    healthcheck:
      test: ["CMD-SHELL", "mysqladmin ping -h localhost -p$$MYSQL_ROOT_PASSWORD"]
      interval: 5s
      retries: 30

  api:
    build:
      context: .
      dockerfile: server/Secucode.Api/Dockerfile
    depends_on:
      mysql:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Default: "Server=mysql;Database=secucode_api;User Id=root;Password=${MYSQL_ROOT_PASSWORD:-secucode-root};"
      Jwt__Key: ${JWT_KEY:?Set JWT_KEY in .env to a random string of at least 32 characters}
      Runner__Mode: Docker
      Runner__DockerEndpoint: unix:///var/run/docker.sock
      Evidence__Root: /data/evidence
      Cors__WebOrigins__0: ${WEB_ORIGIN:-http://localhost:5173}
    ports:
      - "8080:8080"
    volumes:
      - evidence:/data/evidence
      - /var/run/docker.sock:/var/run/docker.sock

volumes:
  mysql-data:
  evidence:
```

`.env.example`:

```
# Copy to .env and change these before running `docker compose up`.
JWT_KEY=replace-with-a-long-random-string-of-at-least-32-characters
MYSQL_ROOT_PASSWORD=secucode-root
WEB_ORIGIN=http://localhost:5173
```

Append to `.gitignore`:

```
# Local secrets for docker compose
.env
# Evidence written by a locally running API
server/Secucode.Api/evidence/
```

- [ ] **Step 6: Verify the container stack end to end**

```bash
cp .env.example .env
docker compose up --build -d
curl http://localhost:8080/api/health
docker compose exec api dotnet Secucode.Api.dll seed
curl -s -X POST http://localhost:8080/api/auth/login -H "Content-Type: application/json" \
  -d '{"username":"student1","password":"Student@123","client":"Web"}'
docker compose down
```

Expected: health returns `{"status":"ok"}`; seed prints "Demo data created…"; login returns JSON with `accessToken`. (Swagger is only enabled outside Production.)

- [ ] **Step 7: Document it in the README**

Add this section to `README.md` after "Getting started":

````markdown
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

**Tests** (Docker Desktop must be running; `python` and `node` on PATH):

```powershell
dotnet test server/Secucode.Api.Tests --filter "Category!=Docker"   # fast suite
dotnet test server/Secucode.Api.Tests --filter "Category=Docker"    # sandbox tests, pulls images on first run
```
````

- [ ] **Step 8: Run the whole suite**

Run: `dotnet build Secucode.sln -v q` → `Build succeeded`, 0 warnings.
Run: `dotnet test server/Secucode.Api.Tests` → all tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A server docker-compose.yml .dockerignore .env.example .gitignore README.md
git commit -m "feat(api): demo seed, docker compose packaging and run docs"
```
