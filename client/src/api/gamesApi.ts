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
  OpponentType,
  PromotionPieceType,
} from "../types/gameTypes";

/**
 * Creates a new game. When `opponentType` is omitted (the legacy/default
 * path) no request body is sent, preserving the existing parameterless
 * POST /api/games behavior for human-vs-human games (NFR-2.1).
 */
export function createGame(
  opponentType?: OpponentType,
): Promise<CreateGameResponse> {
  // Only send a body when a non-default opponent is chosen. This keeps the
  // backward-compatible parameterless request untouched for Human games.
  if (opponentType === undefined || opponentType === "Human") {
    return apiRequest<CreateGameResponse>("/api/games", { method: "POST" });
  }

  const body: CreateGameRequest = { opponentType };
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
