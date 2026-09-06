using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// Heuristic position evaluator combining material values, piece-square tables, and a mobility
/// term. Pure and deterministic: identical inputs always produce identical scores, so the result
/// is safe to assert for equality in unit tests. The board is consumed as a FEN string, matching
/// the rest of the domain contracts, so this service carries no dependency on any third-party
/// move-generation library.
/// </summary>
/// <remarks>
/// The score is computed from the perspective of the requested <paramref name="side"/>: it is the
/// evaluator's own assets (material, placement, mobility) minus the opponent's. The implementation
/// is antisymmetric, so <c>Score(board, White) == -Score(board, Black)</c> for the material and
/// placement components.
/// </remarks>
public sealed class HeuristicEvaluator : IHeuristicEvaluator
{
    // Standard, widely used piece values in centipawns. The king is assigned a large value so it is
    // never "captured" in a material trade comparison, even though it is never actually removed.
    private const int PawnValue = 100;
    private const int KnightValue = 320;
    private const int BishopValue = 330;
    private const int RookValue = 500;
    private const int QueenValue = 900;
    private const int KingValue = 20000;

    // Small per-move weight for the mobility term. Keeps mobility a tie-breaker rather than
    // overwhelming material/placement signals.
    private const int MobilityWeight = 2;

    // Piece-square tables, indexed [file 0..7 from a-h][rank 0..7 from rank 1 to rank 8] and
    // expressed from White's perspective (rank 1 == index 0). A bonus is a positive entry; a
    // penalty is a negative entry. Black's table is the vertical mirror of White's, applied by
    // flipping the rank index, so symmetric piece placement yields antisymmetric scores.
    //
    // Values follow the well-known "Simplified Evaluation Function" tables by Tomasz Michniewski.
    private static readonly int[,] PawnTable =
    {
        {  0,  0,  0,  0,  0,  0,  0,  0 },
        { 50, 50, 50, 50, 50, 50, 50, 50 },
        { 10, 10, 20, 30, 30, 20, 10, 10 },
        {  5,  5, 10, 25, 25, 10,  5,  5 },
        {  0,  0,  0, 20, 20,  0,  0,  0 },
        {  5, -5,-10,  0,  0,-10, -5,  5 },
        {  5, 10, 10,-20,-20, 10, 10,  5 },
        {  0,  0,  0,  0,  0,  0,  0,  0 },
    };

    private static readonly int[,] KnightTable =
    {
        { -50,-40,-30,-30,-30,-30,-40,-50 },
        { -40,-20,  0,  0,  0,  0,-20,-40 },
        { -30,  0, 10, 15, 15, 10,  0,-30 },
        { -30,  5, 15, 20, 20, 15,  5,-30 },
        { -30,  0, 15, 20, 20, 15,  0,-30 },
        { -30,  5, 10, 15, 15, 10,  5,-30 },
        { -40,-20,  0,  5,  5,  0,-20,-40 },
        { -50,-40,-30,-30,-30,-30,-40,-50 },
    };

    private static readonly int[,] BishopTable =
    {
        { -20,-10,-10,-10,-10,-10,-10,-20 },
        { -10,  0,  0,  0,  0,  0,  0,-10 },
        { -10,  0,  5, 10, 10,  5,  0,-10 },
        { -10,  5,  5, 10, 10,  5,  5,-10 },
        { -10,  0, 10, 10, 10, 10,  0,-10 },
        { -10, 10, 10, 10, 10, 10, 10,-10 },
        { -10,  5,  0,  0,  0,  0,  5,-10 },
        { -20,-10,-10,-10,-10,-10,-10,-20 },
    };

    private static readonly int[,] RookTable =
    {
        {  0,  0,  0,  0,  0,  0,  0,  0 },
        {  5, 10, 10, 10, 10, 10, 10,  5 },
        { -5,  0,  0,  0,  0,  0,  0, -5 },
        { -5,  0,  0,  0,  0,  0,  0, -5 },
        { -5,  0,  0,  0,  0,  0,  0, -5 },
        { -5,  0,  0,  0,  0,  0,  0, -5 },
        { -5,  0,  0,  0,  0,  0,  0, -5 },
        {  0,  0,  0,  5,  5,  0,  0,  0 },
    };

