namespace Secucode.Contracts;

public sealed record GrantDto(Capability Capability, Reach Reach);

public sealed record RoleDto(int Id, string Name, string? Description, int Rank, bool IsSystem, bool IsStudent,
    ScopeLevels AllowedScopeLevels, IReadOnlyList<GrantDto> Grants);

public sealed record RoleRequest(string Name, string? Description, int Rank, ScopeLevels AllowedScopeLevels);
