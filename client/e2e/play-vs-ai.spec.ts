import { test, expect } from "@playwright/test";
import { createVsAiGame, playMove, statusPanel, moveHistoryEntries } from "./helpers";

// Drives a single browser context through a game against the AI opponent
// on the live backend. Unlike the two-player suites, there is no second
// human seat to claim — the AI is the opponent and the game starts Active.

test("a vs-AI game starts immediately in Active state with no join link to share", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);

  // AC-6: there is no human opponent to invite, so the join-link panel must
  // never render for a VsAi game.
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  // The AI is ready immediately — the game skips WaitingForPlayer2 and
  // starts Active with the human (White) to move.
  await expect(statusPanel(page)).toContainText("Your turn");

  await context.close();
});

test("the AI opponent replies to the player's move", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);
  await expect(statusPanel(page)).toContainText("Your turn");

  // White opens; the move list gains one entry and control passes to the AI.
  await playMove(page, "e2", "e4", 1);
  await expect(statusPanel(page)).toContainText("Opponent's turn");

  // The AI (Black) replies on its own via the backend; the move list grows
  // to two and control returns to the human player.
  await expect(moveHistoryEntries(page)).toHaveCount(2, { timeout: 15_000 });
  await expect(statusPanel(page)).toContainText("Your turn");

  await context.close();
});
