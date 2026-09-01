import { describe, expect, it, vi, beforeEach } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useClickToMove } from "./useClickToMove";
import * as gamesApi from "../api/gamesApi";
import { ApiError } from "../api/httpClient";
import type { GameStateResponse } from "../types/gameTypes";

vi.mock("../api/gamesApi");

const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

// White pawn on e7, one step from promoting, nothing else relevant nearby.
const PROMOTION_FEN = "8/4P3/8/8/8/8/7k/7K w - - 0 40";

function makeGameState(overrides: Partial<GameStateResponse> = {}): GameStateResponse {
  return {
    gameId: "game-1",
    status: "Active",
    fen: START_FEN,
    turn: "White",
    yourColor: "White",
    result: null,
    resultReason: null,
    moveCount: 0,
    isCheck: false,
    lastMove: null,
    opponentType: "Human",
    ...overrides,
  };
}

describe("useClickToMove", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it("selects a piece then submits a move to a legal-looking destination", async () => {
    const onStateUpdate = vi.fn();
    const nextState = makeGameState({ turn: "Black", moveCount: 1 });
    vi.mocked(gamesApi.submitMove).mockResolvedValue(nextState);

    const { result } = renderHook(() =>
      useClickToMove({
        gameId: "game-1",
        playerToken: "token-1",
        yourColor: "White",
        gameState: makeGameState(),
        onStateUpdate,
      }),
    );

    act(() => {
      result.current.handleSquareClick("e2");
    });

    expect(result.current.phase.phase).toBe("PieceSelected");
    expect(result.current.selectedSquare).toBe("e2");
    expect(result.current.legalDestinations).toContain("e4");

    await act(async () => {
      result.current.handleSquareClick("e4");
    });

    expect(gamesApi.submitMove).toHaveBeenCalledWith("game-1", "token-1", "e2", "e4", null);
    expect(onStateUpdate).toHaveBeenCalledWith(nextState);
    expect(result.current.phase.phase).toBe("Idle");
    expect(result.current.error).toBeNull();
  });

  it("reverts selection and surfaces an error when the server rejects the move", async () => {
    const onStateUpdate = vi.fn();
    vi.mocked(gamesApi.submitMove).mockRejectedValue(
      new ApiError(400, { error: "IllegalMove", message: "That piece can't move there." }),
    );

    const { result } = renderHook(() =>
      useClickToMove({
        gameId: "game-1",
        playerToken: "token-1",
        yourColor: "White",
        gameState: makeGameState(),
        onStateUpdate,
      }),
    );

    act(() => {
      result.current.handleSquareClick("e2");
    });
    expect(result.current.selectedSquare).toBe("e2");

    await act(async () => {
      result.current.handleSquareClick("e4");
    });

    expect(gamesApi.submitMove).toHaveBeenCalledWith("game-1", "token-1", "e2", "e4", null);
    expect(onStateUpdate).not.toHaveBeenCalled();
    expect(result.current.phase.phase).toBe("Idle");
    expect(result.current.selectedSquare).toBeNull();
    expect(result.current.error).toBe("That piece can't move there.");
  });

  it("opens the promotion picker before submitting when a pawn reaches the back rank", async () => {
    const onStateUpdate = vi.fn();
    const nextState = makeGameState({ fen: START_FEN, moveCount: 41 });
    vi.mocked(gamesApi.submitMove).mockResolvedValue(nextState);

    const { result } = renderHook(() =>
      useClickToMove({
        gameId: "game-1",
        playerToken: "token-1",
        yourColor: "White",
        gameState: makeGameState({ fen: PROMOTION_FEN }),
        onStateUpdate,
      }),
    );

    act(() => {
      result.current.handleSquareClick("e7");
    });
    expect(result.current.legalDestinations).toContain("e8");

    act(() => {
      result.current.handleSquareClick("e8");
    });

    expect(result.current.phase.phase).toBe("PromotionPending");
    expect(result.current.promotionPending).toEqual({ from: "e7", to: "e8" });
    expect(gamesApi.submitMove).not.toHaveBeenCalled();

    await act(async () => {
      result.current.handlePromotionSelect("Queen");
    });

    expect(gamesApi.submitMove).toHaveBeenCalledWith("game-1", "token-1", "e7", "e8", "Queen");
    expect(onStateUpdate).toHaveBeenCalledWith(nextState);
    expect(result.current.phase.phase).toBe("Idle");
  });

  it("does nothing when clicking an opponent's piece or an empty non-destination square", () => {
    const onStateUpdate = vi.fn();
    const { result } = renderHook(() =>
      useClickToMove({
        gameId: "game-1",
        playerToken: "token-1",
        yourColor: "White",
        gameState: makeGameState(),
        onStateUpdate,
      }),
    );

    act(() => {
      result.current.handleSquareClick("e7"); // black pawn, not selectable by White
    });

    expect(result.current.phase.phase).toBe("Idle");
    expect(result.current.selectedSquare).toBeNull();
    expect(gamesApi.submitMove).not.toHaveBeenCalled();
  });
});
