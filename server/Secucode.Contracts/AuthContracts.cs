namespace Secucode.Contracts;

public sealed record LoginRequest(string Username, string Password, ClientKind Client);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User);

public sealed record UserDto(int Id, string Username, string FullName, string? Email, bool IsActive, bool HasPin, IReadOnlyList<AssignmentDto> Assignments);

public sealed record AssignmentDto(int Id, int RoleId, string RoleName, int? BranchId, int? ClassId, int? BatchId,
    DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive);

public sealed record EffectiveCapabilityDto(Capability Capability, Reach Reach, ScopeDto Scope, string RoleName);

public sealed record MeResponse(UserDto User, IReadOnlyList<EffectiveCapabilityDto> Capabilities);
