using Microsoft.Extensions.Options;

namespace Secucode.Api.Features.Attempts;

public sealed class EvidenceOptions
{
    public string Root { get; set; } = "evidence"; // relative paths resolve against the app's content root
}

// ponytail: local disk volume; swap for blob storage (S3/Azure) when the API runs on more than one machine.
public sealed class EvidenceStore(IOptions<EvidenceOptions> options, IHostEnvironment env)
{
    private string Root => Path.GetFullPath(options.Value.Root, env.ContentRootPath);

    // File names are always server-generated; nothing from the request reaches the path.
    public async Task<string> SaveAsync(int examId, int attemptId, Stream content, string extension, CancellationToken ct)
    {
        var relative = Path.Combine(examId.ToString(), attemptId.ToString(), $"{Guid.NewGuid():N}{extension}");
        var full = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);
        return relative.Replace('\\', '/');
    }

    public Stream OpenRead(string relativePath) => File.OpenRead(Path.Combine(Root, relativePath));
}
