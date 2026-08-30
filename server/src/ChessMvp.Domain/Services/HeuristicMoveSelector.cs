using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// Heuristic move-selector. The first stage (<see cref="SelectBestCapture"/>) maximizes
/// material gain from captures. The combined stage (<see cref="SelectBestMove"/>) adds
/// lightweight positional tie-breakers — check, piece development, central control, and a queen
/// promotion preference — to the capture-stage material gain, so ties in material gain are
/// resolved in favour of the more positionally desirable move. Legal moves are supplied by
/// <see cref="IChessRulesEngine"/>; capture classification, material scoring, and the
/// tie-breakers are derived directly from the FEN (via <see cref="FenBoard"/>) so the selector
/// stays independent of the rules engine's internal board representation.
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

    // Tie-breaker weights, all small relative to a pawn (100) so they only decide between moves
    // of equal material gain. Check is worth half a pawn; developing a non-pawn/non-king piece
    // off its home rank is worth a third of a pawn; central control is graded by proximity to the
    // four center squares (max CentralControlRingRadius * CentralControlWeight). Queen promotion
    // is special-cased with a material-scale bonus so a queen is always preferred over an
    // under-promotion.
    public static readonly int CheckBonus = 50;
    public static readonly int DevelopmentBonus = 30;
    public static readonly int CentralControlWeight = 10;
    public static readonly int CentralControlRingRadius = 3;

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

    /// <inheritdoc/>
    public ScoredMove? SelectBestMove(string fen, PlayerColor sideToMove)
    {
        var legalMoves = _rulesEngine.GetAllLegalMoves(fen, sideToMove);
        if (legalMoves.Count == 0)
        {
            return null;
        }

        var board = FenBoard.Parse(fen);

        ScoredMove? best = null;
        foreach (var move in legalMoves)
        {
            // Capture-stage material gain: victim value minus aggressor value, zero for
            // non-captures. This is the dominant term; the tie-breakers below only decide
            // between moves that share this gain.
            var materialGain = 0;
            if (board.TryGetCapturedPiece(move, out var victim))
            {
                var aggressor = board.GetPieceAt(move.FromSquare) ?? PieceType.Pawn;
                materialGain = ValueOf(victim) - ValueOf(aggressor);
            }

            var checkBonus = GivesCheck(fen, sideToMove, move) ? CheckBonus : 0;
            var developmentBonus = DevelopmentScore(board, sideToMove, move);
            var centralBonus = CentralControlScore(move);
            var promotionBonus = PromotionScore(move);

            var score = materialGain + checkBonus + developmentBonus + centralBonus + promotionBonus;

            if (best is null || score > best.Score)
            {
                best = new ScoredMove
                {
                    Move = move,
                    MaterialGain = materialGain,
                    CheckBonus = checkBonus,
                    DevelopmentBonus = developmentBonus,
                    CentralControlBonus = centralBonus,
                    QueenPromotionBonus = promotionBonus,
                    Score = score,
                };
            }
        }

        return best;
    }

    /// <summary>
    /// Whether <paramref name="move"/> delivers check. Legality is guaranteed by the move
    /// generator, so this re-applies the move through the rules engine solely to read its
    /// check/checkmate flag. (Checkmate is a check, so both flags are honoured.)
    /// </summary>
    private bool GivesCheck(string fen, PlayerColor sideToMove, LegalMove move)
    {
        var result = _rulesEngine.TryApplyMove(
            fen, sideToMove, move.FromSquare, move.ToSquare, move.Promotion);

        return result.IsLegal && (result.IsCheck || result.IsCheckmate);
    }

    /// <summary>
    /// Rewards a non-pawn/non-king piece for leaving its home rank (rank 1 for White, rank 8 for
    /// Black). Pawns and the king are excluded; sideways shuffles along the home rank do not
    /// count as development.
    /// </summary>
    private static int DevelopmentScore(FenBoard board, PlayerColor sideToMove, LegalMove move)
    {
        var piece = board.GetPieceAt(move.FromSquare);
        if (!piece.HasValue)
        {
            return 0;
        }

        var kind = piece.Value;
        if (kind == PieceType.Pawn || kind == PieceType.King)
        {
            return 0;
        }

        var startingRank = sideToMove == PlayerColor.White ? '1' : '8';
        if (move.FromSquare.Length < 2 || move.FromSquare[1] != startingRank)
        {
            return 0;
        }

        if (move.ToSquare.Length < 2 || move.ToSquare[1] == startingRank)
        {
            return 0;
        }

        return DevelopmentBonus;
    }

    /// <summary>
    /// Rewards moves that improve occupation or influence near the center. Each square is scored
    /// by its Chebyshev distance to the nearest of the four center squares (d4, e4, d5, e5); the
    /// bonus is the increase in that centrality from the origin to the destination, clamped at
    /// zero so moves away from the center are never rewarded.
    /// </summary>
    private static int CentralControlScore(LegalMove move)
    {
        var fromCentrality = Centrality(move.FromSquare);
        var toCentrality = Centrality(move.ToSquare);
        return Math.Max(0, toCentrality - fromCentrality);
    }

    private static int Centrality(string square)
    {
        if (square.Length < 2)
        {
            return 0;
        }

        var file = square[0];
        var rank = square[1];

        // Distance to the nearest central file (d/e) and central rank (4/5).
        var fileDist = Math.Min(Math.Abs(file - 'd'), Math.Abs(file - 'e'));
        var rankDist = Math.Min(Math.Abs(rank - '4'), Math.Abs(rank - '5'));
        var chebyshev = Math.Max(fileDist, rankDist);

        return Math.Max(0, CentralControlRingRadius - chebyshev) * CentralControlWeight;
    }

    /// <summary>
    /// Always prefers a queen promotion over an under-promotion. The bonus is the material gained
    /// by replacing the pawn with the promoted piece, so queen outranks rook, which outranks
    /// bishop/knight — guaranteeing a queen is chosen whenever a promotion is chosen.
    /// </summary>
    private static int PromotionScore(LegalMove move)
    {
        if (!move.Promotion.HasValue)
        {
            return 0;
        }

        return ValueOf(move.Promotion.Value) - PawnValue;
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

    internal static int ValueOf(PromotionPieceType piece) => piece switch
    {
        PromotionPieceType.Queen => QueenValue,
        PromotionPieceType.Rook => RookValue,
        PromotionPieceType.Bishop => BishopValue,
        PromotionPieceType.Knight => KnightValue,
        _ => 0,
    };
}
