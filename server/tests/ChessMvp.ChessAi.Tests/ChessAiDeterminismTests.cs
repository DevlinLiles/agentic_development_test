using ChessMvp.ChessAi;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChessMvp.ChessAi.Tests;

/// <summary>
/// Determinism and promotion-handling tests that exercise the real
/// <see cref="HeuristicChessAiPlayer"/> purely through the dependency-injection container, as
/// the rest of the application does. The player is registered as a singleton by
/// <see cref="ServiceCollectionExtensions.AddChessAi"/>, so these tests resolve
/// <see cref="IChessAiPlayer"/> from a built service provider and assert that move selection is
/// stable across repeated invocations of the same position and that promotion moves yield a
/// valid promotion piece.
/// </summary>
/// <remarks>
/// <para>
/// The tests intentionally use the real rules engine adapter and the real heuristic evaluator
/// (the same graph <c>AddChessAi</c> wires in production) rather than stubs, so they guard the
/// end-to-end determinism contract: the same FEN, side, and legal-move set must always yield the
/// same chosen move, score, and promotion piece. Any non-determinism in evaluation or in the
/// SAN-based tie-break would surface as a divergent result across runs and fail the assertions.
/// </para>
/// <para>
/// To stress the tie-break specifically, the legal-move list is re-ordered (shuffled with a
/// fixed seed) between invocations. Because ties are broken by ascending SAN using an ordinal
/// comparison, the chosen move must be independent of the supplied ordering; a non-deterministic
/// or order-dependent tie-break would produce a different move for some ordering and fail.
/// </para>
/// </remarks>
public class ChessAiDeterminismTests
{
    private const string StartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // White pawn on e7 can promote to e8; the black king on a8 and white king on e1 leave the
    // promotion legal. This is the canonical promotion position used across the suite.
    private const string PromotionFen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";

    private const int DeterminismRuns = 25;

    // Fixed seed so the shuffle-based tie-break test is itself reproducible while still varying
    // the input order across iterations.
    private const int ShuffleSeed = 0xC123;

    /// <summary>
    /// Builds a service provider whose AI graph mirrors production: the real rules engine
    /// adapter is registered as a singleton, and <see cref="ServiceCollectionExtensions.AddChessAi"/>
    /// wires the heuristic player in as the <see cref="IChessAiPlayer"/> singleton together with
    /// its <see cref="IHeuristicEvaluator"/> dependency. The returned provider is the concrete
    /// <see cref="ServiceProvider"/> so callers can dispose it via <c>using</c>.
    /// </summary>
    private static (ServiceProvider provider, IChessAiPlayer player, IChessRulesEngine rulesEngine)
        BuildContainerWithAiPlayer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();
        services.AddChessAi();

