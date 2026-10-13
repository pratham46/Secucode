using System.Net;
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class HarnessTests(ApiFixture api)
{
    [Fact]
    public async Task The_api_boots_for_desktop_tests()
    {
        var body = await (await api.Factory.CreateClient().GetAsync("/api/health")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }
}
