using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ChessMvp.Infrastructure.Persistence.Repositories;

public sealed class GameRepository : IGameRepository
{
    private readonly ChessDbContext _dbContext;

    public GameRepository(ChessDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Game?> GetByIdAsync(Guid gameId) =>
        _dbContext.Games.AsTracking().FirstOrDefaultAsync(g => g.Id == gameId);

    public Task<Game?> GetByIdWithMovesAsync(Guid gameId) =>
        _dbContext.Games
            .AsTracking()
            .Include(g => g.Moves)
            .FirstOrDefaultAsync(g => g.Id == gameId);

    public async Task AddAsync(Game game)
    {
        await _dbContext.Games.AddAsync(game);
    }

    public async Task<IReadOnlyList<Move>> GetMovesAsync(Guid gameId) =>
        await _dbContext.Moves
            .AsNoTracking()
            .Where(m => m.GameId == gameId)
            .OrderBy(m => m.MoveNumber)
            .ToListAsync();

    public async Task SaveChangesAsync()
    {
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Translate the EF-specific rowversion conflict into a domain exception so
            // ChessMvp.Domain never needs a compile-time dependency on EF Core.
            throw new GameStateConflictException();
        }
    }
}
