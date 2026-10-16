using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Secucode.Contracts;

namespace Secucode.Desktop.Api;

// The /hubs/exams connection. Students are joined to their private group by the server automatically.
public sealed class LiveConnection : IAsyncDisposable
{
    private readonly HubConnection _hub;

    public event Action<string, LiveAttemptMessage>? Received;

    // Fires after the first connect and after every reconnect: pushes sent while disconnected are lost,
    // so listeners must re-read the attempt.
    public event Func<Task>? Connected;

    public LiveConnection(Uri apiBase, Func<Task<string?>> accessToken, Func<HttpMessageHandler>? handler = null,
        HttpTransportType transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(apiBase, "hubs/exams"), o =>
            {
                o.AccessTokenProvider = accessToken;
                o.Transports = transports;
                if (handler is not null) o.HttpMessageHandlerFactory = _ => handler();
            })
            .WithAutomaticReconnect()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        foreach (var method in LiveMessages.All)
            _hub.On<LiveAttemptMessage>(method, m => Received?.Invoke(method, m));
        _hub.Reconnected += async _ => await RaiseConnectedAsync();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_hub.State == HubConnectionState.Disconnected) await _hub.StartAsync(ct);
        // The server joins our private group after StartAsync returns; a round-trip guarantees it has happened.
        await _hub.InvokeAsync("LeaveExam", 0, ct);
        await RaiseConnectedAsync();
    }

    // The exam must not depend on the hub: keep trying in the background until connected or cancelled.
    public async Task StartWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await StartAsync(ct);
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task RaiseConnectedAsync()
    {
        if (Connected is { } handler)
            foreach (var h in handler.GetInvocationList().Cast<Func<Task>>())
                {
                try { await h(); }
                catch (Exception) { /* a faulty listener must not fail the connection or starve the others */ }
            }
    }

    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}
