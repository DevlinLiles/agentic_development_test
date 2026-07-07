namespace ChessMvp.Domain.Abstractions;

public sealed record MoveApplicationResult
{
    public required bool IsLegal { get; init; }

    public string? San { get; init; }

    public string? ResultingFen { get; init; }

    public bool IsCheck { get; init; }

    public bool IsCheckmate { get; init; }

    public bool IsStalemate { get; init; }

    public bool IsFiftyMoveDraw { get; init; }

    public MoveFailureReason? FailureReason { get; init; }

    public static MoveApplicationResult Illegal(MoveFailureReason reason) =>
        new() { IsLegal = false, FailureReason = reason };
}
