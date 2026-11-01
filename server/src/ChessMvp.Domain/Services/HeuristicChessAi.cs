using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// The default <see cref="IChessAi"/>: a deliberately shallow, fully deterministic
/// move-selection heuristic. Every legal move is applied and the resulting position is scored
/// statically — material from a fixed piece-value table plus two light positional terms
/// (central occupation for pieces, advancement for pawns) — with an immediate checkmate
/// preferred outright and drawing moves avoided whenever an alternative exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Determinism.</b> No randomness source, no clock, no mutable state, and int-only
/// arithmetic (no floating point, so no cross-platform rounding variance). Ties are broken by
/// an explicit total order — score, then from square, then to square, compared ordinally —
/// rather than by trusting the enumeration order, so the same position selects the same move
/// even if the rules engine reorders its output. See <see cref="Beats"/>.
/// </para>
/// <para>
/// <b>Minimax compatibility.</b> The scorer is a static evaluator over the position after the
/// AI's own move, computed from the mover's perspective with symmetric terms (the opponent's
/// material and placement score against us). That is exactly the leaf-evaluation shape a
/// search needs, so deepening this into a minimax search is an implementation change behind
/// <see cref="IChessAi"/> — no consumer and no contract change is required.
/// </para>
/// <para>
/// <b>Cost and non-goals.</b> One move application and one board scan per legal move (~35 in a
/// middlegame). There is no reply lookahead, no opening book, no transposition table and no
/// randomness; strength upgrades belong behind the same seam, not inside this class.
/// </para>
/// </remarks>
public sealed class HeuristicChessAi : IChessAi
{
    // Material values, kept identical to the table pinned elsewhere in the domain
    // (HeuristicChessEngine): changing these is a spec change, not a free refactor.
    private const int PawnValue = 100;
    private const int KnightValue = 300;
    private const int BishopValue = 300;
    private const int RookValue = 500;
    private const int QueenValue = 900;

    // Kings are deliberately valued at 0: both sides always have exactly one, so a king term
    // would be constant, and a king's worth is terminal (mate) — which the mate term below
    // owns, not the material table.

    /// <summary>
    /// Light positional term: a non-king, non-pawn piece standing on one of the four central
    /// squares (d4, d5, e4, e5). Small next to the material table so it only orders materially
    /// equal outcomes.
    /// </summary>
    private const int CenterBonus = 10;

    /// <summary>
    /// Light positional term: bonus per rank a pawn has advanced beyond its starting rank.
    /// Rewards progress and nudges material ties toward developing play.
    /// </summary>
    private const int PawnAdvanceBonusPerRank = 5;

    /// <summary>Light tactical term for a candidate that gives check.</summary>
    private const int CheckBonus = 50;

    /// <summary>
    /// Dominates the sum of every other term (a whole army is worth ~3900), so an immediate
    /// checkmate outranks any material or positional gain a non-mating move can offer.
    /// </summary>
    private const int CheckmateScore = 1_000_000;

    /// <summary>
    /// Dominates any single-move material swing but is dwarfed by <see cref="CheckmateScore"/>,
    /// so a drawing move is avoided whenever a non-drawing alternative exists — yet a mating
    /// move is still preferred over merely avoiding a draw.
    /// </summary>
    private const int DrawPenalty = 10_000;

    private readonly IChessRulesEngine _rulesEngine;

    public HeuristicChessAi(IChessRulesEngine rulesEngine)
    {
        _rulesEngine = rulesEngine;
    }

    /// <inheritdoc />
    public ChosenMove? ChooseMove(string fen, PlayerColor sideToMove)
    {
        // The rules engine's enumeration is the complete and only source of candidates: this AI
        // never invents, filters or re-derives moves. An unparseable FEN yields an empty
        // enumeration and therefore null — terminal positions are the caller's concern, exactly
        // as for IChessRulesEngine.
        var candidates = _rulesEngine.GetAllLegalMoves(fen, sideToMove);
        if (candidates.Count == 0)
        {
            return null;
        }

        RulesLegalMove? best = null;
        var bestScore = 0;

        foreach (var candidate in candidates)
        {
            var score = ScoreCandidate(fen, sideToMove, candidate);

            if (best is null || Beats(candidate, score, best, bestScore))
            {
                best = candidate;
                bestScore = score;
            }
        }

        // candidates.Count > 0 guarantees the loop ran at least once, so best is never null.
        return ToChosenMove(fen, best!);
    }

