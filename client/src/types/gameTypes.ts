// Types for the chess backend API contract.
//
// This file is the single source of truth for the shape of data exchanged
// with the server. If the backend contract shifts (enum casing, field
// names, etc.) once it's finished, update it here and in `src/api/gamesApi.ts`
// rather than chasing type mismatches across the app.

export type PlayerColor = "White" | "Black";

export type GameStatus = "WaitingForPlayer2" | "Active" | "Ended";

export type GameResult = "WhiteWins" | "BlackWins" | "Draw";

export type GameResultReason =
  | "Checkmate"
  | "Stalemate"
  | "FiftyMoveRule"
  | "Resignation";

export type PromotionPieceType = "Queen" | "Rook" | "Bishop" | "Knight";

// How a game's second seat is filled. TwoPlayer waits for a human to join via
// the join link; VsAi fills the Black seat with the AI so the human (White) can
// play immediately, and there is no shareable join URL.
export type GameMode = "TwoPlayer" | "VsAi";

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
  result: GameResult | null;
  resultReason: GameResultReason | null;
  moveCount: number;
  isCheck: boolean;
  mode: GameMode;
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
  // Null for VsAi games (no second player to join).
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
