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
