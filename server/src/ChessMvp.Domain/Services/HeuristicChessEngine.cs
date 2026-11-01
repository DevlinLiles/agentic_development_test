using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// Basic heuristic move selection for the AI opponent. Wraps <see cref="IChessRulesEngine"/> for
/// legality and move application, and scores candidate positions by material plus a small check
/// bonus, with a one-ply opponent-reply lookahead to avoid hanging material. Fully deterministic:
/// the only ordering input is <see cref="IChessRulesEngine.GetAllLegalMoves"/>' enumeration order
/// (stable for identical fen + side inputs), and ties are broken by keeping the first candidate
/// that achieves the best score — a strict greater-than comparison, never &gt;=.
/// </summary>
/// <remarks>
/// <para>
/// Complexity (NFR-1): ~35 + 35·(1 + 35) rules-engine calls in a typical middlegame — the
/// "1-ply plus best-reply material evaluation" shape the brief authorized, applied to every
/// candidate rather than a pruned set. There is no deeper search, no opening book, no
/// transposition table and no randomness source; those are explicit non-goals.
/// </para>
/// <para>
/// All scores are <see cref="int"/>; no floating point is used anywhere, so there is no
/// cross-platform determinism hazard (FR-3.4).
/// </para>
/// </remarks>
public sealed class HeuristicChessEngine : IHeuristicChessEngine
{
    // Material values (FR-3). Changing these is a spec change, not a free refactor: every
    // pinned-selection test depends on them and they are pinned to the brief's value table.
    private const int PawnValue = 100;
    private const int KnightValue = 300;
    private const int BishopValue = 300;
    private const int RookValue = 500;
    private const int QueenValue = 900;
    private const int KingValue = 0;

    /// <summary>Light bonus for a candidate move that gives check.</summary>
    private const int CheckBonus = 50;

    /// <summary>
    /// Dominates any single-move material swing (a whole army is worth less than this), so
    /// "avoid a drawing move" outranks everything except immediate mate.
    /// </summary>
    private const int DrawPenalty = 10_000;

    private readonly IChessRulesEngine _rulesEngine;

    public HeuristicChessEngine(IChessRulesEngine rulesEngine)
    {
        _rulesEngine = rulesEngine;
    }

