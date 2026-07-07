using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

public sealed record MoveRequest(string FromSquare, string ToSquare, PromotionPieceType? Promotion);