    /// <summary>
    /// Scores one candidate by applying it and statically evaluating the resulting position
    /// from <paramref name="sideToMove"/>'s perspective: material plus positional terms, plus
    /// the light tactical terms the move itself carries (mate, check, draw).
    /// </summary>
    private int ScoreCandidate(string fen, PlayerColor sideToMove, RulesLegalMove candidate)
    {
        var applied = ApplyCandidate(fen, sideToMove, candidate);

        // Defensive only: the candidate came from this same FEN's legal-move enumeration, so
        // the application cannot legitimately fail. If it ever does, the move is scored below
        // every legal candidate instead of being selected unverified.
        if (!applied.IsLegal)
        {
            return int.MinValue;
        }

        var score = EvaluatePosition(applied.ResultingFen!, sideToMove);

        // Immediate mate outranks every static term: CheckmateScore dwarfs the largest possible
        // material swing, so "mate now" beats "win material", whatever the material says.
        if (candidate.IsCheckmate)
        {
            score += CheckmateScore;
        }

        if (candidate.IsCheck)
        {
            score += CheckBonus;
        }

        // Drawing moves are avoided whenever any alternative exists: DrawPenalty dwarfs a whole
        // army's worth of material, so it can only be the deciding term between a drawing and a
        // non-drawing candidate. When every candidate draws it applies to all of them equally,
        // cancels out of the comparison, and the tie-break decides.
        if (candidate.IsStalemate || candidate.IsFiftyMoveDraw)
        {
            score -= DrawPenalty;
        }

        return score;
    }

    /// <summary>
    /// The explicit tie-break that makes selection deterministic and independent of the order
    /// in which the rules engine enumerates moves: a strictly higher score wins, and equal
    /// scores are broken by the ordinally smaller from square, then the ordinally smaller to
    /// square. From/to pairs are unique within one enumeration (promotions are reported once
    /// per pair), so this is a total order over the candidates and the same candidate set
    /// always selects the same move.
    /// </summary>
    private static bool Beats(RulesLegalMove candidate, int candidateScore, RulesLegalMove incumbent, int incumbentScore)
    {
        if (candidateScore != incumbentScore)
        {
            return candidateScore > incumbentScore;
        }

        // Ordinal comparison: culture-invariant and stable across platforms and locales, which
        // a culture-aware comparison would not be.
        var byFromSquare = string.CompareOrdinal(candidate.FromSquare, incumbent.FromSquare);

        return byFromSquare != 0
            ? byFromSquare < 0
            : string.CompareOrdinal(candidate.ToSquare, incumbent.ToSquare) < 0;
    }

    /// <summary>
    /// Applies a candidate from an enumeration of legal moves, supplying Queen exactly when the
    /// from/to pair is a promotion, so the application is always complete and the resulting
    /// position is the one that would actually arise.
    /// </summary>
    private MoveApplicationResult ApplyCandidate(string fen, PlayerColor sideToMove, RulesLegalMove candidate)
    {
        // Declared as PromotionPieceType? (not `var`) so the conditional below is target-typed:
        // `var` cannot infer a type for "Queen or null" (CS0173).
        PromotionPieceType? promotion = _rulesEngine.IsPromotionMove(fen, candidate.FromSquare, candidate.ToSquare)
            ? PromotionPieceType.Queen
            : null;

        return _rulesEngine.TryApplyMove(fen, sideToMove, candidate.FromSquare, candidate.ToSquare, promotion);
    }

