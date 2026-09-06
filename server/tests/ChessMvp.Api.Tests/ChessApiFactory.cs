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
    // Use the same stable rolling tag the project's docker-compose.yml pins for its SQL Server
    // service. A specific CU tag (e.g. 2022-CU14) can be retired from MCR over time, which makes
    // Testcontainers fail to pull the image and breaks every integration test that depends on this
    // factory; 2022-latest is always present and matches the image the app is actually run against.
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
