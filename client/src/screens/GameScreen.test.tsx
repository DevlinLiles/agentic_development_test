// Verification tests for GameScreen's AI-game ("Computer" opponent) semantics.
//
// AI games differ from the classic two-player flow in three ways that all
// land in this screen:
//   1. Initialization/spectators: an AI game is created Active with no
//      joinable second seat, so an un-seated visitor is never a
//      spectator-in-waiting — they must get the not-a-participant message
//      (per product spec there is no spectator mode), never a join attempt
//      and never a read-only board.
//   2. Reload: the creator reloads with a saved session, so the screen must
//      reuse that session (fetch state with its token, reconnect the hub
//      with it) and never try to join.
//   3. SignalR snapshots: the AI reply arrives as a full snapshot broadcast
//      over the same group the human tab is already in. The screen replaces
//      its state wholesale and must re-patch `yourColor` from the session
//      (the broadcast always carries null for it).
// Plus the join-link suppression itself: no JoinLinkPanel for AI games, and
// — as a regression guard — the panel still shows for human games.
//
// ChessBoard and MoveHistoryPanel are stubbed (they own click-to-move and
// move-history fetching, out of scope here); StatusPanel and JoinLinkPanel
// are the real components so the suppression is verified end-to-end through
// GameScreen's render tree. The hub hook is replaced with a controllable
// mock so snapshots can be pushed in and the (gameId, playerToken) pair it
// is handed can be asserted. (The real hook is called on every render even
// before a session exists — it self-skips connecting when both args are
// null — so "not connected" is asserted as "never handed a seat".)

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ReactElement } from "react";
import { GameScreen } from "./GameScreen";
import * as gamesApi from "../api/gamesApi";
import type { GameStateResponse } from "../types/gameTypes";

// Controllable stand-in for the SignalR hook. `latestState` is whatever the
// test wants the hub to have most recently pushed; `calls` records the
// (gameId, playerToken) pair of every render so tests can assert both the
// "connects with the saved session" and the "never handed a seat" behaviors.
const hubMock = vi.hoisted(() => ({
  latestState: null as GameStateResponse | null,
  calls: [] as Array<{ gameId: string | null; playerToken: string | null }>,
}));

vi.mock("../signalr/useGameHubConnection", () => ({
  useGameHubConnection: (gameId: string | null, playerToken: string | null) => {
    hubMock.calls.push({ gameId, playerToken });
    return {
      latestState: hubMock.latestState,
      connectionStatus: "Connected" as const,
    };
  },
}));

// Board/history stubs: they surface exactly the props GameScreen derives
// from the snapshot (yourColor, moveCount, fen) so snapshot handling can be
// asserted without dragging in click-to-move or history fetching.
vi.mock("../components/board/ChessBoard", () => ({
  ChessBoard: (props: { gameState: GameStateResponse; yourColor: string | null }) => (
    <div
      data-testid="chess-board-stub"
      data-your-color={String(props.gameState.yourColor)}
      data-move-count={String(props.gameState.moveCount)}
      data-fen={props.gameState.fen}
    />
  ),
}));

vi.mock("../components/panels/MoveHistoryPanel", () => ({
  MoveHistoryPanel: (props: { refreshKey: number }) => (
    <div data-testid="move-history-stub" data-move-count={String(props.refreshKey)} />
  ),
}));

// Auto-mocked wholesale: these tests assert which endpoints GameScreen hits
// (and, crucially, which it must NOT hit — joinGame) and with what payload.
vi.mock("../api/gamesApi");

const GAME_ID = "33333333-3333-3333-3333-333333333333";
const PLAYER_TOKEN = "44444444-4444-4444-4444-444444444444";
const SESSION_KEY = `chess:session:${GAME_ID}`;
const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
const AFTER_REPLY_FEN =
  "rnbqkbnr/pppp1pppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2";

function makeAiState(overrides: Partial<GameStateResponse> = {}): GameStateResponse {
  return {
    gameId: GAME_ID,
    status: "Active",
    fen: START_FEN,
    turn: "White",
    yourColor: "White",
    result: null,
    resultReason: null,
    moveCount: 1,
    isCheck: false,
    // The engine's reply has landed (Black's first move) — a realistic
    // mid-game AI snapshot.
    lastMove: { from: "e7", to: "e5", san: "e5" },
    opponentType: "Computer",
    ...overrides,
  };
}

function makeHumanState(overrides: Partial<GameStateResponse> = {}): GameStateResponse {
  return {
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
    opponentType: "Human",
    ...overrides,
  };
}

function saveSession(color: "White" | "Black" = "White") {
  localStorage.setItem(
    SESSION_KEY,
    JSON.stringify({ gameId: GAME_ID, playerToken: PLAYER_TOKEN, color }),
  );
}

function gameTree(): ReactElement {
  return (
    <MemoryRouter initialEntries={[`/game/${GAME_ID}`]}>
      <Routes>
        <Route path="/game/:gameId" element={<GameScreen />} />
      </Routes>
    </MemoryRouter>
  );
}

