using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Persistence seam for <see cref="Game"/> aggregates. Implementations live in
/// ChessMvp.Infrastructure so ChessMvp.Domain stays free of EF Core / storage concerns.
/// </summary>
public interface IGameRepository
{
    Task<Game?> GetByIdAsync(Guid gameId);

    Task<Game?> GetByIdWithMovesAsync(Guid gameId);

    Task AddAsync(Game game);

    Task<IReadOnlyList<Move>> GetMovesAsync(Guid gameId);

    Task SaveChangesAsync();
}
