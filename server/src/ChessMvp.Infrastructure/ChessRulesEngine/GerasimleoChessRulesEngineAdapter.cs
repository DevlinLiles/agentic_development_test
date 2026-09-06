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

        return IsPawnPromotion(board, fromSquare, toSquare);
    }

    public IReadOnlyList<LegalMove> GetAllLegalMoves(string fen)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return Array.Empty<LegalMove>();
        }

        // The board is terminal (checkmate/stalemate) when there are no legal moves; Moves() simply
        // yields nothing in that case, so no special handling is required beyond mapping each move.
        return board.Moves()
            .Select(m => ToLegalMove(board, m))
            .ToList();
    }

    private static LegalMove ToLegalMove(ChessBoard board, EngineMove move)
    {
        var fromSquare = move.OriginalPosition.ToString();
        var toSquare = move.NewPosition.ToString();

        return new LegalMove
        {
            FromSquare = fromSquare,
            ToSquare = toSquare,
            San = move.San,
            IsCheck = move.IsCheck,
            IsCheckmate = move.IsMate,
            // Reuse the same pawn-to-back-rank heuristic as IsPromotionMove so the two paths stay
            // consistent: a move is a promotion iff a pawn departs for the 1st/8th rank.
            IsPromotion = IsPawnPromotion(board, fromSquare, toSquare),
        };
    }

    // Shared promotion heuristic used by both IsPromotionMove and GetAllLegalMoves so the public
    // predicate and the enumerated LegalMove.IsPromotion flag can never disagree.
    private static bool IsPawnPromotion(ChessBoard board, string fromSquare, string toSquare)
    {
        var piece = board[fromSquare];
        if (piece is null || piece.Type != PieceType.Pawn)
        {
            return false;
        }

        var destinationRank = toSquare[^1];
        return destinationRank is '8' or '1';
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
