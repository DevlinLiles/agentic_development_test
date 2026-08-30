namespace ChessMvp.Domain.Entities;

/// <summary>
/// A legal capture annotated with its material gain. The gain is the victim's standard
/// material value minus the aggressor's, so a pawn capturing a queen scores highly while a
/// queen capturing a pawn scores negatively. Non-captures are never represented here: they are
/// scored zero and excluded from the capture-selection stage.
/// </summary>
public sealed record ScoredCapture
{
    public required LegalMove Move { get; init; }

    public required int MaterialGain { get; init; }
}