    /// <summary>
    /// Statically evaluates one position from <paramref name="forColor"/>'s perspective: the
    /// sum, over every piece on the board, of that piece's value signed positively for
    /// <paramref name="forColor"/> and negatively for the opponent. Symmetric by construction —
    /// the opponent's material and placement score against us — which is what a minimax leaf
    /// evaluation requires.
    /// </summary>
    /// <remarks>
    /// Fail-neutral on a malformed placement field (unrecognized characters and overlong ranks
    /// are skipped): the rules engine produced this FEN, so evaluation never needs to
    /// second-guess parseability, and throwing here would turn a scoring question into a game
    /// fault.
    /// </remarks>
    private static int EvaluatePosition(string fen, PlayerColor forColor)
    {
        // Field 0 of a FEN is the piece placement: ranks from 8 down to 1, files a→h, a digit
        // meaning "that many empty squares". Everything after the first space is irrelevant
        // here.
        var ranks = fen.Split(' ')[0].Split('/');

        var score = 0;

        for (var rankIndex = 0; rankIndex < ranks.Length; rankIndex++)
        {
            var rank = 8 - rankIndex; // rankIndex 0 describes rank 8
            if (rank < 1)
            {
                break; // defensive: more ranks than a board has
            }

            var file = 0; // 0 = a … 7 = h

            foreach (var piece in ranks[rankIndex])
            {
                if (piece is >= '1' and <= '8')
                {
                    file += piece - '0'; // skip that many empty squares
                    continue;
                }

                if (file > 7)
                {
                    break; // defensive: overlong rank, ignore the remainder
                }

                // FEN case encodes colour: upper-case is White. +1 for the evaluating side's
                // own pieces, -1 for the opponent's.
                var isOwnPiece = char.IsUpper(piece) == (forColor == PlayerColor.White);
                score += (isOwnPiece ? 1 : -1) * (MaterialValue(piece) + PositionalValue(piece, file, rank));

                file++;
            }
        }

        return score;
    }

    /// <summary>The material value of one piece, by its FEN character (case-insensitive).</summary>
    private static int MaterialValue(char piece) =>
        char.ToLowerInvariant(piece) switch
        {
            'p' => PawnValue,
            'n' => KnightValue,
            'b' => BishopValue,
            'r' => RookValue,
            'q' => QueenValue,
            'k' => 0, // see the value table above
            _ => 0, // anything unrecognized is skipped fail-neutrally
        };

    /// <summary>
    /// The light positional value of one piece on one square, expressed from the owning side's
    /// perspective — the caller applies the sign that turns it into the evaluating side's
    /// score. Two terms only, both deliberately small next to the material table: central
    /// occupation for non-king, non-pawn pieces, and advancement for pawns.
    /// </summary>
    private static int PositionalValue(char piece, int file, int rank)
    {
        var kind = char.ToLowerInvariant(piece);

        if (kind == 'k')
        {
            // The king is excluded on purpose: no term here may reward walking it toward the
            // centre, which in the middlegame is exactly where it does not want to be caught.
            return 0;
        }

        if (kind == 'p')
        {
            // Ranks advanced beyond the starting rank (rank 2 for White, rank 7 for Black); a
            // pawn on or behind its start (the latter impossible in practice) scores nothing.
            var ranksAdvanced = char.IsUpper(piece) ? rank - 2 : 7 - rank;
            return ranksAdvanced > 0 ? ranksAdvanced * PawnAdvanceBonusPerRank : 0;
        }

        // The four central squares: files d/e are 3/4, ranks 4/5 are 4/5.
        var isCentral = file is 3 or 4 && rank is 4 or 5;
        return isCentral ? CenterBonus : 0;
    }

    private ChosenMove ToChosenMove(string fen, RulesLegalMove move) =>
        new()
        {
            FromSquare = move.FromSquare,
            ToSquare = move.ToSquare,
            // The promotion piece is resolved here rather than deferred: the chosen move is
            // always complete and directly applicable, with no second decision left to the
            // caller.
            PromotionPiece = _rulesEngine.IsPromotionMove(fen, move.FromSquare, move.ToSquare)
                ? PromotionPieceType.Queen
                : null,
        };
}
