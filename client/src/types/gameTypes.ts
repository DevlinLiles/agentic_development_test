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

/**
 * Who occupies the second seat of a game.
 * - "Human": the classic two-player game. The game is created in
 *   WaitingForPlayer2 and the second seat is filled via the shareable link.
 * - "Computer": a solo game against the server-side AI opponent. The game is
 *   Active immediately after creation and the second seat can never be joined.
 */
export type OpponentKind = "Human" | "Computer";

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
  /**
   * Opponent kind for this game. Optional, and `undefined` means "Human":
   * snapshots produced before this field existed (or by a server that has not
   * been upgraded yet) omit it, and the default two-player behavior is the
   * safe fallback everywhere it is read.
   */
  opponentType?: OpponentKind;
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

/** Request body for POST /api/games — selecting the opponent mode. */
export interface CreateGameRequest {
  opponent?: OpponentKind;
}

export interface CreateGameResponse {
  gameId: string;
  playerToken: string;
  color: PlayerColor;
  joinUrl: string;
  gameState: GameStateResponse;
  /** Mirrors the requested opponent kind; omitted/undefined for human games. */
  opponentType?: OpponentKind;
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
