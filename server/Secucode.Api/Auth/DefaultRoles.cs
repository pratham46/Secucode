using Secucode.Contracts;
using static Secucode.Contracts.Capability;
using static Secucode.Contracts.ScopeLevels;

namespace Secucode.Api.Auth;

// The seeded Indian-college hierarchy, exactly as spec §5.4. Everything except Super Admin is editable afterwards.
public static class DefaultRoles
{
    private static readonly Capability[] Corrections = [ResetAttempts, ReopenAttempts, ExtendTime];

    public static List<Secucode.Api.Data.Entities.Role> Create(DateTime now)
    {
        var all = Enum.GetValues<Capability>();
        Capability[] hod = [ManageUsers, AssignRoles, CreateExams, ManageExams, .. Corrections, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit];

        return
        [
            Make(RoleNames.SuperAdmin, 1000, Institution, InScope(all), isSystem: true),
            Make(RoleNames.Principal, 900, Institution, InScope(all.Where(c => c is not (ManageSettings or ManageMasters or ManageRoles)))),
            Make(RoleNames.ExamController, 800, Institution | Department,
                InScope([CreateExams, ManageExams, .. Corrections, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit])),
            Make(RoleNames.Hod, 700, Department, InScope(hod)),
            Make(RoleNames.SubHod, 600, Department, InScope(hod.Where(c => c != AssignRoles))),
            Make(RoleNames.ClassCoordinator, 400, Year | Batch,
                InScope([ManageUsers, CreateExams, .. Corrections, ViewResults, ViewEvidence])),
            Make(RoleNames.Faculty, 300, Department | Year | Batch,
                [.. InScope([CreateExams]), .. OwnOnly([.. Corrections, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit])]),
            Make(RoleNames.Student, 0, Batch, [], isStudent: true),
        ];

        Secucode.Api.Data.Entities.Role Make(string name, int rank, ScopeLevels levels, IEnumerable<Secucode.Api.Data.Entities.RoleCapability> grants, bool isSystem = false, bool isStudent = false) => new()
        {
            Name = name, Rank = rank, AllowedScopeLevels = levels, IsSystem = isSystem, IsStudent = isStudent,
            CreatedAt = now, Capabilities = grants.ToList(),
        };
    }

    private static IEnumerable<Secucode.Api.Data.Entities.RoleCapability> InScope(IEnumerable<Capability> caps) =>
        caps.Select(c => new Secucode.Api.Data.Entities.RoleCapability { Capability = c, Reach = Reach.InScope });

    private static IEnumerable<Secucode.Api.Data.Entities.RoleCapability> OwnOnly(IEnumerable<Capability> caps) =>
        caps.Select(c => new Secucode.Api.Data.Entities.RoleCapability { Capability = c, Reach = Reach.OwnOnly });
}
