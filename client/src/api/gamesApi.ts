// Thin typed wrappers over httpClient for every documented game endpoint.
// Keep this file in sync with `src/types/gameTypes.ts` if the backend
// contract shifts.

import { apiRequest } from "./httpClient";
import type {
  CreateGameRequest,
  CreateGameResponse,
  GameStateResponse,
  JoinGameResponse,
  MoveHistoryResponse,
  OpponentKind,
  PromotionPieceType,
} from "../types/gameTypes";

/**
 * Creates a new game.
 *
 * `opponent` selects the mode:
 * - "Human" (or omitted): two-player game, created WaitingForPlayer2 — the
 *   shareable join link flow, byte-for-byte the previous behavior.
 * - "Computer": solo game against the server-side AI, created Active with the
 *   caller seated as White and no joinable second seat.
 *
 * Only values the create UI presents as selectable ("Human" | "Computer") are
 * ever sent; anything else defaults to a human game server-side. When no mode
 * is passed the request carries no body at all — the exact pre-selection call
 * shape the server still binds to a human game.
 */
export function createGame(opponent?: OpponentKind): Promise<CreateGameResponse> {
  // Typed against CreateGameRequest (the contract's request shape) so this
  // call site cannot drift from `src/types/gameTypes.ts` if the wire field
  // name or optionality ever shifts again.
  const body: CreateGameRequest | undefined = opponent
    ? { opponent }
    : undefined;

  return apiRequest<CreateGameResponse>("/api/games", {
    method: "POST",
    body,
  });
}

export function joinGame(gameId: string): Promise<JoinGameResponse> {
  return apiRequest<JoinGameResponse>(`/api/games/${gameId}/join`, {
    method: "POST",
  });
}

export function getGameState(
  gameId: string,
  token?: string | null,
): Promise<GameStateResponse> {
  return apiRequest<GameStateResponse>(`/api/games/${gameId}`, { token });
}

export function submitMove(
  gameId: string,
  token: string,
  fromSquare: string,
  toSquare: string,
  promotion: PromotionPieceType | null,
): Promise<GameStateResponse> {
  return apiRequest<GameStateResponse>(`/api/games/${gameId}/moves`, {
    method: "POST",
    token,
    body: { fromSquare, toSquare, promotion },
  });
}

export function getMoveHistory(
  gameId: string,
  token?: string | null,
): Promise<MoveHistoryResponse> {
  return apiRequest<MoveHistoryResponse>(`/api/games/${gameId}/moves`, {
    token,
  });
}
