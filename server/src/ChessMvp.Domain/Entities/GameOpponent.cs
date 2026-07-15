namespace ChessMvp.Domain.Entities;

/// <summary>
/// Selects who occupies the black seat when a game is created.
/// </summary>
public enum GameOpponent
{
    /// <summary>
    /// A second human joins via the shareable link. The game starts in
    /// <see cref="GameStatus.WaitingForPlayer2"/>.
    /// </summary>
    Human,

    /// <summary>
    /// The heuristic AI opponent takes black immediately and the game starts
    /// <see cref="GameStatus.Active"/>. No join link is offered to the user.
    /// </summary>
    Ai,
}
