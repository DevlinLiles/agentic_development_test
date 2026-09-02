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
  PromotionPieceType,
} from "../types/gameTypes";

// Creates a new game. With no arguments this creates the original
// human-vs-human game that waits for a second player to join. Pass an
// `opponent` of "Ai" (and a `mode` for the creator's side) to start an AI
// game, which the server creates as Active with the AI on the opposite seat.
export function createGame(request?: CreateGameRequest): Promise<CreateGameResponse> {
  return apiRequest<CreateGameResponse>("/api/games", {
    method: "POST",
    body: request,
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
