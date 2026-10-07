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
