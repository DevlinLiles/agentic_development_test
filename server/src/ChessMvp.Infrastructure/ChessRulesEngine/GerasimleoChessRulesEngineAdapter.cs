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
    /// Enumerates every legal move for the side to move, mapping the underlying library's
    /// <see cref="EngineMove"/> onto <see cref="LegalMove"/> values. Promotion is detected by
    /// the same heuristic used in <see cref="IsPromotionMove"/>: the moving piece is a pawn and
    /// the destination square sits on the back rank (file '8' for white, '1' for black).
    /// </summary>
    public IReadOnlyList<LegalMove> GetAllLegalMoves(string fen)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return Array.Empty<LegalMove>();
        }

        return board.Moves()
            .Select(m => ToLegalMove(board, m))
            .ToList();
    }

    private static LegalMove ToLegalMove(ChessBoard board, EngineMove move)
    {
        var from = move.OriginalPosition.ToString();
        var to = move.NewPosition.ToString();

        return new LegalMove(
            FromSquare: from,
            ToSquare: to,
            San: move.San,
            IsPromotion: IsPawnPromotion(board, from, to));
    }

    /// <summary>
    /// Reuses the <see cref="IsPromotionMove"/> heuristic but without re-loading the board: the
    /// caller already has a populated <paramref name="board"/>, so the piece lookup is cheap.
    /// </summary>
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
