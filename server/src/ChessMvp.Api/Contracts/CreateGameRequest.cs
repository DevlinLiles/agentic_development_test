using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

/// <summary>
/// Request body for POST /api/games. All members are optional with sensible defaults so the
/// endpoint keeps working for human-vs-human games created without a body.
/// </summary>
/// <param name="Opponent">
/// Whether the second seat is a human (waits for a join) or the computer (the game starts
/// immediately as Active). Defaults to <see cref="GameOpponentType.Human"/>.
/// </param>
/// <param name="Mode">
/// The side the requesting player wants to play. For AI games this determines which seat the AI
/// takes (the opposite colour). For human-vs-human games this is ignored and the creator always
/// plays White. Defaults to <see cref="PlayerColor.White"/>.
/// </param>
public sealed record CreateGameRequest(
    GameOpponentType? Opponent,
    PlayerColor? Mode)
{
    public GameOpponentType OpponentValue => Opponent ?? GameOpponentType.Human;
    public PlayerColor ModeValue => Mode ?? PlayerColor.White;
}
