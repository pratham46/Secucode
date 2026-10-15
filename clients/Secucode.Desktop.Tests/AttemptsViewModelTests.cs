using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Staff;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class AttemptsViewModelTests(ApiFixture api)
{
    private readonly World _world = new(api);

    [Fact]
    public async Task Lists_attempts_and_loads_one_in_detail()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var attempt = await _world.AttemptAsync(exam.Id, student.Id, AttemptStatus.Submitted);
        var client = new ApiClient(api.Factory.CreateClient());
        await client.LoginAsync(author.Username, World.Password);
        var vm = new AttemptsViewModel(client, await client.ExamAsync(exam.Id));

        await vm.LoadAsync();
        var detail = await vm.DetailAsync(attempt.Id);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("Submitted", row.Status);
        Assert.Equal("Student Tester", row.Student);
        Assert.Equal("print(1)", detail.Attempt.Code);
    }
}
