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
