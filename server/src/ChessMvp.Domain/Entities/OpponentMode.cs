namespace ChessMvp.Domain.Entities;

/// <summary>
/// Discriminates how the second seat of a game is (or can be) filled. This is the
/// per-game mode discriminator persisted alongside the rest of the game state.
/// </summary>
public enum OpponentMode
{
    /// <summary>
    /// The classic two-player game: the game starts in <see cref="GameStatus.WaitingForPlayer2"/>
    /// and the second seat is filled via the shareable join link.
    /// </summary>
    Human = 0,

    /// <summary>
    /// A solo game against the server-side AI opponent: the game becomes active immediately
    /// after creation and the second seat can never be joined by a human.
    /// </summary>
    Computer = 1,
}
