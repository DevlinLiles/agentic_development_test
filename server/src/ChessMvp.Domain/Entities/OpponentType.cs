namespace ChessMvp.Domain.Entities;

/// <summary>
/// Distinguishes a two-human game (the original share-link flow) from a
/// single-user game against the built-in AI opponent. Serialized to the
/// client as a JSON string ("Human" / "Ai") via the global
/// JsonStringEnumConverter, matching the client's <c>OpponentType</c> union.
/// </summary>
public enum OpponentType
{
    Human = 0,

    Ai = 1,
}
