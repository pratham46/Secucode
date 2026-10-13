using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Contracts;

namespace Secucode.Api.Tests.Infrastructure;

public sealed class LiveClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ConcurrentQueue<(string Method, LiveAttemptMessage Message)> _received = new();

    private LiveClient(HubConnection connection)
    {
        _connection = connection;
        foreach (var method in LiveMessages.All)
            _connection.On<LiveAttemptMessage>(method, m => _received.Enqueue((method, m)));
    }

    public static async Task<LiveClient> ConnectAsync(ApiFixture api, string accessToken)
    {
        var server = api.Factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/exams"), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        var client = new LiveClient(connection);
        await connection.StartAsync();
        // StartAsync returns before the server's OnConnectedAsync (user-group join) has run; a round-trip is the barrier.
        await connection.InvokeAsync("LeaveExam", 0);
        return client;
    }

    public Task JoinExamAsync(int examId) => _connection.InvokeAsync("JoinExam", examId);

    public async Task<LiveAttemptMessage> WaitForAsync(string method, Func<LiveAttemptMessage, bool>? match = null, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            foreach (var (m, message) in _received)
                if (m == method && (match?.Invoke(message) ?? true))
                    return message;
            await Task.Delay(50);
        }
        throw new TimeoutException($"No {method} message within {timeoutMs} ms.");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
