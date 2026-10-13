using System.Net;
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class HealthTests(ApiFixture api)
{
    [Fact]
    public async Task Health_is_anonymous_and_ok()
    {
        var body = await (await api.Factory.CreateClient().GetAsync("/api/health")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_routes_are_a_401_problem_when_anonymous()
    {
        await (await api.Factory.CreateClient().GetAsync("/api/nope")).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }
}
