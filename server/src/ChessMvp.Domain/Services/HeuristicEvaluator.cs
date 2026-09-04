using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// Deterministic static evaluator combining material values, piece-square tables, and a
/// mobility term. The board is supplied as a FEN string (the universal board representation
/// used throughout the domain). The score is side-relative: positive favours <c>color</c>.
/// </summary>
public sealed class HeuristicEvaluator : IHeuristicEvaluator
{
    private readonly IChessRulesEngine _rulesEngine;

    /// <summary>
    /// Weight applied to the mobility term (legal-move count for the evaluated side), in
    /// centipawns per move. Kept small so positional/material signals dominate.
    /// </summary>
    public const int MobilityWeight = 2;

    // Material values per piece type in centipawns. The king is assigned a value so that every
    // piece type has one; in legal positions both sides have exactly one king so it cancels in
    // the side-relative difference.
    private static readonly IReadOnlyDictionary<char, int> MaterialValues = new Dictionary<char, int>
    {
        ['P'] = 100,
        ['N'] = 320,
        ['B'] = 330,
        ['R'] = 500,
        ['Q'] = 900,
        ['K'] = 20000,
    };

    // Piece-square tables, indexed [row * 8 + col] where row 0 == rank 8 (the order FEN lists
    // ranks in) and col 0 == file 'a'. Values are written from White's perspective; a Black
    // piece reads its bonus from the vertically mirrored square so the tables stay symmetric.
    private static readonly IReadOnlyDictionary<char, int[]> PieceSquareTables = new Dictionary<char, int[]>
    {
        ['P'] = [
            0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
            5,  5, 10, 25, 25, 10,  5,  5,
            0,  0,  0, 20, 20,  0,  0,  0,
            5, -5,-10,  0,  0,-10, -5,  5,
            5, 10, 10,-20,-20, 10, 10,  5,
            0,  0,  0,  0,  0,  0,  0,  0,
        ],
        ['N'] = [
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
        ],
        ['B'] = [
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20,
        ],
        ['R'] = [
            0,  0,  0,  0,  0,  0,  0,  0,
            5, 10, 10, 10, 10, 10, 10,  5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            0,  0,  0,  5,  5,  0,  0,  0,
        ],
        ['Q'] = [
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
            -5,  0,  5,  5,  5,  5,  0, -5,
            0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20,
        ],
        ['K'] = [
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            20, 20,  0,  0,  0,  0, 20, 20,
            20, 30, 10,  0,  0, 10, 30, 20,
        ],
    };

    public HeuristicEvaluator(IChessRulesEngine rulesEngine)
    {
        _rulesEngine = rulesEngine;
    }

    /// <summary>
    /// Evaluates <paramref name="board"/> (a FEN string) from <paramref name="color"/>'s
    /// perspective. Returns 0 for an invalid FEN. The result is deterministic and side-relative.
    /// </summary>
    public int Evaluate(string board, PlayerColor color)
    {
        if (string.IsNullOrWhiteSpace(board) ||
            !TryParsePlacement(board, out var ranks, out _))
        {
            return 0;
        }

        long whiteScore = 0;
        long blackScore = 0;

        for (var rowIndex = 0; rowIndex < ranks.Length; rowIndex++)
        {
            var rank = ranks[rowIndex];
            var fileIndex = 0;
            foreach (var ch in rank)
            {
                if (char.IsDigit(ch))
                {
                    fileIndex += ch - '0';
                    continue;
                }

                if (fileIndex >= 8)
                {
                    break; // malformed rank; ignore the remainder
                }

                var type = char.ToUpperInvariant(ch);
                var isWhite = char.IsUpper(ch);

                if (MaterialValues.TryGetValue(type, out var material) &&
                    PieceSquareTables.TryGetValue(type, out var table))
                {
                    // White reads the table directly (row 0 == rank 8); Black reads the
                    // vertically mirrored row so the tables remain perspective-symmetric.
                    var pstIndex = isWhite
                        ? rowIndex * 8 + fileIndex
                        : (7 - rowIndex) * 8 + fileIndex;
                    var pst = table[pstIndex];

                    if (isWhite)
                    {
                        whiteScore += material + pst;
                    }
                    else
                    {
                        blackScore += material + pst;
                    }
                }

                fileIndex++;
            }
        }

        var own = color == PlayerColor.White ? whiteScore : blackScore;
        var opp = color == PlayerColor.White ? blackScore : whiteScore;

        var mobility = CountLegalMoves(board, color);
        var total = (own - opp) + (long)MobilityWeight * mobility;
        return (int)total;
    }

    /// <summary>
    /// Counts the legal moves available to <paramref name="color"/>. When <paramref name="color"/>
    /// is already the side to move in <paramref name="fen"/> the rules engine is queried directly;
    /// otherwise the FEN is rebuilt with <paramref name="color"/> to move (en passant cleared, since
    /// the original target applied to the other side). An illegal rebuilt position yields no moves,
    /// giving a deterministic zero mobility contribution.
    /// </summary>
    private int CountLegalMoves(string fen, PlayerColor color)
    {
        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
        {
            return 0;
        }

        var colorChar = color == PlayerColor.White ? 'w' : 'b';

        if (fields[1].Length > 0 && fields[1][0] == colorChar)
        {
            return _rulesEngine.GetAllLegalMoves(fen).Count;
        }

        var castling = fields.Length > 2 && !string.IsNullOrEmpty(fields[2]) ? fields[2] : "-";
        var halfmove = fields.Length > 4 ? fields[4] : "0";
        var fullmove = fields.Length > 5 ? fields[5] : "1";
        var flippedFen = $"{fields[0]} {colorChar} {castling} - {halfmove} {fullmove}";

        return _rulesEngine.GetAllLegalMoves(flippedFen).Count;
    }

    private static bool TryParsePlacement(string fen, out string[] ranks, out char sideToMove)
    {
        ranks = Array.Empty<string>();
        sideToMove = '\0';

        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
        {
            return false;
        }

        ranks = fields[0].Split('/');
        if (ranks.Length != 8)
        {
            return false;
        }

        sideToMove = fields[1].Length > 0 ? fields[1][0] : '\0';
        return sideToMove is 'w' or 'b';
    }
}
