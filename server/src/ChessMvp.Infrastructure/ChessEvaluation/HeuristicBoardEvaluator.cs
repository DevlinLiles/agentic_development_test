using Chess;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Infrastructure.ChessEvaluation;

/// <summary>
/// A standalone, stateless heuristic board evaluator. It scores a position from the perspective
/// of the side to move by combining three terms:
/// <list type="bullet">
/// <item><term>Material</term><description>fixed centipawn values for every piece type.</description></item>
/// <item><term>Piece-square tables</term><description>per-piece-type positional bonuses/penalties
/// indexed by square, defined for every piece type (pawns and knights at minimum).</description></item>
/// <item><term>Mobility</term><description>the number of legal moves available to the side to move,
/// weighted into centipawns.</description></item>
/// </list>
/// The evaluator is a pure function of its inputs: the same <c>(board, side)</c> always yields the
/// same numeric score. A positive score favors <paramref name="side"/>; a negative score favors the
/// opponent. The score is symmetric &mdash; <c>Score(board, White) == -Score(board, Black)</c> &mdash;
/// because each color's own material, table, and mobility are accumulated separately and differenced.
/// </summary>
public sealed class HeuristicBoardEvaluator
{
    // Draw rules are kept identical to the rules-engine adapter so FEN reloading for mobility behaves
    // the same as the rest of the system. The value has no effect on raw legal-move enumeration.
    private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;

    /// <summary>
    /// Mobility weight: each legal move for the side to move contributes this many centipawns to
    /// that side's score. Kept small relative to material so a single piece's worth of mobility
    /// never outweighs a pawn.
    /// </summary>
    private const int MobilityWeight = 2;

    /// <summary>
    /// Material values in centipawns for every piece type. The king is assigned a large value so a
    /// value is defined for all piece types; since both sides always have exactly one king, it
    /// cancels out of the material balance and never dominates the score.
    /// </summary>
    private static readonly Dictionary<PieceType, int> MaterialValues = new()
    {
        [PieceType.Pawn] = 100,
        [PieceType.Knight] = 320,
        [PieceType.Bishop] = 330,
        [PieceType.Rook] = 500,
        [PieceType.Queen] = 900,
        [PieceType.King] = 20000,
    };

    // Piece-square tables, listed from White's perspective with rank 8 first (index 0 == a8) down to
    // rank 1 last (index 63 == h1), file a..h left to right. Black scores mirror the table vertically
    // (rank 1 at index 0) so the same bonus/penalty applies symmetrically for the mirrored square.
    // Values are the well-known simplified-evaluation tables (centipawns).

    private static readonly int[] PawnTable =
    {
         0,   0,   0,   0,   0,   0,   0,   0,
        50,  50,  50,  50,  50,  50,  50,  50,
        10,  10,  20,  20,  20,  20,  10,  10,
         5,   5,  10,  10,  10,  10,   5,   5,
         0,   0,   0,   0,   0,   0,   0,   0,
        -5,  -5, -10, -10, -10, -10,  -5,  -5,
        -5,  -5, -10, -10, -10, -10,  -5,  -5,
         0,   0,   0,   0,   0,   0,   0,   0,
    };

    private static readonly int[] KnightTable =
    {
       -50, -40, -30, -30, -30, -30, -40, -50,
       -40, -20,   0,   0,   0,   0, -20, -40,
       -30,   0,  10,  15,  15,  10,   0, -30,
       -30,   5,  15,  20,  20,  15,   5, -30,
       -30,   0,  15,  20,  20,  15,   0, -30,
       -30,   5,  10,  15,  15,  10,   5, -30,
       -40, -20,   0,   5,   5,   0, -20, -40,
       -50, -40, -30, -30, -30, -30, -40, -50,
    };

    private static readonly int[] BishopTable =
    {
       -20, -10, -10, -10, -10, -10, -10, -20,
       -10,   0,   0,   0,   0,   0,   0, -10,
       -10,   0,   5,  10,  10,   5,   0, -10,
       -10,   5,   5,  10,  10,   5,   5, -10,
       -10,   0,  10,  10,  10,  10,   0, -10,
       -10,  10,  10,  10,  10,  10,  10, -10,
       -10,   5,   0,   0,   0,   0,   5, -10,
       -20, -10, -10, -10, -10, -10, -10, -20,
    };

    private static readonly int[] RookTable =
    {
         0,   0,   0,   0,   0,   0,   0,   0,
         5,  10,  10,  10,  10,  10,  10,   5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
         0,   0,   0,   5,   5,   0,   0,   0,
    };

    private static readonly int[] QueenTable =
    {
       -20, -10, -10,  -5,  -5, -10, -10, -20,
       -10,   0,   0,   0,   0,   0,   0, -10,
       -10,   0,   5,   5,   5,   5,   0, -10,
        -5,   0,   5,   5,   5,   5,   0,  -5,
         0,   0,   5,   5,   5,   5,   0,  -5,
       -10,   5,   5,   5,   5,   5,   0, -10,
       -10,   0,   5,   0,   0,   0,   0, -10,
       -20, -10, -10,  -5,  -5, -10, -10, -20,
    };

