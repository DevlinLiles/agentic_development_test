import { describe, expect, it, beforeEach, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { CreateGameScreen } from "./CreateGameScreen";
import * as gamesApi from "../api/gamesApi";
import type { CreateGameResponse } from "../types/gameTypes";

vi.mock("../api/gamesApi");

const STORAGE_KEY = "chess:createPrefs";

function makeCreateResponse(overrides: Partial<CreateGameResponse> = {}): CreateGameResponse {
  return {
    gameId: "game-123",
    playerToken: "token-abc",
    color: "White",
    joinUrl: "/game/game-123",
    isVsAi: false,
    gameState: {
      gameId: "game-123",
      status: "WaitingForPlayer2",
      fen: "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
      turn: "White",
      yourColor: "White",
      result: null,
      resultReason: null,
      moveCount: 0,
      isCheck: false,
      isVsAi: false,
      lastMove: null,
    },
    ...overrides,
  };
}

function renderScreen() {
  return render(
    <MemoryRouter>
      <CreateGameScreen />
    </MemoryRouter>,
  );
}

describe("CreateGameScreen", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.resetAllMocks();
  });

  it("defaults to the Human opponent and White color", () => {
    renderScreen();

    expect(screen.getByRole("radio", { name: /Play a friend/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /^White/i })).toBeChecked();
  });

  it("lets the user select the vs. AI opponent mode", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("radio", { name: /Play the computer/i }));

    expect(screen.getByRole("radio", { name: /Play the computer/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /Play a friend/i })).not.toBeChecked();
  });

  it("lets the user select the Black color", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("radio", { name: /^Black/i }));

    expect(screen.getByRole("radio", { name: /^Black/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /^White/i })).not.toBeChecked();
  });

  it("persists the selected opponent and color for the duration of the session", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("radio", { name: /Play the computer/i }));
    await user.click(screen.getByRole("radio", { name: /^Black/i }));

    const stored = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? "null");
    expect(stored).toEqual({ opponent: "Ai", color: "Black" });

    // A fresh mount (simulating a re-visit later in the same session) hydrates
    // the persisted selection rather than resetting to defaults.
    renderScreen();

    expect(screen.getByRole("radio", { name: /Play the computer/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /^Black/i })).toBeChecked();
  });

  it("creates the game with the selected opponent", async () => {
    const user = userEvent.setup();
    vi.mocked(gamesApi.createGame).mockResolvedValue(makeCreateResponse({ isVsAi: true }));

    renderScreen();

    await user.click(screen.getByRole("radio", { name: /Play the computer/i }));
    await user.click(screen.getByRole("button", { name: /Create Game/i }));

    expect(gamesApi.createGame).toHaveBeenCalledWith("Ai");
  });

  it("shows an error message when game creation fails", async () => {
    const user = userEvent.setup();
    vi.mocked(gamesApi.createGame).mockRejectedValue(new Error("Network down"));

    renderScreen();

    await user.click(screen.getByRole("button", { name: /Create Game/i }));

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(gamesApi.createGame).toHaveBeenCalled();
  });
});
