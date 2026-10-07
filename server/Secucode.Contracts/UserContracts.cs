namespace Secucode.Contracts;

public sealed record AssignmentRequest(int RoleId, int? BranchId, int? ClassId, int? BatchId, DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record UpdateAssignmentRequest(DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record CreateUserRequest(string Username, string Password, string FullName, string? Email, string? Pin,
    IReadOnlyList<AssignmentRequest> Assignments);

public sealed record UpdateUserRequest(string FullName, string? Email, bool IsActive);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record ResetPinRequest(string NewPin);
