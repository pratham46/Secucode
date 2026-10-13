using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Users;

public static class UserMapping
{
    public static AssignmentDto ToDto(this RoleAssignment a, DateOnly today) =>
        new(a.Id, a.RoleId, a.Role.Name, a.BranchId, a.ClassId, a.BatchId, a.ValidFrom, a.ValidTo, a.IsActiveOn(today), a.Role.IsStudent);

    public static UserDto ToDto(this User u, DateOnly today) =>
        new(u.Id, u.Username, u.FullName, u.Email, u.IsActive, u.PinHash is not null,
            u.Assignments.OrderByDescending(a => a.Role.Rank).Select(a => a.ToDto(today)).ToList());
}
