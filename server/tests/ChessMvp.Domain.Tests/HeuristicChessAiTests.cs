using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Behavioural pins for <see cref="HeuristicChessAi"/>, expressed against a scripted rules
/// engine (see <see cref="ScriptedRulesEngine"/>): each scenario states its position, its
/// candidate moves and the position each candidate produces, so the term under test —
/// material, placement, mate, draws or tie-breaking — is read straight off the resulting FENs
/// rather than off mock call counts.
/// </summary>
/// <remarks>
/// <para>
/// Every scenario also doubles as a seam guard: the scripted engine throws on
/// <see cref="IChessRulesEngine.GetLegalDestinations"/>, so a passing test proves the AI
/// selected its move from <see cref="IChessRulesEngine.GetAllLegalMoves"/> alone.
/// </para>
/// <para>
/// Check, checkmate and draw flags are scripted rather than derived from board geometry — they
/// are inputs the real rules engine computes. What is under test here is what the AI does with
/// them once supplied.
/// </para>
/// </remarks>
public class HeuristicChessAiTests
{
    private const string StartFenWhiteToMove = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string StartFenBlackToMove = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1";

    // The starting position after each knight development. Materially identical, no central
    // square occupied, no pawn advanced: exact ties for the scorer.
    private const string AfterNc3 = "rnbqkbnr/pppppppp/8/8/8/2N5/PPPPPPPP/R1BQKBNR b KQkq - 1 1";
    private const string AfterNf3 = "rnbqkbnr/pppppppp/8/8/8/5N2/PPPPPPPP/RNBQKB1R b KQkq - 1 1";
    private const string AfterNa3 = "rnbqkbnr/pppppppp/8/8/8/N7/PPPPPPPP/R1BQKBNR b KQkq - 1 1";

    // The starting position after 1.a3 / 1.a4 — one vs two ranks of pawn advancement.
    private const string AfterA3 = "rnbqkbnr/pppppppp/8/8/8/P7/1PPPPPPP/RNBQKBNR b KQkq - 0 1";
    private const string AfterA4 = "rnbqkbnr/pppppppp/8/8/P7/8/1PPPPPPP/RNBQKBNR b KQkq - 0 1";

    // Black's mirror images after ...a6 / ...a5.
    private const string AfterBlackA6 = "rnbqkbnr/1ppppppp/p7/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterBlackA5 = "rnbqkbnr/1ppppppp/8/p7/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // A white knight on c3 with both kings tucked away, and the two squares it can reach:
    // a4 (edge of the board) and d5 (one of the four central squares).
    private const string KnightOnC3 = "7k/8/8/8/8/2N5/8/7K w - - 0 1";
    private const string AfterKnightToA4 = "7k/8/8/8/N7/8/8/7K b - - 1 1";
    private const string AfterKnightToD5 = "7k/8/8/3N4/8/8/8/7K b - - 1 1";

    // The same knight, but a black pawn on b5 is now capturable. Capturing it wins a pawn yet
    // lands on the edge; the quiet central move keeps material level but gains the centre.
    private const string KnightOnC3WithPawnOnB5 = "7k/8/8/1p6/8/2N5/8/7K w - - 0 1";
    private const string AfterKnightTakesB5 = "7k/8/8/1N6/8/8/8/7K b - - 1 1";
    private const string AfterQuietKnightToD5 = "7k/8/8/1p1N4/8/8/8/7K b - - 1 1";

    // Black is a rook down (white R+Q against black Q) but has the move. The queen can capture
    // the white queen, retreat quietly, or deliver mate.
    private const string BlackToMoveDownARook = "3q3k/8/8/8/8/8/8/R2Q3K b - - 0 1";
    private const string AfterBlackQueenTakesD1 = "7k/8/8/8/8/8/8/R2q3K w - - 0 2";
    private const string AfterBlackQueenRetreatsToD6 = "7k/8/3q4/8/8/8/8/R2Q3K w - - 1 2";
    private const string AfterBlackQueenMatesOnH4 = "7k/8/8/8/7q/8/8/R2Q3K w - - 1 2";

