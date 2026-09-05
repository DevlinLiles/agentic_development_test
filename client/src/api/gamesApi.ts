// Thin typed wrappers over httpClient for every documented game endpoint.
// Keep this file in sync with `src/types/gameTypes.ts` if the backend
// contract shifts.

import { apiRequest } from "./httpClient";
import type {
  CreateGameResponse,
  GameMode,
  GameStateResponse,
  JoinGameResponse,
  MoveHistoryResponse,
  PromotionPieceType,
} from "../types/gameTypes";

/**
 * Creates a new game.
 *
 * `mode` selects between a two-player shared-seat game ("TwoPlayer") and a
 * game against the server's AI player ("VsAi"). It is forwarded as the `mode`
 * query parameter, which the backend binds to its nullable GameMode enum and
 * defaults to TwoPlayer when omitted — so existing callers that pass nothing
 * keep the original behavior.
 */
export function createGame(mode?: GameMode): Promise<CreateGameResponse> {
  const searchParams = new URLSearchParams();
  if (mode) {
    searchParams.set("mode", mode);
  }
  const query = searchParams.toString();
  const path = query.length > 0 ? `/api/games?${query}` : "/api/games";
  return apiRequest<CreateGameResponse>(path, { method: "POST" });
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
