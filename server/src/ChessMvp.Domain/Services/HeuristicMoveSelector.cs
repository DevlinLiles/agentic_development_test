using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// Heuristic move-selector whose first stage maximizes material gain from captures. Legal moves
/// are supplied by <see cref="IChessRulesEngine"/>; capture classification and material scoring
/// are derived directly from the FEN (via <see cref="FenBoard"/>) so the selector stays
/// independent of the rules engine's internal board representation.
/// </summary>
public sealed class HeuristicMoveSelector : IHeuristicMoveSelector
{
    // Standard material values expressed in centipawns (1/100 of a pawn): pawn 100, knight 300,
    // bishop 300, rook 500, queen 900. Exposed so callers/tests can reference the same scale the
    // selector uses. The king is never legally captured, so it carries no capturable value; the
    // entry exists only to make the value table total.
    public static readonly int PawnValue = 100;
    public static readonly int KnightValue = 300;
    public static readonly int BishopValue = 300;
    public static readonly int RookValue = 500;
    public static readonly int QueenValue = 900;
    private const int KingValue = 0;

    private readonly IChessRulesEngine _rulesEngine;

    public HeuristicMoveSelector(IChessRulesEngine rulesEngine)
    {
        _rulesEngine = rulesEngine ?? throw new ArgumentNullException(nameof(rulesEngine));
    }

    /// <inheritdoc/>
    public ScoredCapture? SelectBestCapture(string fen, PlayerColor sideToMove)
    {
        // Stage 1 input: every legal move for the side to move. The rules engine is the single
        // source of legality (checks, pins, castling, promotion, en passant rights, etc.).
        var legalMoves = _rulesEngine.GetAllLegalMoves(fen, sideToMove);
        if (legalMoves.Count == 0)
        {
            return null;
        }

        var board = FenBoard.Parse(fen);

        ScoredCapture? best = null;
        foreach (var move in legalMoves)
        {
            if (!board.TryGetCapturedPiece(move, out var victim))
            {
                // Non-captures are scored zero and never selected at this stage.
                continue;
            }

            // The aggressor always occupies the origin of a legal move; the fallback only guards
            // against an impossible empty origin.
            var aggressor = board.GetPieceAt(move.FromSquare) ?? PieceType.Pawn;
            var gain = ValueOf(victim) - ValueOf(aggressor);

            if (best is null || gain > best.MaterialGain)
            {
                best = new ScoredCapture { Move = move, MaterialGain = gain };
            }
        }

        return best;
    }

    internal static int ValueOf(PieceType piece) => piece switch
    {
        PieceType.Queen => QueenValue,
        PieceType.Rook => RookValue,
        PieceType.Bishop => BishopValue,
        PieceType.Knight => KnightValue,
        PieceType.Pawn => PawnValue,
        PieceType.King => KingValue,
        _ => 0,
    };
}
