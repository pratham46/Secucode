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