    private static readonly int[,] QueenTable =
    {
        { -20,-10,-10, -5, -5,-10,-10,-20 },
        { -10,  0,  0,  0,  0,  0,  0,-10 },
        { -10,  0,  5,  5,  5,  5,  0,-10 },
        {  -5,  0,  5,  5,  5,  5,  0, -5 },
        {   0,  0,  5,  5,  5,  5,  0, -5 },
        { -10,  5,  5,  5,  5,  5,  0,-10 },
        { -10,  0,  5,  0,  0,  0,  0,-10 },
        { -20,-10,-10, -5, -5,-10,-10,-20 },
    };

    private static readonly int[,] KingMiddleGameTable =
    {
        { -30,-40,-40,-50,-50,-40,-40,-30 },
        { -30,-40,-40,-50,-50,-40,-40,-30 },
        { -30,-40,-40,-50,-50,-40,-40,-30 },
        { -30,-40,-40,-50,-50,-40,-40,-30 },
        { -20,-30,-30,-40,-40,-30,-30,-20 },
        { -10,-20,-20,-20,-20,-20,-20,-10 },
        {  20, 20,  0,  0,  0,  0, 20, 20 },
        {  20, 30, 10,  0,  0, 10, 30, 20 },
    };

    /// <inheritdoc />
    public int Score(string board, PlayerColor side)
    {
        if (string.IsNullOrWhiteSpace(board))
        {
            return 0;
        }

        if (!TryParsePlacement(board, out var grid, out _))
        {
            return 0;
        }

        // Score every square from White's perspective (White assets positive). The final, side-
        // relative score is obtained by negating when the caller asks for Black's perspective,
        // which keeps the function antisymmetric and symmetric with respect to color symmetry.
        var whiteScore = EvaluateForWhite(grid);

        var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;

        // Mobility is counted for the evaluated side only and added from that side's perspective, so
        // it naturally favors the side that moves more. Using pseudo-legal moves (ignoring whether
        // the move leaves the own king in check) keeps the term cheap and self-contained, satisfying
        // the "legal or pseudo-legal" requirement while remaining deterministic.
        perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);

