namespace ChessMvp.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }

    public Guid? WhiteSlotToken { get; set; }

    public Guid? BlackSlotToken { get; set; }

    public string CurrentFen { get; set; } = ChessConstants.StartingFen;

    public PlayerColor Turn { get; set; } = PlayerColor.White;

    public GameStatus Status { get; set; } = GameStatus.WaitingForPlayer2;

    public GameResult? Result { get; set; }

    public GameResultReason? ResultReason { get; set; }

    public GameOpponentType OpponentType { get; set; } = GameOpponentType.Human;

    public PlayerColor? AiColor { get; set; }

    public int HalfmoveClock { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public byte[] Version { get; set; } = [];

    public List<Move> Moves { get; set; } = [];
}
