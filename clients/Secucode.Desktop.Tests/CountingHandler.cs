using System.Collections.Concurrent;
using System.Net.Http;

namespace Secucode.Desktop.Tests;

// Wraps the test server's handler and records every request path.
public sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly ConcurrentQueue<string> _paths = new();

    public int Count(string path) => _paths.Count(p => p == path);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _paths.Enqueue(request.RequestUri!.AbsolutePath);
        return base.SendAsync(request, ct);
    }
}
