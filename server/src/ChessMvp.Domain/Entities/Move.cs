namespace ChessMvp.Domain.Entities;

public class Move
{
    public long Id { get; set; }

    public Guid GameId { get; set; }

    public Game? Game { get; set; }

    public int MoveNumber { get; set; }

    public PlayerColor PlyColor { get; set; }

    public string San { get; set; } = string.Empty;

    public string FromSquare { get; set; } = string.Empty;

    public string ToSquare { get; set; } = string.Empty;

    public PromotionPieceType? PromotionPiece { get; set; }

    public string ResultingFen { get; set; } = string.Empty;

    public bool IsCheck { get; set; }

    public bool IsCheckmate { get; set; }

    public DateTime CreatedUtc { get; set; }
}
