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

    public IReadOnlyList<RulesLegalMove> GetAllLegalMoves(string fen, PlayerColor sideToMove)
    {
        // The from/to pairs come straight from the library's own move generation, exactly as
        // GetLegalDestinations does, so the two methods can never disagree about what is legal.
        if (!TryGetEngineMoves(fen, sideToMove, out var moves))
        {
            return Array.Empty<RulesLegalMove>();
        }

        var candidates = moves
            .Select(m => (From: m.OriginalPosition.ToString(), To: m.NewPosition.ToString()))
            // The library can emit one entry per promotion piece for a single from/to pair, while
            // promotion-piece choice is deferred to TryApplyMove by contract, so collapse to one
            // entry per from/to pair.
            .DistinctBy(pair => (pair.From.ToUpperInvariant(), pair.To.ToUpperInvariant()))
            .ToList();

        // SAN and the terminal-state flags are only knowable once a move has actually been played,
        // so every candidate is replayed through the existing apply path on a throw-away copy of
        // the position. That keeps a single source of truth for SAN/flag computation (the
        // enumeration is consistent with TryApplyMove by construction) and leaves the input
        // position untouched, preserving the stateless FEN-in/FEN-out contract.
        var result = new List<RulesLegalMove>(candidates.Count);
        foreach (var (from, to) in candidates)
        {
            var played = TryApplyMove(fen, sideToMove, from, to, promotion: null);

            result.Add(new RulesLegalMove
            {
                FromSquare = from,
                ToSquare = to,
                San = played.San ?? string.Empty,
                IsCheck = played.IsCheck,
                IsCheckmate = played.IsCheckmate,
                IsStalemate = played.IsStalemate,
                IsFiftyMoveDraw = played.IsFiftyMoveDraw,
            });
        }

        return result;
    }

    private bool TryGetEngineMoves(string fen, PlayerColor sideToMove, out IEnumerable<EngineMove> moves)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            moves = Enumerable.Empty<EngineMove>();
            return false;
        }

        // A side that is not to move in the given position simply has no legal moves; like an
        // unparseable FEN, that is reported as an empty enumeration rather than by throwing.
        if (board.Turn != ToEngineColor(sideToMove))
        {
            moves = Enumerable.Empty<EngineMove>();
            return false;
        }

        moves = board.Moves();
        return true;
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
