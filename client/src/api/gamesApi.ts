// Thin typed wrappers over httpClient for every documented game endpoint.
// Keep this file in sync with `src/types/gameTypes.ts` if the backend
// contract shifts.

import { apiRequest } from "./httpClient";
import type {
  CreateGameRequest,
  CreateGameResponse,
  GameStateResponse,
  GameOpponentType,
  JoinGameResponse,
  MoveHistoryResponse,
  PlayerColor,
  PromotionPieceType,
} from "../types/gameTypes";

// Creates a new game. With no arguments this creates the original
// human-vs-human game that waits for a second player to join. Pass an
// `opponent` of "Ai" (and a `mode` for the creator's side) to start an AI
// game, which the server creates as Active with the AI on the opposite seat.
// The `request` body is forwarded verbatim to POST /api/games so the server
// can pick the opponent mode (`opponent`: "Human" | "Ai") and, for AI games,
// which color the creator plays (`mode`). Omitting the request keeps the
// historical human-vs-human default, so existing callers that POST an empty
// body are unaffected.
export function createGame(request?: CreateGameRequest | null): Promise<CreateGameResponse> {
  return apiRequest<CreateGameResponse>("/api/games", {
    method: "POST",
    body: request ?? {},
  });
}

/**
 * Convenience overload for callers that only care about the opponent/mode.
 * Forwards `{ opponent, mode: null }` to POST /api/games. For AI games the
 * server assigns the AI to the seat opposite `mode` (defaulting the creator to
 * White when `mode` is omitted/null), so omitting `mode` mirrors the original
 * human-plays-White behaviour.
 */
export function createGameWithMode(
  opponent: GameOpponentType,
  mode?: PlayerColor | null,
): Promise<CreateGameResponse> {
  return createGame({ opponent, mode: mode ?? null });
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
