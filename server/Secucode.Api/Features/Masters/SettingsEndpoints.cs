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
