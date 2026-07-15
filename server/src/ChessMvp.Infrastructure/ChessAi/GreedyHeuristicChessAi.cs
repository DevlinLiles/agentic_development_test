using Chess;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using EngineMove = Chess.Move;

namespace ChessMvp.Infrastructure.ChessAi;

/// <summary>
/// The computer opponent. For a given FEN it returns exactly one move chosen from
/// <see cref="ChessBoard.Moves"/>, using a material-balance evaluation plus a fixed-depth
/// negamax/minimax search (alpha-beta pruned). The selection is guaranteed to be legal under
/// the move generator, and when the side to move is already in a terminal state (no legal
/// moves) the AI returns <see langword="null"/> rather than raising — the move that produced
/// the position already classified the game end via the rules engine.
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

    // Search depth (plies). Two is enough for a "basic heuristic" opponent per the MVP scope —
    // it avoids the horizon effect on immediate captures and mates while staying fast on the
    // API's stateless hot path. Only this constant changes if a stronger engine is wanted.
    private const int SearchDepth = 2;

    // Mate score sentinel. Mate scores are offset by the search ply so a mate found in fewer
    // plies always beats one found in more plies (a sooner mate is strictly better).
    private const int MateScore = 10_000_000;

    public AiMove? ChooseMove(string fen)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            // An unparseable FEN is treated as a terminal/non-playable state: the AI has nothing
            // to do and must not raise.
            return null;
        }

        var legalMoves = board.Moves().ToList();
        if (legalMoves.Count == 0)
        {
            // Terminal-state short-circuit: no legal moves means checkmate/stalemate, already
            // classified by the rules engine on the move that produced `fen`. The AI returns no
            // move (sentinel) rather than raising.
            return null;
        }

        ScoredMove? best = null;
        // Full-width window at the root; alpha-beta narrows it as we descend.
        var alpha = int.MinValue + 1;
        var beta = int.MaxValue - 1;

        foreach (var move in legalMoves)
        {
            // Every move here came straight from the move generator, so it is legal by
            // construction. We always score it (engine-level failures map to the worst score)
            // so that, as long as there is at least one legal move, `best` is always set and we
            // return exactly one move from legal_moves(state).
            var value = ScoreRootMove(board, move, SearchDepth - 1, -beta, -alpha);

            if (best is null
                || value > best.Score
                || (value == best.Score
                    && string.CompareOrdinal(move.San, best.San) < 0))
            {
                best = new ScoredMove(
                    move.OriginalPosition.ToString(),
                    move.NewPosition.ToString(),
                    move.San,
                    ResolvePromotion(board, move),
                    value);
            }

            if (value > alpha)
            {
                alpha = value;
            }
        }

        return best is null
            ? null
            : new AiMove(best.From, best.To, best.Promotion);
    }

    /// <summary>
    /// Plays <paramref name="move"/> on a throwaway copy of <paramref name="board"/> and scores
    /// the resulting position from the root mover's perspective via negamax. The child's score
    /// is from the opponent's perspective, so it is negated to return the root mover's score.
    /// </summary>
    private static int ScoreRootMove(
        ChessBoard board,
        EngineMove move,
        int depth,
        int alpha,
        int beta)
    {
        var after = CloneBoard(board);
        if (after is null)
        {
            return int.MinValue + 1;
        }

        after.OnPromotePawn += (_, e) => e.PromotionResult = PromotionType.ToQueen;

        bool applied;
        try
        {
            applied = after.Move(move);
        }
        catch (Exception)
        {
            return int.MinValue + 1;
        }

        if (!applied)
        {
            return int.MinValue + 1;
        }

        // Terminal positions (e.g. a delivered mate) are detected inside Negamax and scored
        // accordingly, so a mate here surfaces as +MateScore from the root mover's perspective.
        return -Negamax(after, depth, -beta, -alpha);
    }

    /// <summary>
    /// Negamax with alpha-beta pruning. Returns the score of the position from the perspective
    /// of the side to move on <paramref name="board"/>. Terminal positions are short-circuited
    /// before the depth check: checkmate scores a large negative value (worst for the side to
    /// move), and stalemate/fifty-move draws score zero.
    /// </summary>
    private static int Negamax(ChessBoard board, int depth, int alpha, int beta)
    {
        var legalMoves = board.Moves().ToList();

        // Terminal-state short-circuit: no legal moves ⇒ checkmate or stalemate/draw.
        if (legalMoves.Count == 0)
        {
            var endgame = board.EndGame;
            if (endgame is not null
                && (endgame.EndgameType == EndgameType.Stalemate
                    || endgame.EndgameType == EndgameType.FiftyMoveRule))
            {
                return 0;
            }

            // Checkmate (or any non-draw terminal): worst case for the side to move. Offset by
            // remaining depth so a mate delivered sooner is strictly preferred.
            return -MateScore + (SearchDepth - depth);
        }

        if (depth <= 0)
        {
            // Leaf: static material evaluation from the side-to-move's perspective.
            return EvaluateMaterial(board, board.Turn);
        }

        var best = int.MinValue + 1;
        foreach (var move in legalMoves)
        {
            var after = CloneBoard(board);
            if (after is null)
            {
                continue;
            }

            after.OnPromotePawn += (_, e) => e.PromotionResult = PromotionType.ToQueen;

            bool applied;
            try
            {
                applied = after.Move(move);
            }
            catch (Exception)
            {
                continue;
            }

            if (!applied)
            {
                continue;
            }

            var value = -Negamax(after, depth - 1, -beta, -alpha);

            if (value > best)
            {
                best = value;
            }

            if (best > alpha)
            {
                alpha = best;
            }

            if (alpha >= beta)
            {
                break;
            }
        }

        return best;
    }

    /// <summary>
    /// Loads a fresh <see cref="ChessBoard"/> from <paramref name="board"/>'s FEN so the
    /// caller's board is never mutated and each searched line is independent. Returns
    /// <see langword="null"/> only if serialisation somehow fails (shouldn't happen for a board
    /// the library itself produced).
    /// </summary>
    private static ChessBoard? CloneBoard(ChessBoard board)
    {
        if (!ChessBoard.TryLoadFromFen(board.ToFen(), out var clone, EnabledDrawRules))
        {
            return null;
        }

        return clone;
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
    /// above) because a queen is almost always the highest-scoring promotion for this eval.
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
        int Score);
}
