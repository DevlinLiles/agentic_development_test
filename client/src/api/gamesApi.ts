// Thin typed wrappers over httpClient for every documented game endpoint.
// Keep this file in sync with `src/types/gameTypes.ts` if the backend
// contract shifts.

import { apiRequest } from "./httpClient";
import type {
  CreateGameRequest,
  CreateGameResponse,
  GameStateResponse,
  GameOpponent,
  JoinGameResponse,
  MoveHistoryResponse,
  PromotionPieceType,
} from "../types/gameTypes";

export function createGame(opponent: GameOpponent = "Human"): Promise<CreateGameResponse> {
  return apiRequest<CreateGameResponse>("/api/games", {
    method: "POST",
    body: { opponent } satisfies CreateGameRequest,
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
