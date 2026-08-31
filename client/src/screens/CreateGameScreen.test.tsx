import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { CreateGameScreen } from "./CreateGameScreen";
import * as gamesApi from "../api/gamesApi";
import { useGameSession } from "../state/useGameSession";
import type { CreateGameResponse } from "../types/gameTypes";

vi.mock("../api/gamesApi");
vi.mock("../state/useGameSession");
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return {
    ...actual,
    useNavigate: () => navigateSpy,
  };
});

const navigateSpy = vi.fn();
const saveSessionSpy = vi.fn();

function makeCreateResponse(overrides: Partial<CreateGameResponse> = {}): CreateGameResponse {
  return {
    gameId: "game-123",
    playerToken: "token-abc",
    color: "White",
    joinUrl: "/game/game-123",
    opponentType: "Human",
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
      lastMove: null,
      opponentType: "Human",
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
    vi.resetAllMocks();
    navigateSpy.mockReset();
    saveSessionSpy.mockReset();
    vi.mocked(useGameSession).mockReturnValue({ saveSession: saveSessionSpy, getSession: vi.fn() });
  });

  it("offers both a human-opponent and an AI-opponent creation control", () => {
    renderScreen();
    expect(screen.getByRole("button", { name: "Create Game" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Play vs AI" })).toBeInTheDocument();
  });

  it("creates a human game, saves the session, and navigates to the game", async () => {
    const response = makeCreateResponse();
    vi.mocked(gamesApi.createGame).mockResolvedValue(response);

    const user = userEvent.setup();
    renderScreen();
    await user.click(screen.getByRole("button", { name: "Create Game" }));

    expect(gamesApi.createGame).toHaveBeenCalledOnce();
    expect(gamesApi.createAiGame).not.toHaveBeenCalled();
    expect(saveSessionSpy).toHaveBeenCalledWith({
      gameId: "game-123",
      playerToken: "token-abc",
      color: "White",
    });
    expect(navigateSpy).toHaveBeenCalledWith("/game/game-123");
  });

  it("creates an AI game, saves the session, and navigates to the game", async () => {
    const response = makeCreateResponse({
      joinUrl: null,
      opponentType: "Ai",
      gameState: {
        ...makeCreateResponse().gameState,
        status: "Active",
        opponentType: "Ai",
      },
    });
    vi.mocked(gamesApi.createAiGame).mockResolvedValue(response);

    const user = userEvent.setup();
    renderScreen();
    await user.click(screen.getByRole("button", { name: "Play vs AI" }));

    expect(gamesApi.createAiGame).toHaveBeenCalledOnce();
    expect(gamesApi.createGame).not.toHaveBeenCalled();
    expect(saveSessionSpy).toHaveBeenCalledWith({
      gameId: "game-123",
      playerToken: "token-abc",
      color: "White",
    });
    expect(navigateSpy).toHaveBeenCalledWith("/game/game-123");
  });

  it("surfaces an error and re-enables the buttons when AI creation fails", async () => {
    vi.mocked(gamesApi.createAiGame).mockRejectedValue(new Error("Server unavailable"));

    const user = userEvent.setup();
    renderScreen();
    await user.click(screen.getByRole("button", { name: "Play vs AI" }));

    expect(saveSessionSpy).not.toHaveBeenCalled();
    expect(navigateSpy).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toBeInTheDocument();
    // Button is re-enabled (no longer shows the "Creating…" label).
    expect(screen.getByRole("button", { name: "Play vs AI" })).toBeEnabled();
  });
});
