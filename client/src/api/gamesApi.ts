// Thin typed wrappers over httpClient for every documented game endpoint.
// Keep this file in sync with `src/types/gameTypes.ts` if the backend
// contract shifts.

import { apiRequest } from "./httpClient";
import type {
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
 * ever sent; anything else defaults to a human game server-side.
 */
export function createGame(opponent?: OpponentKind): Promise<CreateGameResponse> {
  return apiRequest<CreateGameResponse>("/api/games", {
    method: "POST",
    body: opponent ? { opponent } : undefined,
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
