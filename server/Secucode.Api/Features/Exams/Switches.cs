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
