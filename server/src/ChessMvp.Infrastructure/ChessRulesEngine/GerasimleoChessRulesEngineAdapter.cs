using Chess;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using EngineMove = Chess.Move;

namespace ChessMvp.Infrastructure.ChessRulesEngine;

/// <summary>
/// Wraps the Gera.Chess (Geras1mleo) library behind <see cref="IChessRulesEngine"/>. If this
/// library ever needs to be swapped (e.g. for ChessDotNet), only this class and its tests change.
/// </summary>
public sealed class GerasimleoChessRulesEngineAdapter : IChessRulesEngine
{
    // Checkmate/stalemate detection is always on in the underlying library. Only the fifty-move
    // rule is enabled here to match MVP scope; insufficient-material and threefold-repetition
    // draws are deferred per the product spec, so they are deliberately left disabled rather than
    // silently ending games in ways GameResultReason can't represent.
    private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;

    public MoveApplicationResult TryApplyMove(
        string fen,
        PlayerColor sideToMove,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return MoveApplicationResult.Illegal(MoveFailureReason.InvalidFen);
        }

        if (board.Turn != ToEngineColor(sideToMove))
        {
            return MoveApplicationResult.Illegal(MoveFailureReason.WrongSideToMove);
        }

        board.OnPromotePawn += (_, e) => e.PromotionResult = ToPromotionType(promotion);

        var move = new EngineMove(fromSquare, toSquare);

        bool applied;
        try
        {
            applied = board.Move(move);
        }
        catch (ChessPieceNotFoundException)
        {
            return MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove);
        }
        catch (ChessGameEndedException)
        {
            return MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove);
        }

        if (!applied)
        {
            return MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove);
        }

        var endgame = board.EndGame;
        return new MoveApplicationResult
        {
            IsLegal = true,
            San = move.San,
            ResultingFen = board.ToFen(),
            IsCheck = move.IsCheck,
            IsCheckmate = move.IsMate,
            IsStalemate = endgame?.EndgameType == EndgameType.Stalemate,
            IsFiftyMoveDraw = endgame?.EndgameType == EndgameType.FiftyMoveRule,
        };
    }

    public IReadOnlySet<string> GetLegalDestinations(string fen, string fromSquare)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return new HashSet<string>();
        }

        return board.Moves()
            .Where(m => string.Equals(m.OriginalPosition.ToString(), fromSquare, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.NewPosition.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsPromotionMove(string fen, string fromSquare, string toSquare)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return false;
        }

        var piece = board[fromSquare];
        if (piece is null || piece.Type != PieceType.Pawn)
        {
            return false;
        }

        var destinationRank = toSquare[^1];
        return destinationRank is '8' or '1';
    }

    /// <summary>
    /// Generates the complete set of legal moves for the side to move in <paramref name="fen"/>.
    /// The underlying library's <c>Moves()</c> already yields only legal moves — it filters out
    /// self-check (a move leaving one's own king attacked is never returned) — so the work here is
    /// to enumerate those moves and expand each pawn promotion into its four legal promotion
    /// pieces, classifying each move as normal/castling/en-passant/promotion.
    /// </summary>
    public IReadOnlyList<LegalMove> GetLegalMoves(string fen)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return Array.Empty<LegalMove>();
        }

        var enPassantTarget = ParseEnPassantTarget(fen);
        var results = new List<LegalMove>();

        foreach (var move in board.Moves())
        {
            var from = move.OriginalPosition.ToString();
            var to = move.NewPosition.ToString();

            // Resolve the moving piece's type via the board indexer (the same access pattern the
            // rest of this adapter and the AI rely on). `var` avoids depending on the library's
            // concrete piece type name, which is not part of the stable seam we expose.
            var piece = board[from];
            var pieceType = piece?.Type;

            if (pieceType == PieceType.Pawn)
            {
                if (to == enPassantTarget)
                {
                    // En-passant: a pawn moving onto the FEN's en-passant target square. The
                    // library only generates this diagonal pawn move when an enemy pawn was just
                    // double-pushed, so matching the target square reliably identifies it.
                    results.Add(new LegalMove(from, to, null, MoveKind.EnPassant));
                    continue;
                }

                if (to[^1] is '1' or '8')
                {
                    // Promotion: the library generates one entry per reach (defaulting to queen
                    // when played), but all four promotion pieces are legal for the same from/to
                    // squares. Expand them so perft and callers see the full move set.
                    foreach (var promotionPiece in AllPromotions)
                    {
                        results.Add(new LegalMove(from, to, promotionPiece, MoveKind.Promotion));
                    }

                    continue;
                }
            }

            var kind = ClassifyMove(pieceType, from, to);
            results.Add(new LegalMove(from, to, null, kind));
        }

        return results;
    }

    /// <summary>
    /// Distinguishes castling from other king moves. Castling is the only move in which a king
    /// travels exactly two squares along its back rank toward one of its rooks. En-passant and
    /// promotion are handled by the caller before this is reached for pawns.
    /// </summary>
    private static MoveKind ClassifyMove(PieceType? pieceType, string from, string to)
    {
        if (pieceType == PieceType.King)
        {
            var fileDelta = Math.Abs(to[0] - from[0]);
            var rankDelta = Math.Abs(to[1] - from[1]);
            if (fileDelta == 2 && rankDelta == 0)
            {
                return MoveKind.Castling;
            }
        }

        return MoveKind.Normal;
    }

    private static readonly PromotionPieceType[] AllPromotions =
    {
        PromotionPieceType.Queen,
        PromotionPieceType.Rook,
        PromotionPieceType.Bishop,
        PromotionPieceType.Knight,
    };

    private static string? ParseEnPassantTarget(string fen)
    {
        var fields = fen.Split(' ');
        if (fields.Length <= 3)
        {
            return null;
        }

        var target = fields[3];
        return target == "-" ? null : target;
    }

    private static PieceColor ToEngineColor(PlayerColor color) =>
        color == PlayerColor.White ? PieceColor.White : PieceColor.Black;

    private static PromotionType ToPromotionType(PromotionPieceType? promotion) => promotion switch
    {
        PromotionPieceType.Rook => PromotionType.ToRook,
        PromotionPieceType.Bishop => PromotionType.ToBishop,
        PromotionPieceType.Knight => PromotionType.ToKnight,
        _ => PromotionType.ToQueen,
    };
}
