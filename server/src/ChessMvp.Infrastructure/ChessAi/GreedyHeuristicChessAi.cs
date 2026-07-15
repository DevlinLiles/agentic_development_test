using Chess;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using EngineMove = Chess.Move;

namespace ChessMvp.Infrastructure.ChessAi;

/// <summary>
/// A deliberately simple, one-ply greedy chess AI. For every legal move it:
///   1. applies the move on a throwaway board,
///   2. scores the resulting position with a material-balance heuristic plus small
///      check/checkmate bonuses, and
///   3. picks the highest-scoring move (ties broken deterministically by SAN so repeated runs
///      are reproducible — essential for unit tests).
/// This is intentionally not a strong engine — it's a "basic heuristic" opponent per the MVP
/// scope: it grabs material when it can (the material term rewards captures), plays a mate
/// whenever one is on the board, and otherwise prefers checking moves. A real search
/// (minimax/alpha-beta) is left for a later phase; only this class changes if/when that lands.
/// </summary>
public sealed class GreedyHeuristicChessAi : IChessAi
{
    // Standard piece values, in pawn units. The king is given a large value purely as a safe
    // sentinel; in a legal position it can never actually be captured.
    private static readonly Dictionary<PieceType, int> MaterialValue = new()
    {
        [PieceType.Pawn] = 100,
        [PieceType.Knight] = 320,
        [PieceType.Bishop] = 330,
        [PieceType.Rook] = 500,
        [PieceType.Queen] = 900,
        [PieceType.King] = 100_000,
    };

    private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;

    public AiMove? ChooseMove(string fen)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return null;
        }

        var legalMoves = board.Moves().ToList();
        if (legalMoves.Count == 0)
        {
            // No legal moves: terminal position already classified by the rules engine on the
            // move that produced `fen`. Nothing for the AI to do.
            return null;
        }

        var sideToMove = board.Turn;

        ScoredMove? best = null;
        foreach (var move in legalMoves)
        {
            var scored = ScoreMove(board, move, sideToMove);

            // Deterministic tie-break on SAN so repeated runs against the same position pick the
            // same move — essential for unit tests and for reproducible AI games.
            if (best is null
                || scored.Score > best.Score
                || (scored.Score == best.Score
                    && string.CompareOrdinal(scored.San, best.San) < 0))
            {
                best = scored;
            }
        }

        return best is null
            ? null
            : new AiMove(best.From, best.To, best.Promotion);
    }

    private static ScoredMove ScoreMove(ChessBoard board, EngineMove move, PieceColor sideToMove)
    {
        // Apply on a freshly-loaded board so the caller's board is untouched and we can evaluate
        // the position *after* our move (which is what a greedy one-ply eval needs).
        if (!ChessBoard.TryLoadFromFen(board.ToFen(), out var after, EnabledDrawRules))
        {
            // Should never happen — we just serialised this board — but treat as a worst case.
            return ScoredMove.Illegal(move);
        }

        // The library raises this event for promotions; default to queen, which a greedy one-ply
        // eval almost always scores highest.
        after.OnPromotePawn += (_, e) => e.PromotionResult = PromotionType.ToQueen;

        try
        {
            if (!after.Move(move))
            {
                return ScoredMove.Illegal(move);
            }
        }
        catch (Exception)
        {
            // The library throws for some illegal-at-the-engine-level moves; treat as unplayable.
            return ScoredMove.Illegal(move);
        }

        var score = EvaluateMaterial(after, sideToMove);

        // A move that delivers checkmate dominates every other score, so the AI always plays a
        // mate when one is on the board. `move.IsMate` is set by the library for the move that
        // produces mate, so we don't need to inspect the post-move endgame struct.
        if (move.IsMate)
        {
            score += 10_000_000;
        }

        // Small bonus for giving check (encourages forcing moves over quiet ones among
        // otherwise-equal material positions).
        if (move.IsCheck)
        {
            score += 50;
        }

        var promotion = ResolvePromotion(board, move);
        return new ScoredMove(
            move.OriginalPosition.ToString(),
            move.NewPosition.ToString(),
            move.San,
            promotion,
            score);
    }

    /// <summary>
    /// Material balance (our pieces minus opponent's), evaluated from
    /// <paramref name="sideToMove"/>'s perspective. Captures naturally score highly because the
    /// captured piece vanishes from the opponent's total.
    /// </summary>
    private static int EvaluateMaterial(ChessBoard board, PieceColor sideToMove)
    {
        var material = 0;

        // ChessBoard doesn't expose a piece iterator in a stable form across library versions,
        // so walk the 64 squares directly. File a..h, rank 1..8 — the library's indexer accepts
        // algebraic notation and returns a typed, nullable piece with .Type/.Color (the same
        // members the rules-engine adapter relies on).
        for (var file = 'a'; file <= 'h'; file++)
        {
            for (var rank = 1; rank <= 8; rank++)
            {
                var piece = board[$"{file}{rank}"];
                if (piece is null)
                {
                    continue;
                }

                var value = MaterialValue.TryGetValue(piece.Type, out var v) ? v : 0;
                if (piece.Color == sideToMove)
                {
                    material += value;
                }
                else
                {
                    material -= value;
                }
            }
        }

        return material;
    }

    /// <summary>
    /// Determines whether <paramref name="move"/> is a pawn promotion and, if so, reports the
    /// promotion piece. The AI defaults promotions to queen (set in the OnPromotePawn handler
    /// above) because a queen is almost always the highest-scoring promotion for a greedy
    /// one-ply eval.
    /// </summary>
    private static PromotionPieceType? ResolvePromotion(ChessBoard board, EngineMove move)
    {
        var piece = board[move.OriginalPosition.ToString()];
        if (piece is null || piece.Type != PieceType.Pawn)
        {
            return null;
        }

        var destinationRank = move.NewPosition.ToString()[^1];
        if (destinationRank is not ('8' or '1'))
        {
            return null;
        }

        return PromotionPieceType.Queen;
    }

    private sealed record ScoredMove(
        string From,
        string To,
        string San,
        PromotionPieceType? Promotion,
        int Score)
    {
        public static ScoredMove Illegal(EngineMove move) => new(
            move.OriginalPosition.ToString(),
            move.NewPosition.ToString(),
            move.San,
            null,
            int.MinValue);
    }
}
