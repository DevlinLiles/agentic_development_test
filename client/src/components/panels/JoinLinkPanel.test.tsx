import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { JoinLinkPanel } from "./JoinLinkPanel";
import type { GameStateResponse } from "../../types/gameTypes";

const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

function makeGameState(overrides: Partial<GameStateResponse>): GameStateResponse {
  return {
    gameId: "game-1",
    status: "WaitingForPlayer2",
    fen: START_FEN,
    turn: "White",
    yourColor: "White",
    mode: "TwoPlayer",
    result: null,
    resultReason: null,
    moveCount: 0,
    isCheck: false,
    lastMove: null,
    ...overrides,
  };
}

describe("JoinLinkPanel", () => {
  it("renders the shareable join link for a waiting TwoPlayer game", () => {
    render(<JoinLinkPanel gameState={makeGameState({})} />);
    expect(screen.getByText("Share this link with your opponent:")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Join link" })).toBeInTheDocument();
  });

  it("is hidden for a VsAi game even when White is to move (AC-6)", () => {
    // A VsAi game starts Active with no second human seat, so the join link
    // must never be shown for it — regardless of status.
    render(
      <JoinLinkPanel
        gameState={makeGameState({ mode: "VsAi", status: "Active" })}
      />,
    );
    expect(screen.queryByText("Share this link with your opponent:")).not.toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "Join link" })).not.toBeInTheDocument();
  });

  it("is hidden when the viewer is not the waiting White player", () => {
    render(<JoinLinkPanel gameState={makeGameState({ yourColor: "Black" })} />);
    expect(screen.queryByRole("textbox", { name: "Join link" })).not.toBeInTheDocument();
  });

  it("is hidden once the TwoPlayer game has started", () => {
    render(
      <JoinLinkPanel
        gameState={makeGameState({ status: "Active", yourColor: "White" })}
      />,
    );
    expect(screen.queryByRole("textbox", { name: "Join link" })).not.toBeInTheDocument();
  });
});