        return perspective;
    }

    /// <summary>
    /// Sums material and piece-square-table values for every piece on the board from White's
    /// point of view (White pieces add, Black pieces subtract). Piece-square entries are read for
    /// White as written; for Black the rank index is flipped so the table is effectively mirrored.
    /// </summary>
    private static int EvaluateForWhite(Piece?[,] grid)
    {
        var score = 0;
        for (var file = 0; file < 8; file++)
        {
            for (var rank = 0; rank < 8; rank++)
            {
                var piece = grid[file, rank];
                if (piece is null)
                {
                    continue;
                }

                var material = MaterialValue(piece.Type);
                var placement = PieceSquareValue(piece, file, rank);

                if (piece.Color == PlayerColor.White)
                {
                    score += material + placement;
                }
                else
                {
                    score -= material + placement;
                }
            }
        }

        return score;
    }

    private static int MaterialValue(PieceType type) => type switch
    {
        PieceType.Pawn => PawnValue,
        PieceType.Knight => KnightValue,
        PieceType.Bishop => BishopValue,
        PieceType.Rook => RookValue,
        PieceType.Queen => QueenValue,
        PieceType.King => KingValue,
        _ => 0,
    };

    private static int PieceSquareValue(Piece piece, int file, int rank)
    {
        // White reads the table with rank as-is (rank index 0 == rank 1, the back rank). Black's
        // view is the vertical mirror, achieved by flipping the rank index, so a pawn advanced for
        // Black earns the same forward-pawn bonus a White pawn would on the mirrored square.
        var tableRank = piece.Color == PlayerColor.White ? rank : 7 - rank;

        return piece.Type switch
        {
            PieceType.Pawn => PawnTable[file, tableRank],
            PieceType.Knight => KnightTable[file, tableRank],
            PieceType.Bishop => BishopTable[file, tableRank],
            PieceType.Rook => RookTable[file, tableRank],
            PieceType.Queen => QueenTable[file, tableRank],
            PieceType.King => KingMiddleGameTable[file, tableRank],
            _ => 0,
        };
    }

    /// <summary>
    /// Counts the pseudo-legal moves available to <paramref name="sideToMoveFor"/> directly from
    /// the parsed board, without delegating to (or depending on) a third-party engine. Pseudo-legal
    /// here means moves a piece could make ignoring whether they leave the mover's own king in
    /// check. This is intentionally a lightweight approximation suitable for a mobility tie-
    /// breaker; full legality is the rules engine's responsibility and is not needed for scoring.
    /// </summary>
    private static int CountPseudoLegalMoves(Piece?[,] grid, PlayerColor sideToMoveFor)
    {
        var count = 0;
        for (var file = 0; file < 8; file++)
        {
            for (var rank = 0; rank < 8; rank++)
            {
                var piece = grid[file, rank];
                if (piece is null || piece.Color != sideToMoveFor)
                {
                    continue;
                }

                count += piece.Type switch
                {
                    PieceType.Pawn => CountPawnMoves(grid, piece.Color, file, rank),
                    PieceType.Knight => CountKnightMoves(grid, piece.Color, file, rank),
                    PieceType.Bishop => CountSlidingMoves(grid, piece.Color, file, rank, BishopDirections),
                    PieceType.Rook => CountSlidingMoves(grid, piece.Color, file, rank, RookDirections),
                    PieceType.Queen => CountSlidingMoves(grid, piece.Color, file, rank, QueenDirections),
                    PieceType.King => CountKingMoves(grid, piece.Color, file, rank),
                    _ => 0,
                };
            }
        }

        return count;
    }

    private static int CountPawnMoves(Piece?[,] grid, PlayerColor color, int file, int rank)
    {
        var count = 0;
        var direction = color == PlayerColor.White ? 1 : -1;

        // Single forward push onto an empty square.
        var oneAhead = rank + direction;
        if (InBounds(file, oneAhead) && grid[file, oneAhead] is null)
        {
            count++;

            // Double push from the starting rank, if both squares ahead are empty.
            var startRank = color == PlayerColor.White ? 1 : 6;
            var twoAhead = rank + 2 * direction;
            if (rank == startRank && InBounds(file, twoAhead) && grid[file, twoAhead] is null)
            {
                count++;
            }
        }

        // Diagonal captures (including empty target squares, which is the pseudo-legal relaxation:
        // a pawn "attacks" the diagonal regardless of occupancy, except en passant which we do not
        // model here since it does not change the move count meaningfully for scoring).
        foreach (var delta in new[] { -1, 1 })
        {
            var targetFile = file + delta;
            var targetRank = rank + direction;
            if (!InBounds(targetFile, targetRank))
            {
                continue;
            }

            var target = grid[targetFile, targetRank];
            if (target is null || target.Color != color)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountKnightMoves(Piece?[,] grid, PlayerColor color, int file, int rank)
    {
        var count = 0;
        foreach (var (df, dr) in KnightOffsets)
        {
            var targetFile = file + df;
            var targetRank = rank + dr;
            if (!InBounds(targetFile, targetRank))
            {
                continue;
            }

            var target = grid[targetFile, targetRank];
            if (target is null || target.Color != color)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountKingMoves(Piece?[,] grid, PlayerColor color, int file, int rank)
    {
        var count = 0;
        foreach (var (df, dr) in KingOffsets)
        {
            var targetFile = file + df;
            var targetRank = rank + dr;
            if (!InBounds(targetFile, targetRank))
            {
                continue;
            }

            var target = grid[targetFile, targetRank];
            if (target is null || target.Color != color)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountSlidingMoves(Piece?[,] grid, PlayerColor color, int file, int rank, (int, int)[] directions)
    {
        var count = 0;
        foreach (var (df, dr) in directions)
        {
            var targetFile = file + df;
            var targetRank = rank + dr;
            while (InBounds(targetFile, targetRank))
            {
                var target = grid[targetFile, targetRank];
                if (target is null)
                {
                    count++;
                }
                else
                {
                    if (target.Color != color)
                    {
                        count++;
                    }

                    break;
                }

                targetFile += df;
                targetRank += dr;
            }
        }

        return count;
    }

    private static bool InBounds(int file, int rank) => (uint)file < 8 && (uint)rank < 8;

    /// <summary>
    /// Parses only the placement field of a FEN into an [file, rank] grid plus the side-to-move
    /// token. Returns <c>false</c> for a malformed placement so the caller can fall back to a
    /// neutral score rather than throwing.
    /// </summary>
    private static bool TryParsePlacement(string fen, out Piece?[,] grid, out PlayerColor sideToMove)
    {
        grid = new Piece?[8, 8];
        sideToMove = PlayerColor.White;

        var fields = fen.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0)
        {
            return false;
        }

        var placement = fields[0];
        var ranks = placement.Split('/');
        if (ranks.Length != 8)
        {
            return false;
        }

        // FEN lists ranks from 8 down to 1; our grid index 0 == rank 1, so we read top-down and
        // store at the mirrored rank index.
        for (var fenRank = 0; fenRank < 8; fenRank++)
        {
            var rankIndex = 7 - fenRank;
            var fileIndex = 0;
            foreach (var ch in ranks[fenRank])
            {
                if (char.IsDigit(ch))
                {
                    fileIndex += ch - '0';
                    continue;
                }

                if (fileIndex >= 8)
                {
                    return false;
                }

                if (!TryCreatePiece(ch, out var piece))
                {
                    return false;
                }

                grid[fileIndex, rankIndex] = piece;
                fileIndex++;
            }

            if (fileIndex != 8)
            {
                return false;
            }
        }

        if (fields.Length >= 2)
        {
            sideToMove = fields[1] == "b" ? PlayerColor.Black : PlayerColor.White;
        }

        return true;
    }

    private static bool TryCreatePiece(char ch, out Piece piece)
    {
        var color = char.IsUpper(ch) ? PlayerColor.White : PlayerColor.Black;
        var type = char.ToLowerInvariant(ch) switch
        {
            'p' => (PieceType?)PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => null,
        };

        if (type is null)
        {
            piece = default!;
            return false;
        }

        piece = new Piece(type.Value, color);
        return true;
    }

    private static readonly (int, int)[] KnightOffsets =
    {
        ( 1,  2), ( 2,  1), ( 2, -1), ( 1, -2),
        (-1, -2), (-2, -1), (-2,  1), (-1,  2),
    };

    private static readonly (int, int)[] KingOffsets =
    {
        ( 1,  0), ( 1,  1), ( 0,  1), (-1,  1),
        (-1,  0), (-1, -1), ( 0, -1), ( 1, -1),
    };

    private static readonly (int, int)[] BishopDirections =
    {
        ( 1,  1), ( 1, -1), (-1,  1), (-1, -1),
    };

    private static readonly (int, int)[] RookDirections =
    {
        ( 1,  0), (-1,  0), ( 0,  1), ( 0, -1),
    };

    private static readonly (int, int)[] QueenDirections =
    {
        ( 1,  0), (-1,  0), ( 0,  1), ( 0, -1),
        ( 1,  1), ( 1, -1), (-1,  1), (-1, -1),
    };

    /// <summary>Internal value type carrying a parsed piece's type and color.</summary>
    private readonly struct Piece
    {
        public Piece(PieceType type, PlayerColor color)
        {
            Type = type;
            Color = color;
        }

        public PieceType Type { get; }

        public PlayerColor Color { get; }
    }

    /// <summary>
    /// A private mirror of the conventional chess piece types, so this service can stay self-
    /// contained and parse FEN without referencing a third-party engine's piece type. Kept private
    /// to avoid polluting the domain namespace.
    /// </summary>
    private enum PieceType
    {
        Pawn,
        Knight,
        Bishop,
        Rook,
        Queen,
        King,
    }
}
