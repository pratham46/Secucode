using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Staff;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class StaffExamsViewModelTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<(StaffExamsViewModel Vm, Section S, TestUser Author)> FacultyAsync()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var client = new ApiClient(api.Factory.CreateClient());
        var me = await client.LoginAsync(author.Username, World.Password);
        return (new StaffExamsViewModel(client, new Session(me)), s, author);
    }

    [Fact]
    public async Task Own_exams_are_listed_with_manage_and_results_rights()
    {
        var (vm, s, author) = await FacultyAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows, r => r.Exam.Id == exam.Id);
        Assert.True(row.CanManage);
        Assert.True(row.CanViewResults);
        Assert.True(vm.CanCreate);
        Assert.Equal("Active", row.Status);
    }

    [Fact]
    public async Task Toggling_flips_active_and_reloads()
    {
        var (vm, s, author) = await FacultyAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        await vm.LoadAsync();

        await vm.ToggleActiveAsync(vm.Rows.Single(r => r.Exam.Id == exam.Id));

        Assert.Equal("Inactive", vm.Rows.Single(r => r.Exam.Id == exam.Id).Status);
    }

    [Fact]
    public async Task Deleting_an_exam_with_attempts_is_refused_with_the_servers_reason()
    {
        var (vm, s, author) = await FacultyAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        await _world.AttemptAsync(exam.Id, student.Id);
        await vm.LoadAsync();

        var e = await Assert.ThrowsAsync<ApiError>(() => vm.DeleteAsync(vm.Rows.Single(r => r.Exam.Id == exam.Id)));

        Assert.Equal(409, e.Status);
        Assert.Contains("Deactivate it instead", e.Detail);
    }
}
