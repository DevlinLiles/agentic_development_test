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

    public int HalfmoveClock { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the black seat is occupied by the heuristic AI opponent
    /// rather than a second human; there is no join link to share and no waiting-for-player-2
    /// state. The human always plays white in AI games for MVP simplicity.
    /// </summary>
    public bool IsVsAi { get; set; }

    public byte[] Version { get; set; } = [];

    public List<Move> Moves { get; set; } = [];
}
