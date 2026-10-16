using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Desktop.Tests;

// xUnit only discovers collection definitions in the test assembly itself, so the API fixture is re-declared here.
[CollectionDefinition(Name)]
public sealed class DesktopApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "desktop-api";
}
