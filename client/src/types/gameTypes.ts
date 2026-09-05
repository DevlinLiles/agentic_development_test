// Types for the chess backend API contract.
//
// This file is the single source of truth for the shape of data exchanged
// with the server. If the backend contract shifts (enum casing, field
// names, etc.) once it's finished, update it here and in `src/api/gamesApi.ts`
// rather than chasing type mismatches across the app.

export type PlayerColor = "White" | "Black";

export type GameStatus = "WaitingForPlayer2" | "Active" | "Ended";

// Mirrors the server-side ChessMvp.Domain.Entities.GameMode enum, which is
// serialized as a string via JsonStringEnumConverter. "TwoPlayer" is a
// shared-seat game awaiting a second human; "VsAi" pits the creator against
// the server's heuristic AI player (the human always plays White).
export type GameMode = "TwoPlayer" | "VsAi";

export type GameResult = "WhiteWins" | "BlackWins" | "Draw";

export type GameResultReason =
  | "Checkmate"
  | "Stalemate"
  | "FiftyMoveRule"
  | "Resignation";

export type PromotionPieceType = "Queen" | "Rook" | "Bishop" | "Knight";

export interface LastMove {
  from: string;
  to: string;
  san: string;
}

export interface GameStateResponse {
  gameId: string;
  status: GameStatus;
  fen: string;
  turn: PlayerColor;
  yourColor: PlayerColor | null;
  mode: GameMode;
  result: GameResult | null;
  resultReason: GameResultReason | null;
  moveCount: number;
  isCheck: boolean;
  lastMove: LastMove | null;
}

export interface MoveHistoryEntry {
  moveNumber: number;
  color: PlayerColor;
  san: string;
  from: string;
  to: string;
  promotion: PromotionPieceType | null;
  isCheck: boolean;
  isCheckmate: boolean;
}

export interface MoveHistoryResponse {
  moves: MoveHistoryEntry[];
}

export interface CreateGameResponse {
  gameId: string;
  playerToken: string;
  color: PlayerColor;
  mode: GameMode;
  // Null for VsAi games, which have no second human seat to join.
  joinUrl: string | null;
  gameState: GameStateResponse;
}

export interface JoinGameResponse {
  gameId: string;
  playerToken: string;
  color: PlayerColor;
  gameState: GameStateResponse;
}

// Documented failure body shapes for POST /moves. `error` is the only field
// guaranteed present; `message` accompanies "IllegalMove" per the contract.
export interface MoveErrorBody {
  error: "IllegalMove" | "PromotionRequired" | string;
  message?: string;
}
