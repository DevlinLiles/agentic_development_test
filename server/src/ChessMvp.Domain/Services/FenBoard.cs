using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// A minimal read-only view of a FEN position: the piece placement and the en passant target
/// square. It exists only to classify moves as captures and to look up the aggressor/victim
/// piece types for material scoring; full move legality is delegated to
/// <see cref="Abstractions.IChessRulesEngine"/>, so this type performs no rules checking of its own.
/// </summary>
internal sealed class FenBoard
{
    private readonly Dictionary<string, PieceType> _pieces;
    private readonly string? _enPassantTarget;

    private FenBoard(Dictionary<string, PieceType> pieces, string? enPassantTarget)
    {
        _pieces = pieces;
        _enPassantTarget = enPassantTarget;
    }

    public static FenBoard Parse(string fen)
    {
        // Case-insensitive keys so square lookups are robust to "E4" vs "e4".
        var pieces = new Dictionary<string, PieceType>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(fen))
        {
            return new FenBoard(pieces, null);
        }

        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0)
        {
            return new FenBoard(pieces, null);
        }

        // The placement field lists rank 8 first down to rank 1, separated by '/'. Within a rank
        // files run a..h left to right; a digit skips that many empty squares.
        var ranks = fields[0].Split('/');
        for (var rankIndex = 0; rankIndex < ranks.Length; rankIndex++)
        {
            var rankNumber = 8 - rankIndex;
            var fileIndex = 0;
            foreach (var ch in ranks[rankIndex])
            {
                if (char.IsDigit(ch))
                {
                    fileIndex += ch - '0';
                    continue;
                }

                var file = (char)('a' + fileIndex);
                pieces[$"{file}{rankNumber}"] = ParsePieceType(ch);
                fileIndex++;
            }
        }

        // Field 4 (index 3) is the en passant target square, or "-" when none is available.
        var enPassant = fields.Length > 3 && fields[3] != "-" ? fields[3] : null;

        return new FenBoard(pieces, enPassant);
    }

    /// <summary>
    /// Returns the piece occupying <paramref name="square"/>, or null when the square is empty.
    /// For a legal move the origin square is always occupied, so callers may treat a null result
    /// as a defensive fallback rather than an expected case.
    /// </summary>
    public PieceType? GetPieceAt(string square) =>
        _pieces.TryGetValue(square, out var piece) ? piece : null;

    /// <summary>
    /// Determines whether <paramref name="move"/> is a capture and, when it is, reports the
    /// victim's piece type. Normal captures are detected by an enemy piece on the destination;
    /// en passant is detected by a pawn moving diagonally onto the FEN en passant target square.
    /// </summary>
    public bool TryGetCapturedPiece(LegalMove move, out PieceType victim)
    {
        if (IsEnPassantCapture(move))
        {
            // The piece removed by an en passant capture is always a pawn.
            victim = PieceType.Pawn;
            return true;
        }

        if (_pieces.TryGetValue(move.ToSquare, out var destinationPiece))
        {
            victim = destinationPiece;
            return true;
        }

        victim = PieceType.Pawn;
        return false;
    }

    private bool IsEnPassantCapture(LegalMove move)
    {
        if (string.IsNullOrEmpty(_enPassantTarget))
        {
            return false;
        }

        if (!string.Equals(move.ToSquare, _enPassantTarget, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Only a pawn capturing diagonally lands on the en passant target; a quiet push (or any
        // non-pawn move) onto that square is not a capture.
        if (GetPieceAt(move.FromSquare) != PieceType.Pawn)
        {
            return false;
        }

        return IsDiagonalMove(move.FromSquare, move.ToSquare);
    }

    private static bool IsDiagonalMove(string from, string to)
    {
        if (from.Length < 2 || to.Length < 2)
        {
            return false;
        }

        var fileDelta = Math.Abs(from[0] - to[0]);
        var rankDelta = Math.Abs(from[1] - to[1]);
        return fileDelta == 1 && rankDelta == 1;
    }

    private static PieceType ParsePieceType(char c) => char.ToLowerInvariant(c) switch
    {
        'p' => PieceType.Pawn,
        'n' => PieceType.Knight,
        'b' => PieceType.Bishop,
        'r' => PieceType.Rook,
        'q' => PieceType.Queen,
        'k' => PieceType.King,
        _ => PieceType.Pawn,
    };
}
