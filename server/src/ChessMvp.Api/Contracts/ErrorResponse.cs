namespace ChessMvp.Api.Contracts;

public sealed record ErrorResponse(string Error, string? Message = null);
