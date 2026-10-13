namespace Secucode.Contracts;

// All fields null = whole institution; branch = department; + class = year; + batch = batch.
public sealed record ScopeDto(int? BranchId, int? ClassId, int? BatchId);

public sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
