import { test, expect } from "@playwright/test";
import {
  aiReplyLine,
  clickSquare,
  createComputerGame,
  moveHistoryEntries,
  playMove,
  statusPanel,
} from "./helpers";

// Drives a single browser context through the solo Computer-game flow against
// the live backend: choose "Play the computer", play White, and let the
// server-side AI answer every move.
//
// Two behaviors make this flow different from the two-player specs, and both
// are asserted here:
//   1. The game is Active the instant it is created (the AI occupies Black at
//      create time), so there is no waiting state and no join link to share.
//   2. Every submitted White ply is answered by exactly one Black ply inside
//      the same POST (the backend applies the reply synchronously, then
//      broadcasts one full-snapshot push covering both plies). So each human
//      move is worth TWO move-history entries, and the status panel returns to
//      "Your turn" with a "Computer played <san>" announcement above it.
//
// The AI's concrete SAN is heuristic and intentionally NOT pinned — the spec
// asserts that *a* reply happened and that the two independent render paths
// (StatusPanel, which renders from the pushed snapshot, and MoveHistoryPanel,
// which refetches the move list) agree about it, not what the engine chose.

test("solo game against the computer: the AI answers every move I make", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  const gameId = await createComputerGame(page);

  // No waiting state, and nothing to share: a computer game is Active at
  // creation, so the join-link panel (a two-player-only element) must be absent
  // and the status panel must already be the human's move.
  await expect(page.getByLabel("Join link")).toHaveCount(0);
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(statusPanel(page)).not.toContainText("Waiting for opponent");
  await expect(moveHistoryEntries(page)).toHaveCount(0);

  // While a submission is in flight the board is paused with a visible busy
  // status naming the computer (UX verification finding A-5) — that window
  // includes the backend computing its reply.
  const busy = page.locator(".chess-board__busy");

  // --- First exchange -------------------------------------------------------
  // The busy line appears while the ply + AI reply round-trip is in flight and
  // clears once the reply lands. It is transient, so each half is asserted with
  // a poll rather than both at once.
  await clickSquare(page, "e2");
  await clickSquare(page, "e4");
  await expect(busy).toBeVisible();
  await expect(busy).toContainText("Computer is thinking");

  // Move-history-count polling: one White ply + one AI reply = 2 entries.
  await expect(moveHistoryEntries(page)).toHaveCount(2);
  await expect(busy).toHaveCount(0);

  // The status panel is a live region announcing the computer's reply above
  // the turn line, then handing the move back to the human. Note there is no
  // "Computer's turn" state to wait for: the backend applies the reply inside
  // the same request, so by the time the client sees state again it is already
  // the human's move.
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(aiReplyLine(page)).toBeVisible();
  // SAN shape only (files/ranks/captures/promotions/check/mate and the two
  // castling spellings) — never which move the heuristic engine picked.
  await expect(aiReplyLine(page)).toHaveText(/^Computer played [a-hO][\w-]*[+#]?$/);

  // ... and the two render paths agree: the announced SAN is the one that was
  // actually recorded in the move history (the AI's entry is the second one).
  const firstReplySan = (await aiReplyLine(page).textContent())!.replace("Computer played ", "");
  await expect(moveHistoryEntries(page).nth(1)).toContainText("Black");
  await expect(moveHistoryEntries(page).nth(1)).toContainText(firstReplySan);

  // --- Second exchange ------------------------------------------------------
  // The same exchange again, from the new position: the entry count must keep
  // stepping by exactly two per human move.
  await playMove(page, "d2", "d4", 4);
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(aiReplyLine(page)).toBeVisible();
  const secondReplySan = (await aiReplyLine(page).textContent())!.replace("Computer played ", "");
  await expect(moveHistoryEntries(page).nth(3)).toContainText("Black");
  await expect(moveHistoryEntries(page).nth(3)).toContainText(secondReplySan);

  // --- The AI reply survives a reload ---------------------------------------
  // Durability: a fresh load re-derives state from the server, not from the
  // SignalR connection's in-memory state, so the latest AI reply is re-read
  // from the same game row. The session (playerToken) survives in localStorage.
  await page.reload();
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(aiReplyLine(page)).toBeVisible();
  await expect(aiReplyLine(page)).toContainText(secondReplySan);
  await expect(moveHistoryEntries(page)).toHaveCount(4);
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  // --- A second browser gets no seat ----------------------------------------
  // The AI owns Black from the moment of creation, so the second seat is never
  // joinable and there is no spectator mode: a browser with no saved session
  // for this game is told it is not a participant.
  const outsiderContext = await browser.newContext();
  const outsider = await outsiderContext.newPage();
  await outsider.goto(`/game/${gameId}`);
  await expect(outsider.getByText("You're not a participant in this game.")).toBeVisible();
  await expect(outsider.locator(".move-history-panel")).toHaveCount(0);
  await outsiderContext.close();

  // --- The human can still keep playing -------------------------------------
  // After all that, the same tab is still mid-game and interactive: another
  // exchange lands and the AI answers again.
  await playMove(page, "b1", "c3", 6);
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(aiReplyLine(page)).toBeVisible();
  await expect(moveHistoryEntries(page)).toHaveCount(6);

  await context.close();
});
