using System.Net.Http;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Staff;

namespace Secucode.Desktop.Tests;

public sealed class ExamEditorInputTests
{
    private static ExamEditorViewModel Offline() => new(new ApiClient(new HttpClient()), null);

    [Fact]
    public void Local_times_become_utc_and_back()
    {
        var utc = ExamEditorViewModel.ToUtc(new DateTime(2026, 11, 2), new TimeSpan(9, 30, 0));

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Local).ToUniversalTime(), utc);
        Assert.Equal((new DateTime(2026, 11, 2), "09:30"), ExamEditorViewModel.Split(utc));
    }

    [Fact]
    public void Unsendable_input_is_caught_before_the_server()
    {
        var vm = Offline();
        vm.Department = new MasterOption(1, "CSE");

        vm.StartTime = "9.30";
        Assert.Equal("Use HH:mm for times, for example 09:30.", vm.BuildRequest().Error);

        vm.StartTime = "9:30";
        vm.Duration = "an hour";
        Assert.Equal("Duration must be a whole number of minutes.", vm.BuildRequest().Error);

        vm.Duration = "60";
        vm.Department = null;
        Assert.Equal("Choose a department.", vm.BuildRequest().Error);
    }

    [Fact]
    public void Blank_questions_are_left_out_and_order_is_kept()
    {
        var vm = Offline();
        vm.Department = new MasterOption(1, "CSE");
        vm.Questions.Add(new QuestionRow { Text = "First" });
        vm.Questions.Add(new QuestionRow { Text = "  " });
        vm.Questions.Add(new QuestionRow { Text = "Second" });
        vm.MoveQuestion(vm.Questions[2], -2);

        var request = vm.BuildRequest().Request!;

        Assert.Equal(["Second", "First"], request.Questions.Select(q => q.Text));
    }
}

[Collection(DesktopApiCollection.Name)]
public sealed class ExamEditorViewModelTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<(ApiClient Client, Section S, TestUser Author)> FacultyAsync()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var client = new ApiClient(api.Factory.CreateClient());
        await client.LoginAsync(author.Username, World.Password);
        return (client, s, author);
    }

    [Fact]
    public async Task Creates_an_exam_for_a_batch()
    {
        var (client, s, _) = await FacultyAsync();
        var vm = new ExamEditorViewModel(client, null);
        await vm.LoadAsync();
        vm.Title = World.Unique("Editor");
        vm.Department = vm.Departments.Single(d => d.Id == s.BranchId);
        vm.Year = vm.Years.Single(y => y.Id == s.ClassId);
        vm.Batch = vm.Batches.Single(b => b.Id == s.BatchId);
        vm.StartDate = DateTime.Today;
        vm.StartTime = "08:00";
        vm.EndDate = DateTime.Today.AddDays(1);
        vm.EndTime = "08:00";
        vm.Questions[0].Text = "Reverse a linked list";

        var (request, error) = vm.BuildRequest();
        var saved = await vm.SaveAsync(request!);

        Assert.Null(error);
        var exam = await client.ExamAsync(saved.Id);
        Assert.Equal(ExamEditorViewModel.ToUtc(DateTime.Today, new TimeSpan(8, 0, 0)), exam.StartsAt);
        Assert.Equal((s.BranchId, s.ClassId, s.BatchId), (exam.BranchId, exam.ClassId, exam.BatchId));
        Assert.Equal("Reverse a linked list", Assert.Single(exam.Questions!).Text);
    }

    [Fact]
    public async Task Editing_keeps_question_ids()
    {
        var (client, s, author) = await FacultyAsync();
        var existing = await _world.ExamAsync(author.Id, s.Batch);
        var vm = new ExamEditorViewModel(client, existing.Id);
        await vm.LoadAsync();
        var firstId = vm.Questions[0].Id;

        vm.Questions[0].Text = "Changed";
        await vm.SaveAsync(vm.BuildRequest().Request!);

        var exam = await client.ExamAsync(existing.Id);
        Assert.Equal(2, exam.Questions!.Count);
        Assert.Equal((firstId, "Changed"), (exam.Questions[0].Id, exam.Questions[0].Text));
        Assert.Equal(existing.Title, vm.Title);
    }

    [Fact]
    public async Task Server_rules_come_back_as_field_errors()
    {
        var (client, s, _) = await FacultyAsync();
        var vm = new ExamEditorViewModel(client, null);
        await vm.LoadAsync();
        vm.Department = vm.Departments.Single(d => d.Id == s.BranchId);
        vm.Title = "";

        var e = await Assert.ThrowsAsync<ApiError>(() => vm.SaveAsync(vm.BuildRequest().Request!));

        Assert.Equal(400, e.Status);
        Assert.Contains("title", e.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
