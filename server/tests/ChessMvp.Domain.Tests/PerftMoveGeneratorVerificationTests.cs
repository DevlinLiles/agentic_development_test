using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Move-generator correctness verification via perft (performance test) node counts.
///
/// Perft walks the full legal move tree to a fixed depth and counts the leaf nodes.
/// The counts are independent of search/evaluation and are the standard deterministic
/// check that a move generator (legal-move generation, including castling, en passant,
/// promotions, and check/pin handling) is correct. They are compared against the
/// published reference values from the chess programming community:
///
///   * Starting position:  https://www.chessprogramming.org/Perft_Results
///   * Kiwipete position:  https://www.chessprogramming.org/Perft_Results
///
/// The procedure fails (the test throws / xUnit reports failure, a non-zero exit) on
/// any mismatch between the computed and the reference node count.
/// </summary>
public class PerftMoveGeneratorVerificationTests
{
    private readonly GerasimleoChessRulesEngineAdapter _engine = new();

    public static IEnumerable<object[]> StartingPositionCases => new[]
    {
        // depth, expectedNodeCount  (published reference values for the start position)
        new object[] { 1, 20 },
        new object[] { 2, 400 },
        new object[] { 3, 8902 },
    };

    public static IEnumerable<object[]> KiwipeteCases => new[]
    {
        // depth, expectedNodeCount  (published reference values for Kiwipete)
        new object[] { 1, 48 },
        new object[] { 2, 2039 },
        new object[] { 3, 97862 },
    };

    [Theory]
    [MemberData(nameof(StartingPositionCases))]
    public void Perft_StartingPosition_MatchesPublishedReference(int depth, long expected)
    {
        long actual = Perft(ChessConstants.StartingFen, depth);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(KiwipeteCases))]
    public void Perft_Kiwipete_MatchesPublishedReference(int depth, long expected)
    {
        const string kiwipeteFen = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";

        long actual = Perft(kiwipeteFen, depth);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Recursive perft: counts the number of leaf nodes at exactly <paramref name="depth"/>
    /// plies from <paramref name="fen"/>. Every move is generated through the project's
    /// move-generator seam (<see cref="IChessRulesEngine"/>) and applied to obtain the
    /// child position, so this exercises the same legality logic the app relies on.
    /// </summary>
    private long Perft(string fen, int depth)
    {
        if (depth == 0)
        {
            return 1;
        }

        var sideToMove = WhoseTurn(fen);

        // Collect the source squares that have at least one legal destination.
        var sources = AllSquares()
            .Where(sq => _engine.GetLegalDestinations(fen, sq).Count > 0)
            .ToList();

        long nodes = 0;
        foreach (var from in sources)
        {
            var destinations = _engine.GetLegalDestinations(fen, from);
            foreach (var to in destinations)
            {
                // Try every promotion piece when the move is a promotion; the engine
                // defaults to queen otherwise, which is fine for non-promotion moves.
                var promotions = _engine.IsPromotionMove(fen, from, to)
                    ? AllPromotions()
                    : new[] { (PromotionPieceType?)null };

                foreach (var promotion in promotions)
                {
                    var result = _engine.TryApplyMove(fen, sideToMove, from, to, promotion);
                    if (!result.IsLegal || result.ResultingFen is null)
                    {
                        // Should not happen for a reported legal destination, but guard
                        // against any divergence so the count stays well-defined.
                        throw new InvalidOperationException(
                            $"Engine reported {from}->{to} as a legal destination but refused to apply it.");
                    }

                    nodes += Perft(result.ResultingFen, depth - 1);
                }
            }
        }

        return nodes;
    }

    private static PlayerColor WhoseTurn(string fen)
    {
        // FEN field 2 is the side to move: 'w' or 'b'.
        var fields = fen.Split(' ');
        return string.Equals(fields[1], "w", StringComparison.OrdinalIgnoreCase)
            ? PlayerColor.White
            : PlayerColor.Black;
    }

    private static IEnumerable<PromotionPieceType?> AllPromotions() =>
        new PromotionPieceType?[]
        {
            PromotionPieceType.Queen,
            PromotionPieceType.Rook,
            PromotionPieceType.Bishop,
            PromotionPieceType.Knight,
        };

    private static IEnumerable<string> AllSquares()
    {
        for (var file = 'a'; file <= 'h'; file++)
        {
            for (var rank = 1; rank <= 8; rank++)
            {
                yield return $"{file}{rank}";
            }
        }
    }
}
