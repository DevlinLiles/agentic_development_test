// Tests for gamesApi.createGame's opponent-mode request wiring.
//
// The acceptance criteria cover "client contract types and API calls support
// opponent mode": this pins the exact wire shape each mode produces — which
// body (if any) POST /api/games receives and how it round-trips back through
// the response contract types.

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import * as gamesApi from "./gamesApi";
import { ApiError } from "./httpClient";
import type { CreateGameResponse, GameStateResponse } from "../types/gameTypes";

const BASE_URL = "https://localhost:7124";
const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

function makeCreateResponse(overrides: Partial<CreateGameResponse> = {}): CreateGameResponse {
  const gameState: GameStateResponse = {
    gameId: "11111111-1111-1111-1111-111111111111",
    status: "WaitingForPlayer2",
    fen: START_FEN,
    turn: "White",
    yourColor: "White",
    result: null,
    resultReason: null,
    moveCount: 0,
    isCheck: false,
    lastMove: null,
    opponentType: "Human",
  };
  return {
    gameId: "11111111-1111-1111-1111-111111111111",
    playerToken: "22222222-2222-2222-2222-222222222222",
    color: "White",
    joinUrl: "/game/11111111-1111-1111-1111-111111111111",
    gameState,
    opponentType: "Human",
    ...overrides,
  };
}

function makeComputerResponse(): CreateGameResponse {
  return makeCreateResponse({
    gameState: {
      ...makeCreateResponse().gameState,
      status: "Active",
      opponentType: "Computer",
    },
    opponentType: "Computer",
  });
}

describe("gamesApi.createGame opponent-mode wiring", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(new Response(JSON.stringify({}), { status: 201 }))),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("sends no body at all when no mode is passed (the pre-selection call shape)", async () => {
    await gamesApi.createGame();

    const [input, init] = vi.mocked(fetch).mock.calls[0] as unknown as [
      RequestInfo,
      RequestInit,
    ];
    expect(input).toBe(`${BASE_URL}/api/games`);
    expect(init.method).toBe("POST");
    // No body: no Content-Type header either — byte-for-byte the request the
    // client sent before opponent selection existed, and the one the server
    // resolves to a human game via EmptyBodyBehavior.Allow.
    expect(init.body).toBeUndefined();
    expect(init.headers).toEqual({});
  });

  it("sends { opponent: \"Computer\" } as the JSON body for a computer game", async () => {
    await gamesApi.createGame("Computer");

    const [, init] = vi.mocked(fetch).mock.calls[0] as unknown as [
      RequestInfo,
      RequestInit,
    ];
    expect(init.method).toBe("POST");
    expect(init.headers).toEqual({ "Content-Type": "application/json" });
    expect(JSON.parse(init.body as string)).toEqual({ opponent: "Computer" });
  });

  it("sends { opponent: \"Human\" } when an explicit Human is passed", async () => {
    await gamesApi.createGame("Human");

    const [, init] = vi.mocked(fetch).mock.calls[0] as unknown as [
      RequestInfo,
      RequestInit,
    ];
    expect(JSON.parse(init.body as string)).toEqual({ opponent: "Human" });
  });

  it("round-trips the opponentType field of the create-game response", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      new Response(JSON.stringify(makeComputerResponse()), { status: 201 }),
    );

    const response = await gamesApi.createGame("Computer");
    expect(response.opponentType).toBe("Computer");
    expect(response.gameState.status).toBe("Active");
    expect(response.gameState.opponentType).toBe("Computer");
  });

  it("round-trips a response from a pre-upgrade server that omits opponentType", async () => {
    const legacy = makeCreateResponse();
    delete (legacy as { opponentType?: unknown }).opponentType;
    delete (legacy.gameState as { opponentType?: unknown }).opponentType;

    vi.mocked(fetch).mockResolvedValueOnce(
      new Response(JSON.stringify(legacy), { status: 201 }),
    );

    // Optional on the client contract so a server that never sends the field
    // still reads as the default two-player game — no undefined crashes.
    const response = await gamesApi.createGame();
    expect(response.gameState.opponentType).toBeUndefined();
    expect(response.opponentType).toBeUndefined();
  });

  it("throws ApiError with the failure body when the server rejects the mode", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "InvalidInput" }), { status: 400 }),
    );

    await expect(gamesApi.createGame("Nope" as never)).rejects.toBeInstanceOf(ApiError);
  });
});
