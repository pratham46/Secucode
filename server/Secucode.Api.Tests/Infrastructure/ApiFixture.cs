using Microsoft.Extensions.Time.Testing;
using MySqlConnector;
using Testcontainers.MySql;

namespace Secucode.Api.Tests.Infrastructure;

// One MySQL container and one API host for the whole run. Tests run sequentially in one collection
// and isolate themselves by creating uniquely named data.
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder()
        .WithImage("mysql:8.4")
        .WithUsername("root")
        .WithPassword("root")
        .Build();

    public ApiFactory Factory { get; private set; } = null!;
    public FakeTimeProvider Time => Factory.Time;

    public string ConnectionString(string database) =>
        new MySqlConnectionStringBuilder(_mysql.GetConnectionString()) { Database = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        Factory = new ApiFactory(ConnectionString("secucode_test"));
        _ = Factory.Server; // boots the app (and, from Task 2 on, migrates the database)
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _mysql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
