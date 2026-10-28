// Component tests for CreateGameScreen's opponent-mode selection.
//
// The screen renders the OpponentChooser radio group and forwards the picked
// mode to gamesApi.createGame(). These tests pin the two behaviors the
// acceptance criteria call out: the default selection is unchanged ("Human",
// the pre-existing two-player flow) and switching modes changes what is
// submitted on create.

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { CreateGameScreen } from "./CreateGameScreen";
import * as gamesApi from "../api/gamesApi";
import type { CreateGameResponse, OpponentKind } from "../types/gameTypes";

// The gamesApi module is mocked wholesale: these tests assert the mode the
// screen passes to createGame(), not the wire format (that lives in
// gamesApi.test.ts). useNavigate still needs a routing context, so the screen
// is rendered inside a bare MemoryRouter.
vi.mock("../api/gamesApi");

const GAME_ID = "11111111-1111-1111-1111-111111111111";
const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

function makeCreateResponse(
  overrides: { opponent?: OpponentKind } = {},
): CreateGameResponse {
  return {
    gameId: GAME_ID,
    playerToken: "22222222-2222-2222-2222-222222222222",
    color: "White",
    joinUrl: `/game/${GAME_ID}`,
    gameState: {
      gameId: GAME_ID,
      status: "WaitingForPlayer2",
      fen: START_FEN,
      turn: "White",
      yourColor: "White",
      result: null,
      resultReason: null,
      moveCount: 0,
      isCheck: false,
      lastMove: null,
      opponentType: overrides.opponent ?? "Human",
    },
    opponentType: overrides.opponent ?? "Human",
  };
}

function renderScreen() {
  return render(
    <MemoryRouter initialEntries={["/"]}>
      <CreateGameScreen />
    </MemoryRouter>,
  );
}

describe("CreateGameScreen opponent-mode selection", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it("offers both opponent modes with Human checked by default", () => {
    renderScreen();

    // The selector is on the screen at all, and the default selection is
    // exactly the pre-existing behavior (two-player, join-link game).
    const human = screen.getByRole("radio", { name: "Play a friend" });
    const computer = screen.getByRole("radio", { name: "Play the computer" });
    expect(human).toBeChecked();
    expect(computer).not.toBeChecked();
    expect(screen.getByRole("button", { name: "Create Game" })).toBeInTheDocument();
  });

  it("submits the default Human mode without any prior interaction", async () => {
    vi.mocked(gamesApi.createGame).mockResolvedValueOnce(makeCreateResponse());
    const user = userEvent.setup();

    renderScreen();
    await user.click(screen.getByRole("button", { name: "Create Game" }));

    // Default submission: createGame("Human") — the same two-player flow the
    // screen had before the selector existed (an explicit Human is
    // indistinguishable from an absent body on the server).
    expect(gamesApi.createGame).toHaveBeenCalledWith("Human");
    expect(gamesApi.createGame).toHaveBeenCalledTimes(1);

    await waitFor(() => {
      expect(localStorage.getItem(`chess:session:${GAME_ID}`)).toBeTruthy();
    });
  });

  it("submits Computer after switching the mode", async () => {
    vi.mocked(gamesApi.createGame).mockResolvedValueOnce(
      makeCreateResponse({ opponent: "Computer" }),
    );
    const user = userEvent.setup();

    renderScreen();
    await user.click(screen.getByRole("radio", { name: "Play the computer" }));
    expect(screen.getByRole("radio", { name: "Play the computer" })).toBeChecked();

    await user.click(screen.getByRole("button", { name: "Create Game" }));
    expect(gamesApi.createGame).toHaveBeenCalledWith("Computer");
    expect(gamesApi.createGame).toHaveBeenCalledTimes(1);
  });

  it("switches back to Human after selecting Computer", async () => {
    vi.mocked(gamesApi.createGame).mockResolvedValueOnce(makeCreateResponse());
    const user = userEvent.setup();

    renderScreen();
    await user.click(screen.getByRole("radio", { name: "Play the computer" }));
    await user.click(screen.getByRole("radio", { name: "Play a friend" }));

    await user.click(screen.getByRole("button", { name: "Create Game" }));
    expect(gamesApi.createGame).toHaveBeenCalledWith("Human");
  });

  it("shows a per-mode busy label and keeps the last mode selected while creating", async () => {
    let resolveCreate: (value: CreateGameResponse) => void = () => {};
    vi.mocked(gamesApi.createGame).mockImplementationOnce(
      () =>
        new Promise<CreateGameResponse>((resolve) => {
          resolveCreate = resolve;
        }),
    );
    const user = userEvent.setup();

    renderScreen();
    await user.click(screen.getByRole("radio", { name: "Play the computer" }));
    await user.click(screen.getByRole("button", { name: "Create Game" }));

    // While the request is in flight: per-mode busy label, the radios are
    // disabled (can't change mode mid-request) and no second submit fires —
    // the button is aria-disabled and guarded, not unmounted.
    expect(screen.getByRole("button", { name: "Starting game…" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Starting game…" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    expect(screen.getByRole("radio", { name: "Play the computer" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Play the computer" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Starting game…" }));
    expect(gamesApi.createGame).toHaveBeenCalledTimes(1);

    // Resolve inside act so the pending saveSession + navigate state updates
    // flush cleanly before the test tears down the router.
    await act(async () => {
      resolveCreate(makeCreateResponse({ opponent: "Computer" }));
    });
  });

  it("clears a failed attempt's error when the mode changes", async () => {
    vi.mocked(gamesApi.createGame).mockRejectedValueOnce(
      new Error("boom while creating"),
    );
    const user = userEvent.setup();

    renderScreen();
    await user.click(screen.getByRole("button", { name: "Create Game" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("boom while creating");
    // The chosen mode is deliberately kept so a retry reuses the same mode.
    expect(screen.getByRole("radio", { name: "Play a friend" })).toBeChecked();

    // A mode switch invalidates the previous failure.
    await user.click(screen.getByRole("radio", { name: "Play the computer" }));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    // And a retry uses the newly selected mode.
    vi.mocked(gamesApi.createGame).mockResolvedValueOnce(
      makeCreateResponse({ opponent: "Computer" }),
    );
    await user.click(screen.getByRole("button", { name: "Create Game" }));
    expect(gamesApi.createGame).toHaveBeenLastCalledWith("Computer");
  });
});
