using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class WebHostingTests(ApiFixture api) : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("secucode_web_").FullName;
    private WebApplicationFactory<Program>? _factory;

    private HttpClient Client()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>SecuCode app</title>");
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app.js"), "console.log('app');");
        _factory = api.Factory.WithWebHostBuilder(b => b.UseWebRoot(_webRoot));
        return _factory.CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/exams")]
    [InlineData("/attempt/5")]
    public async Task App_routes_get_index_html_with_a_content_security_policy(string path)
    {
        var res = await Client().GetAsync(path);

        await res.ExpectAsync(HttpStatusCode.OK);
        Assert.Contains("<title>SecuCode app", await res.Content.ReadAsStringAsync());
        Assert.Contains("default-src 'self'", Assert.Single(res.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("no-cache", res.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Static_files_are_served_with_the_policy()
    {
        var res = await Client().GetAsync("/assets/app.js");

        await res.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("console.log('app');", await res.Content.ReadAsStringAsync());
        Assert.True(res.Headers.Contains("Content-Security-Policy"));
    }

    [Theory]
    [InlineData("blaze_face_short_range.tflite")]
    [InlineData("vision_wasm_internal.wasm")]
    public async Task MediaPipe_model_and_wasm_are_served(string file)
    {
        var c = Client();
        Directory.CreateDirectory(Path.Combine(_webRoot, "mediapipe"));
        File.WriteAllBytes(Path.Combine(_webRoot, "mediapipe", file), [1, 2, 3]);

        var res = await c.GetAsync($"/mediapipe/{file}");

        await res.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal([1, 2, 3], await res.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Api_hub_and_missing_file_paths_are_never_the_app()
    {
        var c = Client();

        await (await c.GetAsync("/api/nope")).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        var health = await c.GetAsync("/api/health");
        await health.ExpectAsync(HttpStatusCode.OK);
        Assert.False(health.Headers.Contains("Content-Security-Policy"));
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/hubs/nope")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/assets/missing.js")).StatusCode);
    }

    public void Dispose()
    {
        _factory?.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }
}