    // A white pawn one step from promotion, with both kings out of the way.
    private const string WhitePawnOnA7 = "8/P7/8/8/8/8/8/K6k w - - 0 1";
    private const string AfterPromotionToQueen = "Q7/8/8/8/8/8/8/K6k b - - 0 1";

    [Fact]
    public void ChooseMove_WhenNoLegalMoveExists_ReturnsNull()
    {
        // Terminal positions are the caller's concern: the AI reports "nothing to choose"
        // rather than fabricating a move or throwing.
        var sut = new HeuristicChessAi(new ScriptedRulesEngine());

        var chosen = sut.ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.Null(chosen);
    }

    [Fact]
    public void ChooseMove_SolicitsTheRulesEngineOnceForTheExactPositionAndSide()
    {
        var engine = new ScriptedRulesEngine().AddMove("b1", "c3", AfterNc3);
        var sut = new HeuristicChessAi(engine);

        _ = sut.ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        // The enumeration is the complete and only candidate source: exactly one consultation,
        // for the exact position and side the caller asked about.
        var call = Assert.Single(engine.GetAllLegalMovesCalls);
        Assert.Equal(StartFenWhiteToMove, call.Fen);
        Assert.Equal(PlayerColor.White, call.SideToMove);
    }

    [Fact]
    public void ChooseMove_RepeatedCalls_ReturnTheSameSelectionWithoutCaching()
    {
        var engine = new ScriptedRulesEngine()
            .AddMove("g1", "f3", AfterNf3)
            .AddMove("b1", "c3", AfterNc3);
        var sut = new HeuristicChessAi(engine);

        var first = sut.ChooseMove(StartFenWhiteToMove, PlayerColor.White);
        var second = sut.ChooseMove(StartFenWhiteToMove, PlayerColor.White);
        var third = sut.ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);

