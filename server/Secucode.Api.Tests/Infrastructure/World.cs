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
