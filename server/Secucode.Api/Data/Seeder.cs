using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

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

    // A small believable college for demos. Does nothing if any user exists.
    public static async Task<bool> SeedDemoAsync(AppDbContext db, TimeProvider time, string adminPassword = "Admin@123")
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
            Person("superadmin", adminPassword, "Super Admin", null, (RoleNames.SuperAdmin, Scope.Institution)),
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
        var adminPassword = services.GetRequiredService<IConfiguration>()["Seed:AdminPassword"];
        if (string.IsNullOrEmpty(adminPassword))
        {
            if (services.GetRequiredService<IHostEnvironment>().IsProduction())
            {
                Console.Error.WriteLine("Refusing to seed in Production without Seed__AdminPassword (the Super Admin password). Set it and run again.");
                Environment.ExitCode = 1;
                return;
            }
            adminPassword = "Admin@123";
        }
        using var scope = services.CreateScope();
        var created = await SeedDemoAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), adminPassword);
        Console.WriteLine(created
            ? "Demo data created. Demo logins: superadmin (Seed:AdminPassword, default Admin@123 outside Production), hod.cse / Hod@123, faculty.cse / Faculty@123, student1 / Student@123 (PIN 1234). Change the demo passwords outside development."
            : "The database already has users, so demo data was not added.");
    }
}