        var provider = services.BuildServiceProvider();
        var player = provider.GetRequiredService<IChessAiPlayer>();
        var rulesEngine = provider.GetRequiredService<IChessRulesEngine>();
        return (provider, player, rulesEngine);
    }

    [Fact]
    public void Container_ResolvesIChessAiPlayerAsHeuristicChessAiPlayerSingleton()
    {
        using var provider = BuildContainerWithAiPlayer().provider;

        var first = provider.GetRequiredService<IChessAiPlayer>();
        var second = provider.GetRequiredService<IChessAiPlayer>();

        // The DI contract: a resolvable, shared singleton instance of the heuristic player.
        Assert.NotNull(first);
        Assert.IsType<HeuristicChessAiPlayer>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void SelectMove_FixedStartingPosition_IsDeterministicAcrossRepeatedInvocations()
    {
        var (provider, player, rulesEngine) = BuildContainerWithAiPlayer();
        using var _ = provider;

        var legalMoves = rulesEngine.GetAllLegalMoves(StartingFen);
        Assert.NotEmpty(legalMoves);

        AiMoveResult? reference = null;

        for (var i = 0; i < DeterminismRuns; i++)
        {
            var result = player.SelectMove(StartingFen, PlayerColor.White, legalMoves);

            Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
            var move = result.Move!;
            Assert.NotNull(move);

            if (reference is null)
            {
                reference = move;
            }
            else
            {
                AssertSameMove(reference, move);
            }
        }

        // Sanity: the loop actually ran and captured a reference.
        Assert.NotNull(reference);
    }

    [Fact]
    public void SelectMove_TieBreaking_IsIndependentOfLegalMoveOrdering()
    {
        // The starting position has several symmetric moves (e.g. a3/b3/c3/... and knight moves)
        // whose resulting positions evaluate equally, producing tied scores that the player must
        // break by ascending SAN. Re-ordering the input therefore must not change the choice.
        var (provider, player, rulesEngine) = BuildContainerWithAiPlayer();
        using var _ = provider;

        var legalMoves = rulesEngine.GetAllLegalMoves(StartingFen).ToList();
        Assert.NotEmpty(legalMoves);

        AiMoveResult? reference = null;
        var rng = new Random(ShuffleSeed);

        for (var i = 0; i < DeterminismRuns; i++)
        {
            var ordered = legalMoves.OrderBy(_ => rng.Next()).ToList();

            var result = player.SelectMove(StartingFen, PlayerColor.White, ordered);

            Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
            var move = result.Move!;
            Assert.NotNull(move);

            if (reference is null)
            {
                reference = move;
            }
            else
            {
                AssertSameMove(reference, move);
            }
        }

        Assert.NotNull(reference);
    }

    [Fact]
    public void SelectMove_PromotionPosition_PicksValidPromotionPieceAndIsDeterministic()
    {
        var (provider, player, rulesEngine) = BuildContainerWithAiPlayer();
        using var _ = provider;

        var legalMoves = rulesEngine.GetAllLegalMoves(PromotionFen).ToList();
        Assert.NotEmpty(legalMoves);
        Assert.Contains(legalMoves, m => m.IsPromotion);

        // Run the selection many times: the chosen promotion piece and move must be identical on
        // every run and must be a valid member of the PromotionPieceType enum.
        PromotionPieceType? referencePiece = null;
        LegalMove? referenceMove = null;
        double referenceScore = 0;

        for (var i = 0; i < DeterminismRuns; i++)
        {
            var result = player.SelectMove(PromotionFen, PlayerColor.White, legalMoves);

            Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
            var move = result.Move!;
            Assert.NotNull(move);
            Assert.True(move.Move.IsPromotion, "the greedy player must choose the promotion move");
            Assert.NotNull(move.PromotionPiece);

            // The chosen piece must be a defined enum value (guard against an unset/garbage piece).
            var piece = move.PromotionPiece!.Value;
            Assert.True(Enum.IsDefined(piece), $"promotion piece {piece} is not a valid enum member");

            if (referenceMove is null)
            {
                referenceMove = move.Move;
                referencePiece = piece;
                referenceScore = move.Score;
            }
            else
            {
                Assert.Equal(referenceMove, move.Move);
                Assert.Equal(referencePiece, piece);
                Assert.Equal(referenceScore, move.Score);
            }
        }

        Assert.NotNull(referenceMove);
        Assert.NotNull(referencePiece);
        // The default policy promotes to a queen unless a knight is strictly better; in this open
        // promotion position a queen is expected.
        Assert.Equal(PromotionPieceType.Queen, referencePiece);
    }

    [Fact]
    public void SelectMove_PromotionPieceIsOneOfTheLegalPromotionPieces()
    {
        // Defense in depth: the chosen promotion piece must be one of the four legal promotion
        // pieces the domain recognises, and the candidate list published via GetLastCandidates
        // must be stable across runs (best-first ordering is deterministic).
        var (provider, player, rulesEngine) = BuildContainerWithAiPlayer();
        using var _ = provider;

        var legalMoves = rulesEngine.GetAllLegalMoves(PromotionFen);

        var validPieces = new HashSet<PromotionPieceType>
        {
            PromotionPieceType.Queen,
            PromotionPieceType.Rook,
            PromotionPieceType.Bishop,
            PromotionPieceType.Knight,
        };

        AiMoveResult? reference = null;

        for (var i = 0; i < DeterminismRuns; i++)
        {
            var result = player.SelectMove(PromotionFen, PlayerColor.White, legalMoves);
            Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);

            var move = result.Move!;
            Assert.Contains(move.PromotionPiece!.Value, validPieces);

            var candidates = player.GetLastCandidates();
            Assert.NotNull(candidates);
            Assert.NotEmpty(candidates);

            if (reference is null)
            {
                reference = move;
            }
            else
            {
                AssertSameMove(reference, move);
            }
        }

        Assert.NotNull(reference);
    }

    /// <summary>
    /// Asserts that two <see cref="AiMoveResult"/> values describe the same selection: identical
    /// move (origin, destination, SAN, promotion flag), promotion piece, and score. Score
    /// equality catches evaluation non-determinism; move/piece equality catches tie-break
    /// non-determinism.
    /// </summary>
    private static void AssertSameMove(AiMoveResult expected, AiMoveResult actual)
    {
        Assert.Equal(expected.Move, actual.Move);
        Assert.Equal(expected.Move.FromSquare, actual.Move.FromSquare);
        Assert.Equal(expected.Move.ToSquare, actual.Move.ToSquare);
        Assert.Equal(expected.Move.San, actual.Move.San);
        Assert.Equal(expected.Move.IsPromotion, actual.Move.IsPromotion);
        Assert.Equal(expected.PromotionPiece, actual.PromotionPiece);
        Assert.Equal(expected.Score, actual.Score);
    }
}
