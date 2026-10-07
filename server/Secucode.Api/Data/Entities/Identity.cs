using Secucode.Api.Auth;
using Secucode.Contracts;

namespace Secucode.Api.Data.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? PinHash { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public List<RoleAssignment> Assignments { get; set; } = [];
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int Rank { get; set; }
    public bool IsSystem { get; set; }
    public bool IsStudent { get; set; }
    public ScopeLevels AllowedScopeLevels { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<RoleCapability> Capabilities { get; set; } = [];
}

public class RoleCapability
{
    public int RoleId { get; set; }
    public Capability Capability { get; set; }
    public Reach Reach { get; set; }
}

public class RoleAssignment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public int? BranchId { get; set; }
    public int? ClassId { get; set; }
    public int? BatchId { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Scope Scope => new(BranchId, ClassId, BatchId);
    public bool IsActiveOn(DateOnly day) => (ValidFrom is null || ValidFrom <= day) && (ValidTo is null || ValidTo >= day);
}

public class RefreshToken
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public ClientKind Client { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? ReplacedById { get; set; }
    public RefreshToken? ReplacedBy { get; set; }
}
