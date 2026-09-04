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

// `mode` is optional so existing callers that omit it receive the original
// TwoPlayer behaviour from the server. Pass "VsAi" to start a single-player
// game against the AI engine.
export function createGame(mode?: GameMode): Promise<CreateGameResponse> {
  const search = mode ? `?mode=${encodeURIComponent(mode)}` : "";
  return apiRequest<CreateGameResponse>(`/api/games${search}`, { method: "POST" });
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
