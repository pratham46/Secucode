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
                          || (e.BranchId != null && departmentIds.Contains(e.BranchId))); // matched by branch so a deleted exam's history stays visible
        }

        var p = Math.Clamp(page ?? 1, 1, 100_000);
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
