using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// A deterministic static board evaluator used by heuristic AI players to score positions during
/// search. The score is returned from the perspective of the side to move: positive values favour
/// the side to move, negative values favour the opponent. Given the same FEN (and therefore the
/// same set of legal moves), this evaluator always returns the same score &mdash; it holds no
/// mutable state and performs no IO.
/// </summary>
public sealed class ChessBoardEvaluator : IChessBoardEvaluator
{
    /// <summary>
    /// Weight applied to the legal-move-count (mobility) term relative to a single centipawn of
    /// material. Kept small so mobility only acts as a tie-breaker between materially equal
    /// positions rather than dominating the evaluation.
    /// </summary>
    private const int MobilityWeight = 2;

    private readonly IChessRulesEngine _rulesEngine;

    public ChessBoardEvaluator(IChessRulesEngine rulesEngine)
    {
        _rulesEngine = rulesEngine;
    }

    /// <summary>
    /// Scores the position described by <paramref name="fen"/>. The result is from the
    /// perspective of the side to move: a positive score means the side to move is ahead, a
    /// negative score means it is behind.
    /// </summary>
    /// <param name="fen">The FEN string of the position to evaluate.</param>
    /// <returns>A single integer score in centipawns from the side-to-move's perspective.</returns>
    public int Evaluate(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            return 0;
        }

        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0)
        {
            return 0;
        }

        var placement = fields[0];
        var sideToMove = ParseSideToMove(fields);

        var (whiteMaterial, blackMaterial) = TallyMaterial(placement);
        var (whitePst, blackPst) = TallyPieceSquareTables(placement);

        // Material + PST are accumulated as "white minus black"; flipping the sign for the
        // side-to-move's perspective keeps the return contract uniform regardless of who is on
        // the move.
        var whiteScore = whiteMaterial + whitePst;
        var blackScore = blackMaterial + blackPst;
        var materialPerspective = whiteScore - blackScore;

        var mobility = CountMobility(fen);

        // Mobility always counts the side-to-move's legal moves, so it is already in that side's
        // favour; we add it directly (scaled) rather than subtracting.
        return sideToMove == PlayerColor.White
            ? materialPerspective + mobility * MobilityWeight
            : -materialPerspective + mobility * MobilityWeight;
    }

    private static (int White, int Black) TallyMaterial(string placement)
    {
        var white = 0;
        var black = 0;

        foreach (var ch in placement)
        {
            if (char.IsWhiteSpace(ch))
            {
                break;
            }

            if (!PieceMaterial.TryGetValue(char.ToLowerInvariant(ch), out var value))
            {
                // Rank separators ('/') and run-length digits are not pieces.
                continue;
            }

            if (char.IsUpper(ch))
            {
                white += value;
            }
            else
            {
                black += value;
            }
        }

        return (white, black);
    }

    private static (int White, int Black) TallyPieceSquareTables(string placement)
    {
        var white = 0;
        var black = 0;

        var ranks = placement.Split('/');
        if (ranks.Length != 8)
        {
            return (white, black);
        }

        // FEN ranks are listed from rank 8 (top, black's back rank) down to rank 1 (white's back
        // rank), so ranks[fenRow] describes rank (8 - fenRow). The piece-square tables use the
        // standard PeSTO orientation where row 0 == rank 8 and column 0 == file a; therefore a
        // white piece on FEN row `fenRow` indexes table[fenRow][file]. Black's perspective is the
        // vertical mirror of White's, so a black piece on FEN row `fenRow` indexes
        // table[7 - fenRow][file] (equivalent to evaluating the mirrored white piece).
        for (var fenRow = 0; fenRow < 8; fenRow++)
        {
            var row = ranks[fenRow];
            var file = 0;

            foreach (var ch in row)
            {
                if (char.IsDigit(ch))
                {
                    // Empty squares are encoded as a run-length count.
                    file += ch - '0';
                    continue;
                }

                if (file < 8 && PieceSquareTables.TryGetValue(char.ToLowerInvariant(ch), out var table))
                {
                    if (char.IsUpper(ch))
                    {
                        white += table[fenRow][file];
                    }
                    else
                    {
                        black += table[7 - fenRow][file];
                    }
                }

                file++;
            }
        }

        return (white, black);
    }

    private int CountMobility(string fen)
    {
        // Mobility is the number of legal moves available to the side to move. Because the rules
        // engine is FEN-in/FEN-out and stateless, the same FEN always yields the same count, which
        // is what keeps Evaluate pure.
        var legalMoves = _rulesEngine.GetAllLegalMoves(fen);
        return legalMoves.Count;
    }

    private static PlayerColor ParseSideToMove(string[] fenFields)
    {
        // Field index 1 is the active-colour token ('w' / 'b'). Default to White when the FEN is
        // malformed so evaluation degrades gracefully rather than throwing.
        if (fenFields.Length > 1)
        {
            return fenFields[1].Equals("b", StringComparison.OrdinalIgnoreCase)
                ? PlayerColor.Black
                : PlayerColor.White;
        }

        return PlayerColor.White;
    }

    /// <summary>
    /// Material value in centipawns for each piece type. King has no material value (it is never
    /// captured) but is included so the table covers every piece type as required.
    /// </summary>
    private static readonly Dictionary<char, int> PieceMaterial = new()
    {
        ['p'] = 100,
        ['n'] = 320,
        ['b'] = 330,
        ['r'] = 500,
        ['q'] = 900,
        ['k'] = 0,
    };

    /// <summary>
    /// Piece-square tables indexed by lower-case piece character. Each table is an 8x8 grid in the
    /// standard PeSTO orientation: row 0 is rank 8 (black's back rank), column 0 is file a. Values
    /// encourage central pawns and knights toward the centre / away from the edges. Pawns and
    /// knights are defined per the acceptance criteria; bishops, rooks, the queen and the king are
    /// provided as well for a complete, consistent evaluation.
    /// </summary>
    private static readonly Dictionary<char, int[][]> PieceSquareTables = new()
    {
        ['p'] = PawnTable,
        ['n'] = KnightTable,
        ['b'] = BishopTable,
        ['r'] = RookTable,
        ['q'] = QueenTable,
        ['k'] = KingTable,
    };

    // Pawn table: row 0 == rank 8. Rewards advancement and central control; the back ranks
    // (where pawns cannot stand) are zero.
    private static readonly int[][] PawnTable =
    {
        new[] {  0,  0,  0,  0,  0,  0,  0,  0 }, // rank 8
        new[] { 50, 50, 50, 50, 50, 50, 50, 50 }, // rank 7 (about to promote)
        new[] { 10, 10, 20, 30, 30, 20, 10, 10 }, // rank 6
        new[] {  5,  5, 10, 25, 25, 10,  5,  5 }, // rank 5
        new[] {  0,  0,  0, 20, 20,  0,  0,  0 }, // rank 4
        new[] {  5, -5,-10,  0,  0,-10, -5,  5 }, // rank 3
        new[] {  5, 10, 10,-20,-20, 10, 10,  5 }, // rank 2 (start)
        new[] {  0,  0,  0,  0,  0,  0,  0,  0 }, // rank 1
    };

    // Knight table: central squares are best, corners are worst.
    private static readonly int[][] KnightTable =
    {
        new[] { -50,-40,-30,-30,-30,-30,-40,-50 }, // rank 8
        new[] { -40,-20,  0,  0,  0,  0,-20,-40 }, // rank 7
        new[] { -30,  0, 10, 15, 15, 10,  0,-30 }, // rank 6
        new[] { -30,  5, 15, 20, 20, 15,  5,-30 }, // rank 5
        new[] { -30,  0, 15, 20, 20, 15,  0,-30 }, // rank 4
        new[] { -30,  5, 10, 15, 15, 10,  5,-30 }, // rank 3
        new[] { -40,-20,  0,  5,  5,  0,-20,-40 }, // rank 2
        new[] { -50,-40,-30,-30,-30,-30,-40,-50 }, // rank 1
    };

    private static readonly int[][] BishopTable =
    {
        new[] { -20,-10,-10,-10,-10,-10,-10,-20 },
        new[] { -10,  0,  0,  0,  0,  0,  0,-10 },
        new[] { -10,  0,  5, 10, 10,  5,  0,-10 },
        new[] { -10,  5,  5, 10, 10,  5,  5,-10 },
        new[] { -10,  0, 10, 10, 10, 10,  0,-10 },
        new[] { -10, 10, 10, 10, 10, 10, 10,-10 },
        new[] { -10,  5,  0,  0,  0,  0,  5,-10 },
        new[] { -20,-10,-10,-10,-10,-10,-10,-20 },
    };

    private static readonly int[][] RookTable =
    {
        new[] {  0,  0,  0,  0,  0,  0,  0,  0 },
        new[] {  5, 10, 10, 10, 10, 10, 10,  5 },
        new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
        new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
        new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
        new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
        new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
        new[] {  0,  0,  0,  5,  5,  0,  0,  0 },
    };

    private static readonly int[][] QueenTable =
    {
        new[] { -20,-10,-10, -5, -5,-10,-10,-20 },
        new[] { -10,  0,  0,  0,  0,  0,  0,-10 },
        new[] { -10,  0,  5,  5,  5,  5,  0,-10 },
        new[] {  -5,  0,  5,  5,  5,  5,  0, -5 },
        new[] {   0,  0,  5,  5,  5,  5,  0, -5 },
        new[] { -10,  5,  5,  5,  5,  5,  0,-10 },
        new[] { -10,  0,  5,  0,  0,  0,  0,-10 },
        new[] { -20,-10,-10, -5, -5,-10,-10,-20 },
    };

    private static readonly int[][] KingTable =
    {
        new[] { -30,-40,-40,-50,-50,-40,-40,-30 }, // rank 8
        new[] { -30,-40,-40,-50,-50,-40,-40,-30 }, // rank 7
        new[] { -30,-40,-40,-50,-50,-40,-40,-30 }, // rank 6
        new[] { -30,-40,-40,-50,-50,-40,-40,-30 }, // rank 5
        new[] { -20,-30,-30,-40,-40,-30,-30,-20 }, // rank 4
        new[] { -10,-20,-20,-20,-20,-20,-20,-10 }, // rank 3
        new[] {  20, 20,  0,  0,  0,  0, 20, 20 }, // rank 2
        new[] {  20, 30, 10,  0,  0, 10, 30, 20 }, // rank 1 (castled / safe)
    };
}
