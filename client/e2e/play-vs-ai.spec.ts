import { test, expect } from "@playwright/test";
import {
  createVsAiGame,
  playMove,
  statusPanel,
  moveHistoryEntries,
} from "./helpers";

// Drives a single browser context through the vs-computer (VsAi) flow against
// the live backend. Unlike two-player games, the AI is seated at creation time,
// so the game starts Active with no WaitingForPlayer2 phase, no shareable join
// link, and no second human to coordinate with. The backend applies the AI's
// reply inline within the same SubmitMove response as the human move, so each
// human action lands two plies in the move history at once.

test("vs-AI game starts active with no join link and the computer replies inline", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);

  // AC-6: VsAi games have no second human to invite, so the join-link panel
  // must never be shown — not even briefly during load.
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  // The AI is seated at creation, so the game skips WaitingForPlayer2 and is
  // Active immediately — it is White's (the human's) turn from the first paint.
  await expect(statusPanel(page)).toContainText("Your turn");

  // White moves e2-e4. The backend applies the AI's reply inline, so two plies
  // (the human move + the computer reply) land in the history at once and
  // control returns to White.
  await playMove(page, "e2", "e4", 2);
  await expect(statusPanel(page)).toContainText("Your turn");

  const entries = moveHistoryEntries(page);
  await expect(entries).toHaveCount(2);
  // The first ply is the human's move; the second is the computer's reply.
  await expect(entries.first()).toContainText("e4");
  await expect(entries.nth(1)).not.toBeEmpty();

  // No join link appears at any point during the AI exchange either.
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  await context.close();
});

test("vs-AI game state survives a reload and stays active", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);
  await expect(statusPanel(page)).toContainText("Your turn");
  await playMove(page, "e2", "e4", 2);

  // Durability: a fresh page load must re-derive the VsAi state from the
  // server (Active, the human's turn, both plies persisted) rather than rely
  // on in-memory SignalR state.
  await page.reload();
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(moveHistoryEntries(page)).toHaveCount(2);
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  await context.close();
});

test("an outsider cannot join a vs-AI game and is told it's not a participant", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();
  const gameId = await createVsAiGame(page);
  await expect(statusPanel(page)).toContainText("Your turn");

  // A second, unrelated browser (no localStorage session) opens the same VsAi
  // game URL. There is no open seat to claim and no spectator mode, so — per
  // spec — it gets the not-a-participant message rather than a join prompt or
  // a read-only board. This also exercises the GameScreen VsAi no-session path.
  const outsiderContext = await browser.newContext();
  const outsider = await outsiderContext.newPage();
  await outsider.goto(`/game/${gameId}`);

  await expect(outsider.getByText("You're not a participant in this game.")).toBeVisible();
  await expect(outsider.getByLabel("Join link")).toHaveCount(0);

  await context.close();
  await outsiderContext.close();
});
