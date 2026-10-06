using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Secucode.Api.Tests.Infrastructure;

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    // Starts at the real "now" so JWTs and MySQL timestamps look normal; tests move it forward with Advance().
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
    public string EvidenceRoot { get; } = Directory.CreateTempSubdirectory("secucode_evidence_").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Jwt:Key", "test-signing-key-0123456789abcdef0123456789");
        builder.UseSetting("Runner:Mode", "Local");
        builder.UseSetting("Evidence:Root", EvidenceRoot);
        builder.UseSetting("Attempts:AutoCloseEnabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }
}
