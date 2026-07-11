import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResignPanel } from "./ResignPanel";
import * as gamesApi from "../../api/gamesApi";
import { ApiError } from "../../api/httpClient";
import type { GameStateResponse } from "../../types/gameTypes";

vi.mock("../../api/gamesApi");

const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

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
    ...overrides,
  };
}

describe("ResignPanel", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it("renders a Resign button for an active game participant", () => {
    render(
      <ResignPanel
        gameId="game-1"
        playerToken="token-1"
        gameState={makeGameState()}
        onStateUpdate={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: "Resign" })).toBeInTheDocument();
  });

  it("does not render before the second player has joined", () => {
    const { container } = render(
      <ResignPanel
        gameId="game-1"
        playerToken="token-1"
        gameState={makeGameState({ status: "WaitingForPlayer2", yourColor: "White" })}
        onStateUpdate={vi.fn()}
      />,
    );

    expect(container).toBeEmptyDOMElement();
  });

  it("does not render once the game has ended", () => {
    const { container } = render(
      <ResignPanel
        gameId="game-1"
        playerToken="token-1"
        gameState={makeGameState({ status: "Ended", result: "WhiteWins", resultReason: "Checkmate" })}
        onStateUpdate={vi.fn()}
      />,
    );

    expect(container).toBeEmptyDOMElement();
  });

  it("requires confirmation before posting the resign request", async () => {
    const user = userEvent.setup();
    render(
      <ResignPanel
        gameId="game-1"
        playerToken="token-1"
        gameState={makeGameState()}
        onStateUpdate={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Resign" }));

    expect(screen.getByText("Are you sure you want to resign?")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm Resign" })).toBeInTheDocument();
    expect(gamesApi.resignGame).not.toHaveBeenCalled();
  });

  it("posts the resign request and forwards the resulting state on confirm", async () => {
    const user = userEvent.setup();
    const onStateUpdate = vi.fn();
    const endedState = makeGameState({
      status: "Ended",
      result: "BlackWins",
      resultReason: "Resignation",
    });
    vi.mocked(gamesApi.resignGame).mockResolvedValue(endedState);

    render(
      <ResignPanel
        gameId="game-1"
        playerToken="token-1"
        gameState={makeGameState()}
        onStateUpdate={onStateUpdate}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Resign" }));
    await user.click(screen.getByRole("button", { name: "Confirm Resign" }));

    expect(gamesApi.resignGame).toHaveBeenCalledWith("game-1", "token-1");
    expect(onStateUpdate).toHaveBeenCalledWith(endedState);
  });

  it("surfaces an error message when the server rejects the resign request", async () => {
    const user = userEvent.setup();
    const onStateUpdate = vi.fn();
    vi.mocked(gamesApi.resignGame).mockRejectedValue(
      new ApiError(409, { error: "GameNotActive" }),
    );

    render(
      <ResignPanel
        gameId="game-1"
        playerToken="token-1"
        gameState={makeGameState()}
        onStateUpdate={onStateUpdate}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Resign" }));
    await user.click(screen.getByRole("button", { name: "Confirm Resign" }));

    expect(gamesApi.resignGame).toHaveBeenCalled();
    expect(onStateUpdate).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toBeInTheDocument();
  });

  it("cancels back to the Resign button without posting", async () => {
    const user = userEvent.setup();
    render(
      <ResignPanel
        gameId="game-1"
        playerToken="token-1"
        gameState={makeGameState()}
        onStateUpdate={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Resign" }));
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.getByRole("button", { name: "Resign" })).toBeInTheDocument();
    expect(gamesApi.resignGame).not.toHaveBeenCalled();
  });
});
