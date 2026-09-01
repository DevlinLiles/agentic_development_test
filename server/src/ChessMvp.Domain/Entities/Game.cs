namespace ChessMvp.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }

    public Guid? WhiteSlotToken { get; set; }

    public Guid? BlackSlotToken { get; set; }

    public string CurrentFen { get; set; } = ChessConstants.StartingFen;

    public PlayerColor Turn { get; set; } = PlayerColor.White;

    public GameStatus Status { get; set; } = GameStatus.WaitingForPlayer2;

    /// <summary>
    /// Who occupies the opposing seat: a waiting human (the share-link flow)
    /// or the built-in AI. Defaults to <see cref="OpponentType.Human"/> so the
    /// original two-player behaviour is preserved when no mode is requested.
    /// </summary>
    public OpponentType OpponentType { get; set; } = OpponentType.Human;

    public GameResult? Result { get; set; }

    public GameResultReason? ResultReason { get; set; }

    public int HalfmoveClock { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public byte[] Version { get; set; } = [];

    public List<Move> Moves { get; set; } = [];
}
