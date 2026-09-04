using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace ChessMvp.Api.Tests;

/// <summary>
/// Spins up a real, ephemeral SQL Server container per test class and points the host's
/// ChessMvpDb connection string at it, so the integration tests exercise EF Core / SQL Server
/// (including the rowversion concurrency check) rather than an in-memory substitute.
/// </summary>
public sealed class ChessApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The parameterless MsSqlBuilder() constructor is obsolete in Testcontainers.MsSql 4.13; pin
    // the image explicitly instead. We use the rolling "2022-latest" tag (the same one used by the
    // repo's docker-compose.yml) rather than a specific Cumulative Update tag such as 2022-CU14,
    // because Microsoft retires old CU tags from MCR over time — a retired tag makes the image
    // unpullable, which fails every integration test that depends on this container.
    private readonly MsSqlContainer _msSqlContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString => _msSqlContainer.GetConnectionString();

    async Task IAsyncLifetime.InitializeAsync() => await _msSqlContainer.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _msSqlContainer.StopAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ChessMvpDb"] = ConnectionString,
            });
        });
    }
}
