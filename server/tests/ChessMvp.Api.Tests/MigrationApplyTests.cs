using ChessMvp.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace ChessMvp.Api.Tests;

/// <summary>
/// Verifies that the EF Core migrations apply cleanly on a fresh SQL Server container started
/// from scratch (no pre-existing schema). This is the acceptance gate for the migration
/// generation step: it spins up an ephemeral Testcontainers.MsSql instance, runs
/// <c>Database.Migrate()</c> against an empty database, and asserts that both migrations —
/// <c>InitialCreate</c> and <c>AddAiOpponent</c> — applied without error and that the columns
/// they introduce actually exist on the <c>Games</c> table.
/// </summary>
public sealed class MigrationApplyTests : IAsyncLifetime
{
    private MsSqlContainer? _msSqlContainer;

    public async Task InitializeAsync()
    {
        _msSqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .Build();
        await _msSqlContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_msSqlContainer is not null)
        {
            await _msSqlContainer.DisposeAsync();
        }
    }

    private ChessDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ChessDbContext>()
            .UseSqlServer(_msSqlContainer!.GetConnectionString())
            .Options;
        return new ChessDbContext(options);
    }

    /// <summary>
    /// Runs a scalar query against the fresh container using a standalone connection (decoupled
    /// from the DbContext's owned connection to avoid disposing it out from under EF Core).
    /// </summary>
    private static async Task<string?> QueryScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result?.ToString();
    }

    [Fact]
    public async Task Migrate_OnFreshDatabase_AppliesInitialCreateAndAddAiOpponent()
    {
        // A brand-new container has an empty database; Migrate() must bring it all the way up to
        // the latest migration (AddAiOpponent) without any hand-holding.
        string connectionString;
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            connectionString = _msSqlContainer!.GetConnectionString();
        }

        // The InitialCreate migration creates the Games table.
        var gamesTableExists = await QueryScalarAsync(connectionString,
            "SELECT OBJECT_ID('Games', 'U');");
        Assert.False(string.IsNullOrEmpty(gamesTableExists));

        // The __EFMigrationsHistory table records every applied migration. Both migrations must
        // be present, proving the chain applied end-to-end on the fresh database.
        var initialCreateApplied = await QueryScalarAsync(connectionString,
            "SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId = '20260707173738_InitialCreate';");
        Assert.Equal("20260707173738_InitialCreate", initialCreateApplied);

        var addAiOpponentApplied = await QueryScalarAsync(connectionString,
            "SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId = '20260708000000_AddAiOpponent';");
        Assert.Equal("20260708000000_AddAiOpponent", addAiOpponentApplied);

        // The AddAiOpponent migration adds OpponentType (NOT NULL, default 0 = Human) and AiColor
        // (nullable) to Games. Their presence in INFORMATION_SCHEMA proves the migration ran.
        var opponentTypeExists = await QueryScalarAsync(connectionString,
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Games' AND COLUMN_NAME = 'OpponentType';");
        Assert.Equal("int", opponentTypeExists);

        var aiColorExists = await QueryScalarAsync(connectionString,
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Games' AND COLUMN_NAME = 'AiColor';");
        Assert.Equal("int", aiColorExists);
    }

    [Fact]
    public async Task Migrate_OnFreshDatabase_OpponentTypeHasHumanDefault()
    {
        string connectionString;
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            connectionString = _msSqlContainer!.GetConnectionString();
        }

        // OpponentType is configured with HasDefaultValue(GameOpponentType.Human) == 0, so SQL
        // Server should report a column default of ((0)).
        var columnDefault = await QueryScalarAsync(connectionString,
            "SELECT COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Games' AND COLUMN_NAME = 'OpponentType';");
        Assert.Equal("((0))", columnDefault);
    }

    [Fact]
    public async Task Migrate_OnFreshDatabase_AiColorIsNullable()
    {
        string connectionString;
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            connectionString = _msSqlContainer!.GetConnectionString();
        }

        // AiColor is a nullable PlayerColor? column, so it must report YES for IS_NULLABLE.
        var isNullable = await QueryScalarAsync(connectionString,
            "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Games' AND COLUMN_NAME = 'AiColor';");
        Assert.Equal("YES", isNullable);
    }
}