        // No "last position" memoization: every call re-derives its answer from the rules
        // engine — and still selects the same move every time.
        Assert.Equal(3, engine.GetAllLegalMovesCalls.Count);
    }

    [Fact]
    public void ChooseMove_EqualScores_TieBreaksOnFromSquareIndependentlyOfEnumerationOrder()
    {
        // Nf3 and Nc3 from the starting position are materially and positionally identical, so
        // the explicit tie-break — not enumeration order — decides, and "b1" sorts before "g1".
        var f3First = new ScriptedRulesEngine()
            .AddMove("g1", "f3", AfterNf3)
            .AddMove("b1", "c3", AfterNc3);
        var c3First = new ScriptedRulesEngine()
            .AddMove("b1", "c3", AfterNc3)
            .AddMove("g1", "f3", AfterNf3);

        var fromF3First = new HeuristicChessAi(f3First).ChooseMove(StartFenWhiteToMove, PlayerColor.White);
        var fromC3First = new HeuristicChessAi(c3First).ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(fromF3First);
        Assert.NotNull(fromC3First);
        Assert.Equal("b1", fromF3First!.FromSquare);
        Assert.Equal("c3", fromF3First.ToSquare);
        Assert.Equal(fromF3First, fromC3First); // enumeration order cannot change the selection
        Assert.Null(fromF3First.PromotionPiece); // a non-promotion stays unresolved
    }

    [Fact]
    public void ChooseMove_EqualScoresFromOneSquare_TieBreaksOnToSquare()
    {
        // Na3 and Nc3 score identically (a3 and c3 are both non-central, material unchanged),
        // so the tie-break falls through to the to-square and "a3" sorts before "c3" — again
        // regardless of the order the moves arrived in.
        var a3First = new ScriptedRulesEngine()
            .AddMove("b1", "a3", AfterNa3)
            .AddMove("b1", "c3", AfterNc3);
        var c3First = new ScriptedRulesEngine()
            .AddMove("b1", "c3", AfterNc3)
            .AddMove("b1", "a3", AfterNa3);

        var fromA3First = new HeuristicChessAi(a3First).ChooseMove(StartFenWhiteToMove, PlayerColor.White);
        var fromC3First = new HeuristicChessAi(c3First).ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(fromA3First);
        Assert.NotNull(fromC3First);
        Assert.Equal("b1", fromA3First!.FromSquare);
        Assert.Equal("a3", fromA3First.ToSquare);
        Assert.Equal(fromA3First, fromC3First);
    }

    [Fact]
    public void ChooseMove_EqualMaterial_PrefersTheCentralSquare()
    {
        // The knight can reach a4 (edge of the board) or d5 (one of the four central squares)
        // with identical material on both resulting boards, so only the placement term can
        // separate them.
        var engine = new ScriptedRulesEngine()
            .AddMove("c3", "a4", AfterKnightToA4)
            .AddMove("c3", "d5", AfterKnightToD5);

        var chosen = new HeuristicChessAi(engine).ChooseMove(KnightOnC3, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("c3", chosen!.FromSquare);
        Assert.Equal("d5", chosen.ToSquare); // +310 (central) beats +300 (edge)
    }

    [Fact]
    public void ChooseMove_EqualMaterial_PrefersTheMoreAdvancedPawn()
    {
        var engine = new ScriptedRulesEngine()
            .AddMove("a2", "a3", AfterA3)
            .AddMove("a2", "a4", AfterA4);

        var chosen = new HeuristicChessAi(engine).ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("a2", chosen!.FromSquare);
        Assert.Equal("a4", chosen.ToSquare); // two ranks advanced (+10) beats one (+5)
    }

    [Fact]
    public void ChooseMove_BlackPawnAdvancement_IsScoredFromBlacksPerspective()
    {
        var engine = new ScriptedRulesEngine()
            .AddMove("a7", "a6", AfterBlackA6)
            .AddMove("a7", "a5", AfterBlackA5);

        var chosen = new HeuristicChessAi(engine).ChooseMove(StartFenBlackToMove, PlayerColor.Black);

        Assert.NotNull(chosen);
        Assert.Equal("a7", chosen!.FromSquare);
        Assert.Equal("a5", chosen.ToSquare); // ...a5 advances two ranks, ...a6 only one
    }

    [Fact]
    public void ChooseMove_MaterialGainOutweighsThePositionalTerm()
    {
        // Nxb5 captures a pawn: the knight lands on the edge (+0 placement) but White nets +300.
        // The quiet Nd5 gains the centre (+10) while the black pawn survives (−100 material and
        // −10 for its own two ranks of advancement): +200 net. The material table must dominate
        // the placement term, not merely nudge it.
        var engine = new ScriptedRulesEngine()
            .AddMove("c3", "d5", AfterQuietKnightToD5)
            .AddMove("c3", "b5", AfterKnightTakesB5);

        var chosen = new HeuristicChessAi(engine).ChooseMove(KnightOnC3WithPawnOnB5, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("c3", chosen!.FromSquare);
        Assert.Equal("b5", chosen.ToSquare); // +300 (capture) beats +200 (central, pawn alive)
    }

    [Fact]
    public void ChooseMove_CapturesAreScoredForTheMovingSide_Black()
    {
        // Qxd1 nets Black +400; retreating the queen to d6 keeps Black a rook down (-500). If
        // the evaluator's sign were flipped, the retreat would win — so this pins the scoring
        // perspective to the side the AI plays for.
        var engine = new ScriptedRulesEngine()
            .AddMove("d8", "d6", AfterBlackQueenRetreatsToD6)
            .AddMove("d8", "d1", AfterBlackQueenTakesD1);

        var chosen = new HeuristicChessAi(engine).ChooseMove(BlackToMoveDownARook, PlayerColor.Black);

        Assert.NotNull(chosen);
        Assert.Equal("d8", chosen!.FromSquare);
        Assert.Equal("d1", chosen.ToSquare);
    }

    [Fact]
    public void ChooseMove_PrefersCheckOverAnEquallyScoredQuietMove()
    {
        var engine = new ScriptedRulesEngine()
            .AddMove("b1", "c3", AfterNc3)
            .AddMove("g1", "f3", AfterNf3, isCheck: true);

        var chosen = new HeuristicChessAi(engine).ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("g1", chosen!.FromSquare); // the checking move wins despite equal material
        Assert.Equal("f3", chosen.ToSquare);
    }

    [Fact]
    public void ChooseMove_PrefersImmediateCheckmateOverWinningMaterial()
    {
        // The mating queen leaves Black a rook down (-500) while capturing the white queen
        // instead would put Black +400 up. Mate must dominate both directions of that material
        // comparison — by a margin no single material swing can reach.
        var engine = new ScriptedRulesEngine()
            .AddMove("d8", "d1", AfterBlackQueenTakesD1)
            .AddMove("d8", "h4", AfterBlackQueenMatesOnH4, isCheck: true, isCheckmate: true);

        var chosen = new HeuristicChessAi(engine).ChooseMove(BlackToMoveDownARook, PlayerColor.Black);

        Assert.NotNull(chosen);
        Assert.Equal("d8", chosen!.FromSquare);
        Assert.Equal("h4", chosen.ToSquare);
    }

    [Fact]
    public void ChooseMove_AvoidsAStalematingMoveWhenAnAlternativeExists()
    {
        var engine = new ScriptedRulesEngine()
            .AddMove("g1", "f3", AfterNf3, isStalemate: true)
            .AddMove("b1", "c3", AfterNc3);

        var chosen = new HeuristicChessAi(engine).ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("b1", chosen!.FromSquare); // the quiet move beats the stalemating one
        Assert.Equal("c3", chosen.ToSquare);
    }

    [Fact]
    public void ChooseMove_AvoidsAFiftyMoveDrawWhenAnAlternativeExists()
    {
        var engine = new ScriptedRulesEngine()
            .AddMove("g1", "f3", AfterNf3, isFiftyMoveDraw: true)
            .AddMove("b1", "c3", AfterNc3);

        var chosen = new HeuristicChessAi(engine).ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("b1", chosen!.FromSquare);
        Assert.Equal("c3", chosen.ToSquare);
    }

    [Fact]
    public void ChooseMove_WhenEveryCandidateDraws_StillChoosesOne()
    {
        // "Avoid the draw" is unsatisfiable here, so the penalty applies to every candidate
        // equally, cancels out of the comparison, and the ordinary tie-break decides.
        var engine = new ScriptedRulesEngine()
            .AddMove("g1", "f3", AfterNf3, isStalemate: true)
            .AddMove("b1", "c3", AfterNc3, isStalemate: true);

        var chosen = new HeuristicChessAi(engine).ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("b1", chosen!.FromSquare);
        Assert.Equal("c3", chosen.ToSquare);
    }

    [Fact]
    public void ChooseMove_Promotion_ReturnsACompleteMoveWithQueen()
    {
        var engine = new ScriptedRulesEngine()
            .AddMove("a7", "a8", AfterPromotionToQueen, isPromotion: true);

        var chosen = new HeuristicChessAi(engine).ChooseMove(WhitePawnOnA7, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("a7", chosen!.FromSquare);
        Assert.Equal("a8", chosen.ToSquare);

        // The promotion is resolved before the move leaves the AI: the caller receives a
        // directly applicable move and never has to choose a piece on the AI's behalf.
        Assert.Equal(PromotionPieceType.Queen, chosen.PromotionPiece);

        // The candidate was scored with the queen already chosen, not with the promotion
        // deferred to move-application time.
        var applied = Assert.Single(engine.TryApplyMoveCalls, call =>
            call.FromSquare == "a7" && call.ToSquare == "a8");
        Assert.Equal(PromotionPieceType.Queen, applied.Promotion);
    }

    [Fact]
    public void ChooseMove_CandidateThatCannotBeApplied_IsNeverSelected()
    {
        // The scripted engine refuses to apply the first candidate, so the AI cannot verify the
        // position it would produce. However attractive that move looks, it must fall back to a
        // candidate it can verify — the AI never selects an unverified move.
        var engine = new ScriptedRulesEngine()
            .AddMove("a2", "a4", AfterA4)
            .AddMove("b1", "c3", AfterNc3);
        engine.MarkUnapplicable("a2", "a4");

        var chosen = new HeuristicChessAi(engine).ChooseMove(StartFenWhiteToMove, PlayerColor.White);

        Assert.NotNull(chosen);
        Assert.Equal("b1", chosen!.FromSquare);
        Assert.Equal("c3", chosen.ToSquare);
    }

    /// <summary>
    /// A scripted <see cref="IChessRulesEngine"/> for scoring scenarios: tests register each
    /// legal move together with the position it produces, and the engine replays them. Move
    /// geometry is never validated — the AI under test may only consume what the enumeration
    /// hands it, which is precisely the behaviour under test.
    /// </summary>
    private sealed class ScriptedRulesEngine : IChessRulesEngine
    {
        private readonly List<RulesLegalMove> _moves = new();
        private readonly Dictionary<string, string> _resultingFens = new(StringComparer.Ordinal);
        private readonly HashSet<string> _promotionPairs = new(StringComparer.Ordinal);
        private readonly HashSet<string> _unapplicablePairs = new(StringComparer.Ordinal);

        public List<(string Fen, PlayerColor SideToMove)> GetAllLegalMovesCalls { get; } = new();

        public List<(string FromSquare, string ToSquare, PromotionPieceType? Promotion)> TryApplyMoveCalls { get; } = new();

        public ScriptedRulesEngine AddMove(
            string fromSquare,
            string toSquare,
            string resultingFen,
            bool isCheck = false,
            bool isCheckmate = false,
            bool isStalemate = false,
            bool isFiftyMoveDraw = false,
            bool isPromotion = false)
        {
            _moves.Add(new RulesLegalMove
            {
                FromSquare = fromSquare,
                ToSquare = toSquare,
                San = fromSquare + toSquare,
                IsCheck = isCheck,
                IsCheckmate = isCheckmate,
                IsStalemate = isStalemate,
                IsFiftyMoveDraw = isFiftyMoveDraw,
            });

            _resultingFens[fromSquare + toSquare] = resultingFen;

            if (isPromotion)
            {
                _promotionPairs.Add(fromSquare + toSquare);
            }

            return this;
        }

        /// <summary>
        /// Marks a registered move as one whose application fails, exercising the AI's defensive
        /// "never select an unverified move" path.
        /// </summary>
        public ScriptedRulesEngine MarkUnapplicable(string fromSquare, string toSquare)
        {
            _unapplicablePairs.Add(fromSquare + toSquare);
            return this;
        }

        public IReadOnlyList<RulesLegalMove> GetAllLegalMoves(string fen, PlayerColor sideToMove)
        {
            GetAllLegalMovesCalls.Add((fen, sideToMove));
            return _moves;
        }

        public MoveApplicationResult TryApplyMove(
            string fen,
            PlayerColor sideToMove,
            string fromSquare,
            string toSquare,
            PromotionPieceType? promotion)
        {
            TryApplyMoveCalls.Add((fromSquare, toSquare, promotion));

            var key = fromSquare + toSquare;
            if (_unapplicablePairs.Contains(key) || !_resultingFens.TryGetValue(key, out var resultingFen))
            {
                return MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove);
            }

            return new MoveApplicationResult
            {
                IsLegal = true,
                San = fromSquare + toSquare,
                ResultingFen = resultingFen,
            };
        }

        /// <summary>
        /// Deliberately unsupported: <see cref="HeuristicChessAi"/> must never consult the
        /// single-square query, so any attempt fails the test loudly rather than silently.
        /// </summary>
        public IReadOnlySet<string> GetLegalDestinations(string fen, string fromSquare) =>
            throw new NotSupportedException("HeuristicChessAi must not consult GetLegalDestinations.");

        public bool IsPromotionMove(string fen, string fromSquare, string toSquare) =>
            _promotionPairs.Contains(fromSquare + toSquare);
    }
}
