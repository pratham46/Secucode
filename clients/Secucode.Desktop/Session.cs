using Secucode.Contracts;

namespace Secucode.Desktop;

// Who is signed in and what the UI should offer them. The API stays the authority: this only hides
// actions it would refuse, and a 403 is still shown with the server's reason.
public sealed class Session(MeResponse me)
{
    public UserDto User => me.User;
    public bool IsStudent => me.User.Assignments.Any(a => a.IsActive && a.IsStudent);
    public bool CanCreateExams => Has(Capability.CreateExams);
    public string RoleNames => string.Join(", ", me.User.Assignments.Where(a => a.IsActive).Select(a => a.RoleName).Distinct());

    public bool CanManage(ExamDto exam) => IsCreator(exam) || Grants(Capability.ManageExams, exam);
    public bool CanViewResults(ExamDto exam) => IsCreator(exam) || Grants(Capability.ViewResults, exam);

    private bool Has(Capability c) => me.Capabilities.Any(x => x.Capability == c);
    private bool IsCreator(ExamDto exam) => exam.CreatedBy == me.User.Id;

    // ponytail: scope containment by ids only; the API's checks (backend spec §5.3) cover anything this misses.
    private bool Grants(Capability c, ExamDto exam) => me.Capabilities.Any(x => x.Capability == c &&
        (x.Reach == Reach.OwnOnly ? IsCreator(exam) : Covers(x.Scope, exam)));

    private static bool Covers(ScopeDto s, ExamDto e) =>
        (s.BranchId is null || s.BranchId == e.BranchId) &&
        (s.ClassId is null || s.ClassId == e.ClassId) &&
        (s.BatchId is null || s.BatchId == e.BatchId);
}
