using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// Heuristic chess AI player. Delegates legal-move enumeration and scoring to
/// <see cref="IHeuristicMoveSelector"/>, which combines material gain with lightweight
/// positional tie-breakers (check, development, central control, queen promotion). The side to
/// move is read from the FEN, so the player is fully FEN-in/move-out and stateless.
/// </summary>
public sealed class HeuristicChessAiPlayer : IChessAiPlayer
{
    private readonly IHeuristicMoveSelector _moveSelector;

    public HeuristicChessAiPlayer(IHeuristicMoveSelector moveSelector)
    {
        _moveSelector = moveSelector ?? throw new ArgumentNullException(nameof(moveSelector));
    }

    /// <inheritdoc/>
    public AiMove? ChooseMove(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            throw new ArgumentNullException(nameof(fen));
        }

        var sideToMove = ParseSideToMove(fen);
        var best = _moveSelector.SelectBestMove(fen, sideToMove);
        if (best is null)
        {
            return null;
        }

        return new AiMove(best.Move.FromSquare, best.Move.ToSquare, best.Move.Promotion);
    }

    /// <summary>
    /// The side to move is the second whitespace-separated FEN field ("w" or "b"). Defaults to
    /// White when the field is missing or unrecognised, matching the starting-position default.
    /// </summary>
    private static PlayerColor ParseSideToMove(string fen)
    {
        var fields = fen.Split(' ');
        return fields.Length > 1 && fields[1] == "b" ? PlayerColor.Black : PlayerColor.White;
    }
}