    /// <inheritdoc />
    public HeuristicMoveSelection? SelectMove(string fen, PlayerColor sideToMove)
    {
        // FR-2: the engine never invents moves — this enumeration is the complete and only
        // source of candidates. An unparseable FEN yields an empty enumeration, which returns
        // null here; terminal-position handling is the caller's concern.
        var candidates = _rulesEngine.GetAllLegalMoves(fen, sideToMove);
        if (candidates.Count == 0)
        {
            return null;
        }

        // Step 1 — immediate mate wins outright (FR-3.1): the first mating candidate in
        // enumeration order is selected without any further scoring.
        foreach (var candidate in candidates)
        {
            if (candidate.IsCheckmate)
            {
                return ToSelection(fen, candidate);
            }
        }

        // Step 2 — avoid drawing moves when a non-drawing alternative exists (FR-3.2). When every
        // move draws, "avoid" is impossible and the full candidate set is scored instead: any
        // move is as good as any other, and the tie-break below picks the first.
        List<RulesLegalMove> nonDrawing = new(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (!candidate.IsStalemate && !candidate.IsFiftyMoveDraw)
            {
                nonDrawing.Add(candidate);
            }
        }

        var pool = nonDrawing.Count > 0 ? nonDrawing : candidates;

        // Step 3 — score every pool candidate from the AI's perspective (higher is better) and
        // keep the first best: `best is null || score > bestScore` is exactly "strictly greater
        // than minus infinity", so the earliest candidate wins every tie (FR-3.4).
        RulesLegalMove? best = null;
        var bestScore = 0;

        foreach (var candidate in pool)
        {
            var score = ScoreCandidate(fen, sideToMove, candidate);

            if (best is null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        // pool is never empty: candidates.Count > 0 above, and pool is either a non-empty subset
        // or the full candidate set. The null-forgiveness below is guaranteed by construction.
        return ToSelection(fen, best!);
    }

    /// <summary>
    /// Scores one candidate from <paramref name="sideToMove"/>'s perspective: material of the
    /// resulting position, plus a check bonus, minus the draw penalty, minus the opponent's best
    /// material reply (one-ply lookahead, FR-3.3).
    /// </summary>
    private int ScoreCandidate(string fen, PlayerColor sideToMove, RulesLegalMove candidate)
    {
        // 3a. Static material of the resulting position. IsLegal is guaranteed in practice — the
        // candidate came from GetAllLegalMoves on this same FEN and Queen is supplied exactly
        // when the from/to pair is a promotion, matching the enumeration's one-entry-per-pair
        // behavior — but the AI never selects an unverified move: an application that somehow
        // fails scores below every legal candidate instead.
        var applied = ApplyCandidate(fen, sideToMove, candidate);
        if (!applied.IsLegal)
        {
            return int.MinValue;
        }

        var resultingFen = applied.ResultingFen!;
        var score = MaterialBalance(resultingFen, sideToMove);

        // 3b. Light check term.
        if (candidate.IsCheck)
        {
            score += CheckBonus;
        }

        // 3c. Draw penalty — only reachable when every candidate draws (the drawing candidates
        // were filtered out in step 2 otherwise).
        if (candidate.IsStalemate || candidate.IsFiftyMoveDraw)
        {
            score -= DrawPenalty;
        }

        // 3d. One-ply lookahead: assume the opponent answers with their best material grab and
        // charge that loss against this candidate, so the AI does not hang pieces.
        var opponent = Other(sideToMove);
        var replies = _rulesEngine.GetAllLegalMoves(resultingFen, opponent);

        if (replies.Count == 0)
        {
            // IsCheckmate was handled in step 1, so an empty reply set here means the opponent
            // has no reply in a position the per-move flags above did not report as a draw —
            // stalemate semantics. Apply the same penalty rather than treating it as a free win.
            if (!candidate.IsCheckmate)
            {
                score -= DrawPenalty;
            }
        }
        else
        {
            var worstReplyLoss = int.MinValue;
            foreach (var reply in replies)
            {
                var replyApplied = ApplyCandidate(resultingFen, opponent, reply);
                if (!replyApplied.IsLegal)
                {
                    // Defensive only (see 3a): never charge the AI for a reply that could not be
                    // applied.
                    continue;
                }

                var replyBalance = MaterialBalance(replyApplied.ResultingFen!, opponent);
                if (replyBalance > worstReplyLoss)
                {
                    worstReplyLoss = replyBalance;
                }
            }

            // If every reply failed to apply defensively (impossible in practice), treat the
            // candidate as reply-neutral rather than scoring it at int.MinValue.
            score -= worstReplyLoss == int.MinValue ? 0 : worstReplyLoss;
        }

        return score;
    }

    /// <summary>
    /// Applies a candidate from an enumeration of legal moves, supplying Queen exactly when the
    /// from/to pair is a promotion (FR-4) so the application is always complete.
    /// </summary>
    private MoveApplicationResult ApplyCandidate(string fen, PlayerColor sideToMove, RulesLegalMove candidate)
    {
        // The explicit PromotionPieceType? annotation supplies the target type the conditional
        // needs: `var` would leave it with no implicit conversion between the enum and null.
        PromotionPieceType? promotion = _rulesEngine.IsPromotionMove(fen, candidate.FromSquare, candidate.ToSquare)
            ? PromotionPieceType.Queen
            : null;

        return _rulesEngine.TryApplyMove(fen, sideToMove, candidate.FromSquare, candidate.ToSquare, promotion);
    }

    private HeuristicMoveSelection ToSelection(string fen, RulesLegalMove move) =>
        new()
        {
            FromSquare = move.FromSquare,
            ToSquare = move.ToSquare,
            // FR-4: the AI always promotes to Queen — no alternative-piece logic exists.
            PromotionPiece = _rulesEngine.IsPromotionMove(fen, move.FromSquare, move.ToSquare)
                ? PromotionPieceType.Queen
                : null,
        };

    /// <summary>
    /// Raw material of a position from one side's perspective: that side's pieces minus the
    /// opponent's. Knows nothing about check, mate, draws or lookahead on purpose — it is one
    /// term of the scoring pipeline above, not a general evaluator, so it stays private.
    /// </summary>
    /// <remarks>
    /// Fail-neutral on a malformed board (returns an unbalanced-but-defined tally rather than
    /// throwing): the rules engine is the authority on legality and has already produced this
    /// FEN, so scoring never needs to second-guess parseability.
    /// </remarks>
    private static int MaterialBalance(string fen, PlayerColor forColor)
    {
        // Field 0 of a FEN is the piece placement: rank-by-rank from 8 down to 1, files a→h,
        // digits meaning "that many empty squares". Everything after the first space is ignored.
        var board = fen.Split(' ')[0];

        var white = 0;
        var black = 0;

        foreach (var ch in board)
        {
            if (ch == '/')
            {
                continue; // rank separator — nothing to score
            }

            if (ch >= '1' && ch <= '8')
            {
                continue; // N empty squares — nothing to score
            }

            var value = PieceValue(ch);
            if (value == 0)
            {
                // Kings score 0; anything unrecognized is skipped fail-neutrally.
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

        return forColor == PlayerColor.White ? white - black : black - white;
    }

    private static int PieceValue(char pieceChar) =>
        char.ToLowerInvariant(pieceChar) switch
        {
            'p' => PawnValue,
            'n' => KnightValue,
            'b' => BishopValue,
            'r' => RookValue,
            'q' => QueenValue,
            'k' => KingValue,
            _ => 0,
        };

    private static PlayerColor Other(PlayerColor color) =>
        color == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
}
