import { describe, expect, it, vi, beforeEach } from "vitest";
import * as gamesApi from "./gamesApi";
import * as httpClient from "./httpClient";
import type { CreateGameResponse } from "../types/gameTypes";

vi.mock("./httpClient", () => ({
  apiRequest: vi.fn(),
  ApiError: class FakeApiError extends Error {},
}));

function makeCreateResponse(overrides: Partial<CreateGameResponse> = {}): CreateGameResponse {
  return {
    gameId: "game-1",
    playerToken: "token-1",
    color: "White",
    joinUrl: null,
    opponentType: "Ai",
    gameState: {
      gameId: "game-1",
      status: "Active",
      fen: "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
      turn: "White",
      yourColor: "White",
      result: null,
      resultReason: null,
      moveCount: 0,
      isCheck: false,
      lastMove: null,
      opponentType: "Ai",
    },
    ...overrides,
  };
}

describe("gamesApi", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it("createGame posts to the human-game endpoint", async () => {
    const response = makeCreateResponse({ opponentType: "Human", joinUrl: "/game/game-1" });
    vi.mocked(httpClient.apiRequest).mockResolvedValue(response);

    await gamesApi.createGame();

    expect(httpClient.apiRequest).toHaveBeenCalledWith("/api/games", { method: "POST" });
  });

  it("createAiGame posts to the AI-game endpoint and returns the response", async () => {
    const response = makeCreateResponse();
    vi.mocked(httpClient.apiRequest).mockResolvedValue(response);

    const result = await gamesApi.createAiGame();

    expect(httpClient.apiRequest).toHaveBeenCalledWith("/api/games/ai", { method: "POST" });
    expect(result).toBe(response);
  });
});
