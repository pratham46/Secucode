using Secucode.Contracts;

namespace Secucode.Desktop.Tests;

public sealed class SessionTests
{
    private const int Me = 5;

    private static Session For(bool student = false, bool activeAssignment = true, params EffectiveCapabilityDto[] caps) =>
        new(new MeResponse(new UserDto(Me, "u", "User", null, true, student,
            [new AssignmentDto(1, 1, student ? "Student" : "Faculty", 1, null, null, null, null, activeAssignment, student)]), caps));

    private static EffectiveCapabilityDto Grant(Capability c, Reach reach = Reach.InScope, int? branch = 1, int? cls = null, int? batch = null) =>
        new(c, reach, new ScopeDto(branch, cls, batch), "Role");

    private static ExamDto Exam(int createdBy, int branch = 1, int? cls = null, int? batch = null) =>
        new(10, "Exam", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 30, ExamMode.DesktopOnly, true, branch, cls, batch,
            createdBy, "Creator", true, true, true, true, null);

    [Fact]
    public void Students_are_recognised_by_an_active_student_assignment()
    {
        Assert.True(For(student: true).IsStudent);
        Assert.False(For(student: true, activeAssignment: false).IsStudent);
        Assert.False(For().IsStudent);
    }

    [Fact]
    public void Creating_exams_needs_the_capability()
    {
        Assert.False(For().CanCreateExams);
        Assert.True(For(caps: Grant(Capability.CreateExams)).CanCreateExams);
    }

    [Fact]
    public void Creators_always_manage_and_view_their_own_exams()
    {
        var session = For();
        Assert.True(session.CanManage(Exam(createdBy: Me)));
        Assert.True(session.CanViewResults(Exam(createdBy: Me)));
        Assert.False(session.CanManage(Exam(createdBy: 99)));
    }

    [Fact]
    public void In_scope_grants_cover_exams_inside_the_scope_only()
    {
        var hod = For(caps: Grant(Capability.ManageExams, branch: 1));
        Assert.True(hod.CanManage(Exam(createdBy: 99, branch: 1, cls: 3, batch: 7)));
        Assert.False(hod.CanManage(Exam(createdBy: 99, branch: 2)));

        var yearCoordinator = For(caps: Grant(Capability.ViewResults, branch: 1, cls: 3));
        Assert.True(yearCoordinator.CanViewResults(Exam(createdBy: 99, branch: 1, cls: 3)));
        Assert.False(yearCoordinator.CanViewResults(Exam(createdBy: 99, branch: 1))); // a department-wide exam is wider than the year
    }

    [Fact]
    public void Own_only_grants_apply_to_own_exams_only()
    {
        var faculty = For(caps: Grant(Capability.ViewResults, Reach.OwnOnly));
        Assert.False(faculty.CanViewResults(Exam(createdBy: 99)));
    }

    [Fact]
    public void Role_names_list_active_roles_once()
    {
        Assert.Equal("Faculty", For().RoleNames);
    }
}
