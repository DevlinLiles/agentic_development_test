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

// Whether the second seat is a human (waits for a join) or the computer (the
// game starts immediately as Active with the AI taking the requested seat).
// Mirrors the server-side `GameOpponentType` enum (Human, Ai) and doubles as
// the game "mode" (human-vs-human vs. human-vs-AI) surfaced through the API.
export type GameOpponentType = "Human" | "Ai";

// Backward-compatible alias; older call sites still import `OpponentType`.
export type OpponentType = GameOpponentType;

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
  lastMove: LastMove | null;
  // The kind of opponent for the second seat. Human games wait for a join; Ai
  // games start Active with the computer on the seat named by `aiColor`.
  opponentType: GameOpponentType;
  // The seat the AI occupies, or null for human-vs-human games.
  aiColor: PlayerColor | null;
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

// Request body for POST /api/games. Both fields are optional server-side
// (defaulting to a human game where the creator plays White); pass `Ai` to
// start an AI game, and `mode` to pick the side the creator plays (the AI
// takes the opposite seat). Omitting the body (or leaving the fields null)
// keeps the historical human-vs-human default, so existing callers that POST
// an empty body are unaffected.
export interface CreateGameRequest {
  opponent?: GameOpponentType | null;
  mode?: PlayerColor | null;
}

export interface CreateGameResponse {
  gameId: string;
  playerToken: string;
  color: PlayerColor;
  // Only present for human-vs-human games; AI games have no second player to
  // share a join link with.
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