    private static readonly int[] KingTable =
    {
       -30, -40, -40, -50, -50, -40, -40, -30,
       -30, -40, -40, -50, -50, -40, -40, -30,
       -30, -40, -40, -50, -50, -40, -40, -30,
       -30, -40, -40, -50, -50, -40, -40, -30,
       -20, -30, -30, -40, -40, -30, -30, -20,
       -10, -20, -20, -20, -20, -20, -20, -10,
        20,  20,   0,   0,   0,   0,  20,  20,
        20,  30,  10,   0,   0,  10,  30,  20,
    };

    /// <summary>
    /// Maps each piece type to its piece-square table. Defined for every piece type so the
    /// positional term is comprehensive; the acceptance criteria require pawns and knights at a
    /// minimum.
    /// </summary>
    private static readonly Dictionary<PieceType, int[]> PieceSquareTables = new()
    {
        [PieceType.Pawn] = PawnTable,
        [PieceType.Knight] = KnightTable,
        [PieceType.Bishop] = BishopTable,
        [PieceType.Rook] = RookTable,
        [PieceType.Queen] = QueenTable,
        [PieceType.King] = KingTable,
    };

    /// <summary>
    /// Scores <paramref name="board"/> from the perspective of <paramref name="side"/> (the side
    /// to move). Positive favors <paramref name="side"/>; negative favors the opponent. A pure
    /// function of the inputs: identical <c>(board, side)</c> always returns the same value.
    /// </summary>
    public int Score(ChessBoard board, PlayerColor side)
    {
        var white = EvaluateFor(board, PlayerColor.White);
        var black = EvaluateFor(board, PlayerColor.Black);
        return side == PlayerColor.White ? white - black : black - white;
    }

    /// <summary>
    /// Convenience overload that loads <paramref name="fen"/> into a board before scoring. Returns
    /// <c>0</c> for an invalid FEN, consistent with the rules engine's lenient FEN handling.
    /// </summary>
    public int Score(string fen, PlayerColor side)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return 0;
        }

        return Score(board, side);
    }

    /// <summary>
    /// Accumulates material, piece-square, and mobility terms for a single color. The result is
    /// that color's absolute score; the public <see cref="Score(ChessBoard, PlayerColor)"/> method
    /// differences the two colors to produce a side-relative score.
    /// </summary>
    private static int EvaluateFor(ChessBoard board, PlayerColor color)
    {
        var engineColor = ToEngineColor(color);
        var material = 0;
        var pst = 0;

        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var square = $"{(char)('a' + file)}{(char)('1' + rank)}";
                var piece = board[square];
                if (piece is null || piece.Color != engineColor)
                {
                    continue;
                }

                material += MaterialValues[piece.Type];
                pst += PieceSquareValue(piece.Type, piece.Color, file, rank);
            }
        }

        var mobility = MobilityWeight * CountLegalMoves(board, color);
        return material + pst + mobility;
    }

    /// <summary>
    /// Looks up the piece-square bonus for <paramref name="type"/> on the square at
    /// (<paramref name="file"/>, <paramref name="rank"/>). White reads the table with rank 8 at
    /// index 0; black mirrors vertically (rank 1 at index 0) so a black piece on, e.g., a1 uses the
    /// same table entry a white piece would use on a8.
    /// </summary>
    private static int PieceSquareValue(PieceType type, PieceColor color, int file, int rank)
    {
        if (!PieceSquareTables.TryGetValue(type, out var table))
        {
            return 0;
        }

        var index = color == PieceColor.White ? (7 - rank) * 8 + file : rank * 8 + file;
        return table[index];
    }

    /// <summary>
    /// Counts the legal moves available to <paramref name="side"/>. When <paramref name="side"/>
    /// is already the board's side to move this is a direct enumeration. Otherwise the FEN's
    /// active-color field is rewritten so the requested color moves, enabling enumeration of its
    /// moves even out of turn. If the flipped position is illegal (e.g. the side left in check would
    /// be the non-mover), the load fails and mobility falls back to <c>0</c> &mdash; a safe,
    /// deterministic default that keeps the score well-defined.
    /// </summary>
    private static int CountLegalMoves(ChessBoard board, PlayerColor side)
    {
        var engineColor = ToEngineColor(side);
        if (board.Turn == engineColor)
        {
            return board.Moves().Count();
        }

        var fen = board.ToFen();
        var fields = fen.Split(' ');
        if (fields.Length <= 1)
        {
            return 0;
        }

        fields[1] = side == PlayerColor.White ? "w" : "b";
        var flippedFen = string.Join(' ', fields);
        if (!ChessBoard.TryLoadFromFen(flippedFen, out var flipped, EnabledDrawRules))
        {
            return 0;
        }

        return flipped.Moves().Count();
    }

    private static PieceColor ToEngineColor(PlayerColor color) =>
        color == PlayerColor.White ? PieceColor.White : PieceColor.Black;
}
