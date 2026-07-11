import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusPanel } from "./StatusPanel";
import type { GameStateResponse } from "../../types/gameTypes";

const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

function makeGameState(overrides: Partial<GameStateResponse>): GameStateResponse {
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

describe("StatusPanel", () => {
  it("renders waiting for opponent", () => {
    render(
      <StatusPanel
        gameState={makeGameState({ status: "WaitingForPlayer2", turn: "White", yourColor: "White" })}
      />,
    );
    expect(screen.getByText("Waiting for opponent")).toBeInTheDocument();
  });

  it("renders your turn when it's the active player's turn", () => {
    render(<StatusPanel gameState={makeGameState({ status: "Active", turn: "White", yourColor: "White" })} />);
    expect(screen.getByText("Your turn")).toBeInTheDocument();
  });

  it("renders opponent's turn when it isn't the active player's turn", () => {
    render(<StatusPanel gameState={makeGameState({ status: "Active", turn: "Black", yourColor: "White" })} />);
    expect(screen.getByText("Opponent's turn")).toBeInTheDocument();
  });

  it("renders a check indicator alongside the turn message", () => {
    render(
      <StatusPanel
        gameState={makeGameState({ status: "Active", turn: "White", yourColor: "White", isCheck: true })}
      />,
    );
    expect(screen.getByText("Your turn")).toBeInTheDocument();
    expect(screen.getByText("Check!")).toBeInTheDocument();
  });

  it("renders checkmate white wins", () => {
    render(
      <StatusPanel
        gameState={makeGameState({
          status: "Ended",
          result: "WhiteWins",
          resultReason: "Checkmate",
        })}
      />,
    );
    expect(screen.getByText("Checkmate — White wins")).toBeInTheDocument();
  });

  it("renders checkmate black wins", () => {
    render(
      <StatusPanel
        gameState={makeGameState({
          status: "Ended",
          result: "BlackWins",
          resultReason: "Checkmate",
        })}
      />,
    );
    expect(screen.getByText("Checkmate — Black wins")).toBeInTheDocument();
  });

  it("renders resignation white wins", () => {
    render(
      <StatusPanel
        gameState={makeGameState({
          status: "Ended",
          result: "WhiteWins",
          resultReason: "Resignation",
        })}
      />,
    );
    expect(screen.getByText("Resignation — White wins")).toBeInTheDocument();
  });

  it("renders resignation black wins", () => {
    render(
      <StatusPanel
        gameState={makeGameState({
          status: "Ended",
          result: "BlackWins",
          resultReason: "Resignation",
        })}
      />,
    );
    expect(screen.getByText("Resignation — Black wins")).toBeInTheDocument();
  });

  it("renders stalemate as a draw", () => {
    render(
      <StatusPanel
        gameState={makeGameState({
          status: "Ended",
          result: "Draw",
          resultReason: "Stalemate",
        })}
      />,
    );
    expect(screen.getByText("Stalemate — Draw")).toBeInTheDocument();
  });

  it("renders the fifty-move rule draw banner", () => {
    render(
      <StatusPanel
        gameState={makeGameState({
          status: "Ended",
          result: "Draw",
          resultReason: "FiftyMoveRule",
        })}
      />,
    );
    expect(screen.getByText("Draw — Fifty-move rule")).toBeInTheDocument();
  });
});
