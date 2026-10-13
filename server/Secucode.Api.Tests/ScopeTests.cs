using Secucode.Api.Auth;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

public sealed class ScopeTests
{
    private static readonly Scope Dept = new(1, null, null);
    private static readonly Scope Year = new(1, 2, null);
    private static readonly Scope Batch = new(1, 2, 3);

    [Fact]
    public void Levels_follow_the_set_fields()
    {
        Assert.Equal(ScopeLevels.Institution, Scope.Institution.Level);
        Assert.Equal(ScopeLevels.Department, Dept.Level);
        Assert.Equal(ScopeLevels.Year, Year.Level);
        Assert.Equal(ScopeLevels.Batch, Batch.Level);
    }

    [Fact]
    public void Wider_scopes_contain_narrower_ones_but_not_the_reverse()
    {
        Assert.True(Scope.Institution.Contains(Batch));
        Assert.True(Dept.Contains(Year));
        Assert.True(Year.Contains(Batch));
        Assert.True(Batch.Contains(Batch));
        Assert.False(Batch.Contains(Year));
        Assert.False(Year.Contains(Dept));
        Assert.False(Dept.Contains(new Scope(9, 2, 3)));
        Assert.False(Year.Contains(new Scope(1, 9, 3)));
    }

    [Fact]
    public void A_class_needs_a_department_and_a_batch_needs_a_class()
    {
        Assert.True(Batch.IsValidShape);
        Assert.False(new Scope(null, 2, null).IsValidShape);
        Assert.False(new Scope(1, null, 3).IsValidShape);
    }
}
