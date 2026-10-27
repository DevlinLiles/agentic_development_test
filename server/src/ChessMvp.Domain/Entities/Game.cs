namespace ChessMvp.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }

    public Guid? WhiteSlotToken { get; set; }

    public Guid? BlackSlotToken { get; set; }

    /// <summary>
    /// How the second seat of this game is (or can be) filled — the per-game mode
    /// discriminator, persisted alongside the rest of the game state.
    ///
    /// Nullable on purpose: the backing column was introduced additively (nullable with a
    /// default, see the AddOpponentModeToGame migration), so rows written before the
    /// discriminator existed read back as <c>null</c>. Consumers must normalize <c>null</c> to
    /// <see cref="Entities.OpponentMode.Human"/> — the pre-existing two-player behavior —
    /// rather than surfacing a third "unknown" mode.
    ///
    /// Note: new games are seeded with Human here (not left to the column default) so the
    /// in-memory entity matches what a round-trip through the database would produce.
    /// </summary>
    public OpponentMode? OpponentMode { get; set; } = ChessMvp.Domain.Entities.OpponentMode.Human;

    public string CurrentFen { get; set; } = ChessConstants.StartingFen;

    public PlayerColor Turn { get; set; } = PlayerColor.White;

    public GameStatus Status { get; set; } = GameStatus.WaitingForPlayer2;

    public GameResult? Result { get; set; }

    public GameResultReason? ResultReason { get; set; }

    public int HalfmoveClock { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public byte[] Version { get; set; } = [];

    public List<Move> Moves { get; set; } = [];
}
