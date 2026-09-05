import { test, expect } from "@playwright/test";
import { createVsAiGame, playMove, statusPanel, moveHistoryEntries } from "./helpers";

// Drives a single browser context through a game against the server's
// heuristic AI player. VsAi games skip the waiting-for-opponent phase: the
// backend creates the game Active with a synthetic Black seat, so the
// creator's tab lands on a playable board with no join link to share, and
// every human move is answered in the same server request by an automatic
// AI reply.

test("vs-ai game starts Active and hides the join link", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);

  // No second human seat — the board is immediately playable (Active, White
  // to move) and the shareable join link panel must not be rendered (AC-6).
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  await context.close();
});

test("the AI replies automatically after the human's move", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);
  await expect(statusPanel(page)).toContainText("Your turn");

  // A single human move is answered in the same server request by the AI's
  // reply, so the move history jumps straight to two plies and control
  // returns to the human (White) without any second browser tab.
  await playMove(page, "e2", "e4", 2);

  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(moveHistoryEntries(page)).toHaveCount(2);
  // The first ply is the human's White move; the second is the AI's Black reply.
  await expect(moveHistoryEntries(page).first()).toContainText("e4");

  await context.close();
});

test("vs-ai state survives a reload", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);
  await playMove(page, "e2", "e4", 2);
  await playMove(page, "g1", "f3", 4);

  // The saved session (localStorage) lets a fresh load re-derive the
  // in-progress Active state from the server rather than treating the
  // viewer as a non-participant.
  await page.reload();
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(moveHistoryEntries(page)).toHaveCount(4);

  await context.close();
});
