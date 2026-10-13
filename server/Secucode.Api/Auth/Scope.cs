using Secucode.Contracts;

namespace Secucode.Api.Auth;

// Where a role assignment applies, or what an exam targets (spec §5.2).
public readonly record struct Scope(int? BranchId, int? ClassId, int? BatchId)
{
    public static readonly Scope Institution = new(null, null, null);

    public ScopeLevels Level =>
        BatchId is not null ? ScopeLevels.Batch
        : ClassId is not null ? ScopeLevels.Year
        : BranchId is not null ? ScopeLevels.Department
        : ScopeLevels.Institution;

    public bool IsValidShape => (ClassId is null || BranchId is not null) && (BatchId is null || ClassId is not null);

    // True when every field this scope sets matches the inner scope.
    public bool Contains(Scope inner) =>
        (BranchId is null || BranchId == inner.BranchId)
        && (ClassId is null || ClassId == inner.ClassId)
        && (BatchId is null || BatchId == inner.BatchId);

    public ScopeDto ToDto() => new(BranchId, ClassId, BatchId);
}