describe("GameScreen AI-game (Computer opponent) semantics", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    hubMock.latestState = null;
    hubMock.calls = [];
  });

  afterEach(() => {
    localStorage.clear();
  });

  it("reuses the saved session on reload of an AI game and never attempts to join", async () => {
    saveSession("White");
    // Reload after the engine replied: two plies on the board.
    vi.mocked(gamesApi.getGameState).mockResolvedValueOnce(
      makeAiState({ fen: AFTER_REPLY_FEN, moveCount: 2, turn: "White" }),
    );

    render(gameTree());

    // The board renders from the reloaded state — no not-a-participant
    // dead-end for the creator who reloads.
    const board = await screen.findByTestId("chess-board-stub");
    expect(board).toHaveAttribute("data-move-count", "2");
    expect(board).toHaveAttribute("data-fen", AFTER_REPLY_FEN);

    // Reload went through the saved session: state was fetched with its
    // token, and the hub is reconnected to the same (gameId, token) channel
    // — the AI's next reply broadcast lands on it without any second tab.
    expect(gamesApi.getGameState).toHaveBeenCalledWith(GAME_ID, PLAYER_TOKEN);
    expect(hubMock.calls[hubMock.calls.length - 1]).toEqual({
      gameId: GAME_ID,
      playerToken: PLAYER_TOKEN,
    });

    // The join endpoint is never touched on reload of an AI game — its
    // second seat is the engine's and can never be claimed.
    expect(gamesApi.joinGame).not.toHaveBeenCalled();

    // JoinLinkPanel is suppressed for AI games.
    expect(screen.queryByLabelText("Join link")).not.toBeInTheDocument();
    expect(
      screen.queryByText("Share this link with your opponent:"),
    ).not.toBeInTheDocument();
  });

  it("replaces state wholesale from an AI-reply snapshot and re-patches yourColor from the session", async () => {
    saveSession("White");
    // Initial load: human's first move is on the board, engine is thinking.
    vi.mocked(gamesApi.getGameState).mockResolvedValueOnce(
      makeAiState({
        moveCount: 1,
        turn: "Black",
        lastMove: { from: "e2", to: "e4", san: "e4" },
      }),
    );

    const { rerender } = render(gameTree());
    await screen.findByTestId("chess-board-stub");
    expect(screen.getByTestId("chess-board-stub")).toHaveAttribute("data-move-count", "1");

    // The AI reply arrives as a full-snapshot broadcast. The hub always
    // sends yourColor: null (it's a per-connection fact the group broadcast
    // doesn't know) — GameScreen must patch it back in from the session
    // rather than rendering as "no seat".
    hubMock.latestState = makeAiState({
      fen: AFTER_REPLY_FEN,
      turn: "White",
      moveCount: 2,
      yourColor: null,
      lastMove: { from: "e7", to: "e5", san: "e5" },
    });

    rerender(gameTree());

    // The snapshot replaced the previous state wholesale: fen, moveCount and
    // the move-history refresh key all moved, and yourColor came back as the
    // session's White — the human keeps their seat (and thus their turn
    // indicator) after the engine's reply.
    const board = screen.getByTestId("chess-board-stub");
    expect(board).toHaveAttribute("data-fen", AFTER_REPLY_FEN);
    expect(board).toHaveAttribute("data-move-count", "2");
    expect(board).toHaveAttribute("data-your-color", "White");
    expect(screen.getByTestId("move-history-stub")).toHaveAttribute("data-move-count", "2");

    // And the connection that carried this snapshot is still the saved
    // session's channel — no second context was ever needed.
    expect(hubMock.calls[hubMock.calls.length - 1]).toEqual({
      gameId: GAME_ID,
      playerToken: PLAYER_TOKEN,
    });
  });

  it("gives an un-seated visitor the not-a-participant message, never a join and never a board", async () => {
    // A fresh browser (no saved session) opens an in-progress AI game. The
    // engine occupies Black from creation, so this visitor can never be a
    // spectator-in-waiting: there is no second seat to claim.
    vi.mocked(gamesApi.getGameState).mockResolvedValueOnce(
      makeAiState({ moveCount: 2, fen: AFTER_REPLY_FEN }),
    );

    render(gameTree());

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("You're not a participant in this game.");
    expect(alert).toHaveTextContent("no saved session");

    // No join attempt was made (an AI game's second seat is not joinable)
    // and no board is rendered (no read-only spectator view).
    expect(gamesApi.joinGame).not.toHaveBeenCalled();
    expect(screen.queryByTestId("chess-board-stub")).not.toBeInTheDocument();

    // The hub was never handed a seat: every render passed null for both
    // (gameId, playerToken), which makes the real hook skip connecting —
    // so no SignalR channel is ever opened for a non-participant.
    expect(hubMock.calls.length).toBeGreaterThan(0);
    for (const call of hubMock.calls) {
      expect(call).toEqual({ gameId: null, playerToken: null });
    }
  });

  it("still renders JoinLinkPanel for a human WaitingForPlayer2 game (regression guard)", async () => {
    saveSession("White");
    vi.mocked(gamesApi.getGameState).mockResolvedValueOnce(makeHumanState());

    render(gameTree());

    // The suppression is scoped to AI games only: the classic two-player
    // waiting-for-opponent panel is untouched.
    const input = await screen.findByLabelText("Join link");
    expect(input).toBeVisible();
    expect(input).toHaveValue(`${window.location.origin}/game/${GAME_ID}`);
  });

  it("hides the join link for a Computer game regardless of status (defense in depth)", async () => {
    // Should the status field ever evolve (e.g. a future "AI warm-up" state),
    // the panel stays off for any Computer game, and a session-less visitor
    // still never tries to join.
    vi.mocked(gamesApi.getGameState).mockResolvedValueOnce(
      makeAiState({ status: "WaitingForPlayer2" }),
    );

    render(gameTree());

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("You're not a participant in this game.");
    expect(gamesApi.joinGame).not.toHaveBeenCalled();
    expect(screen.queryByLabelText("Join link")).not.toBeInTheDocument();
  });
});
