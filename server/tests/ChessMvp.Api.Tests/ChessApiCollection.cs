using Xunit;

namespace ChessMvp.Api.Tests;

/// <summary>
/// Shares a single Testcontainers SQL Server instance (and WebApplicationFactory) across every
/// test class in this assembly, so we pay the container-startup cost once per test run rather
/// than once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ChessApiCollection : ICollectionFixture<ChessApiFactory>
{
    public const string Name = "ChessApi";
}
