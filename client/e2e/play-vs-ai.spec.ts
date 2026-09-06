import { test, expect, type Page } from "@playwright/test";
import { playMove, statusPanel, moveHistoryEntries } from "./helpers";

// E2E scenarios for the VsAi (Play vs Computer) flow against the live backend.
//
// Unlike two-player games, a VsAi game fills the Black seat with the AI at
// creation time and goes straight to Active — there is no WaitingForPlayer2
// phase, no shareable join link, and no second browser to claim the seat. The
// AI replies inline within the same SubmitMove HTTP request, so every human
// move lands as two plies (the human's move plus the AI's reply) in a single
// atomic server response.

/** Creates a new VsAi game from the landing screen and returns its id (parsed from the URL). */
async function createVsAiGame(page: Page): Promise<string> {
  await page.goto("/");
  await page.getByRole("button", { name: "Play vs Computer" }).click();
  await page.waitForURL(/\/game\/[0-9a-f-]{36}/);
  const match = page.url().match(/\/game\/([0-9a-f-]{36})/);
  if (!match) throw new Error(`Could not parse game id from URL: ${page.url()}`);
  return match[1];
}

test("a VsAi game starts Active immediately and never shows a join link", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);

  // VsAi games skip WaitingForPlayer2 entirely: the human (White) is to move
  // right away, so the status panel shows "Your turn" rather than "Waiting for
  // opponent".
  await expect(statusPanel(page)).toContainText("Your turn");

  // AC-6: there is no second player to join a VsAi game, so the shareable
  // join-link panel must be hidden for the creator (and everyone else).
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  await context.close();
});

test("the human's move is answered inline by the AI, landing as two plies in one response", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await createVsAiGame(page);
  await expect(statusPanel(page)).toContainText("Your turn");

  // No join link should ever appear during the AI game.
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  // The AI reply is generated server-side within the same SubmitMove call, so
  // after the human plays e2-e4 the move history jumps straight to two
  // entries (White's e4 + Black's AI reply) — there is never a transient
  // single-move state for the client to render.
  await playMove(page, "e2", "e4", 2);

  // Both plies are recorded: the first is the human's (White), the second is
  // the AI's (Black).
  await expect(moveHistoryEntries(page)).toHaveCount(2);
  await expect(moveHistoryEntries(page).first()).toContainText("White");
  await expect(moveHistoryEntries(page).nth(1)).toContainText("Black");

  // After the AI replies it is White's turn again, so the human is prompted to
  // move (the game is still Active, not ended).
  await expect(statusPanel(page)).toContainText("Your turn");

  // The join link remains hidden throughout the AI game (AC-6).
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  // Durability: a fresh page load re-derives the in-progress state from the
  // server using the persisted session — the active game, both plies, and the
  // still-hidden join link all survive a reload.
  await page.reload();
  await expect(statusPanel(page)).toContainText("Your turn");
  await expect(moveHistoryEntries(page)).toHaveCount(2);
  await expect(page.getByLabel("Join link")).toHaveCount(0);

  await context.close();
});

test("a browser with no session for a VsAi game is told it's not a participant", async ({ browser }) => {
  // The creator establishes and persists a session; a second, unrelated browser
  // (no localStorage) opening the same VsAi game URL has no session and, per the
  // no-spectator spec, must be told it isn't a participant rather than getting a
  // read-only board. VsAi games skip the join path, so this second browser can
  // never claim a seat either.
  const creatorContext = await browser.newContext();
  const creator = await creatorContext.newPage();
  const gameId = await createVsAiGame(creator);
  await expect(statusPanel(creator)).toContainText("Your turn");

  const outsiderContext = await browser.newContext();
  const outsider = await outsiderContext.newPage();
  await outsider.goto(`/game/${gameId}`);

  await expect(outsider.getByText("You're not a participant in this game.")).toBeVisible();
  // And critically, the outsider never sees a join link to share.
  await expect(outsider.getByLabel("Join link")).toHaveCount(0);

  await creatorContext.close();
  await outsiderContext.close();
});
