using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Features.Staff;

public sealed class StaffExamsViewModel(ApiClient api, Session session)
{
    public DateTime From { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime To { get; set; } = DateTime.Today.AddDays(60);
    public bool CanCreate => session.CanCreateExams;
    public List<StaffExamRow> Rows { get; private set; } = [];

    public async Task LoadAsync() =>
        Rows = (await api.StaffExamsAsync(From.ToUniversalTime(), To.AddDays(1).ToUniversalTime()))
            .Select(e => new StaffExamRow(e, session)).ToList();

    public async Task ToggleActiveAsync(StaffExamRow row)
    {
        await api.SetExamActiveAsync(row.Exam.Id, !row.Exam.IsActive);
        await LoadAsync();
    }

    public async Task DeleteAsync(StaffExamRow row)
    {
        await api.DeleteExamAsync(row.Exam.Id);
        await LoadAsync();
    }
}

public sealed record StaffExamRow(ExamDto Exam, Session Session)
{
    public string Title => Exam.Title;
    public string Duration => $"{Exam.DurationMinutes} min";
    public string Mode => Exam.Mode == ExamMode.DesktopOnly ? "Desktop only" : "Desktop or web";
    public string Status => Exam.IsActive ? "Active" : "Inactive";
    public string CreatedBy => Exam.CreatedByName;
    public bool CanManage => Session.CanManage(Exam);
    public bool CanViewResults => Session.CanViewResults(Exam);

    public string Window
    {
        get
        {
            var start = Exam.StartsAt.ToLocalTime();
            var end = Exam.EndsAt.ToLocalTime();
            return start.Date == end.Date ? $"{start:ddd d MMM, HH:mm} – {end:HH:mm}" : $"{start:d MMM HH:mm} – {end:d MMM HH:mm}";
        }
    }
}
