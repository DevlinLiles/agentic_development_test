using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

public sealed record MoveHistoryResponse(IReadOnlyList<MoveHistoryEntry> Moves)
{
    public static MoveHistoryResponse FromMoves(IReadOnlyList<Move> moves) =>
        new(moves
            .OrderBy(m => m.MoveNumber)
            .Select(m => new MoveHistoryEntry(
                m.MoveNumber,
                m.PlyColor,
                m.San,
                m.FromSquare,
                m.ToSquare,
                m.PromotionPiece,
                m.IsCheck,
                m.IsCheckmate))
            .ToList());
}

public sealed record MoveHistoryEntry(
    int MoveNumber,
    PlayerColor Color,
    string San,
    string From,
    string To,
    PromotionPieceType? Promotion,
    bool IsCheck,
    bool IsCheckmate);
