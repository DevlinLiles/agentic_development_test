import { test, expect } from "@playwright/test";
import { createGame, joinGame, readJoinPath } from "./helpers";

test("join link is only shown to the waiting creator, and disappears once active", async ({ browser }) => {
  const whiteContext = await browser.newContext();
  const white = await whiteContext.newPage();
  await createGame(white);

  const joinLinkInput = white.getByLabel("Join link");
  await expect(joinLinkInput).toBeVisible();
  const joinPath = await readJoinPath(white);

  const blackContext = await browser.newContext();
  const black = await blackContext.newPage();
  await joinGame(black, joinPath);

  // Black never sees a join-link panel (there's nothing left to share).
  await expect(black.getByLabel("Join link")).toHaveCount(0);

  // Once active, White's own join-link panel disappears too via the SignalR push.
  await expect(joinLinkInput).toHaveCount(0, { timeout: 10_000 });

  await whiteContext.close();
  await blackContext.close();
});

test("a browser with no session for an in-progress game is told it's not a participant", async ({ browser }) => {
  const whiteContext = await browser.newContext();
  const white = await whiteContext.newPage();
  await createGame(white);
  const joinPath = await readJoinPath(white);

  const blackContext = await browser.newContext();
  const black = await blackContext.newPage();
  await joinGame(black, joinPath);
  await expect(white.getByLabel("Join link")).toHaveCount(0, { timeout: 10_000 });

  // A third, unrelated browser (no localStorage session) opens the same link after both
  // seats are filled — per spec, there is no spectator mode.
  const outsiderContext = await browser.newContext();
  const outsider = await outsiderContext.newPage();
  await outsider.goto(joinPath);

  await expect(outsider.getByText("You're not a participant in this game.")).toBeVisible();

  await whiteContext.close();
  await blackContext.close();
  await outsiderContext.close();
});
