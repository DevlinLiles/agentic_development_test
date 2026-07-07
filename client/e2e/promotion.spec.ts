import { test, expect } from "@playwright/test";
import { createGame, joinGame, playMove, readJoinPath, clickSquare, moveHistoryEntries } from "./helpers";

// A short, legal race-the-pawns line that reaches a real capture-promotion on move 5,
// exercising the PromotionPicker UI end to end against the live backend:
//   1. a4 h5  2. a5 h4  3. a6 h3  4. axb7 hxg2  5. bxa8=Q
test("promoting a pawn via the picker produces the right SAN and piece", async ({ browser }) => {
  const whiteContext = await browser.newContext();
  const white = await whiteContext.newPage();
  await createGame(white);
  const joinPath = await readJoinPath(white);

  const blackContext = await browser.newContext();
  const black = await blackContext.newPage();
  await joinGame(black, joinPath);
  await expect(white.locator(".status-panel")).toContainText("Your turn", { timeout: 10_000 });

  await playMove(white, "a2", "a4", 1);
  await playMove(black, "h7", "h5", 2);
  await playMove(white, "a4", "a5", 3);
  await playMove(black, "h5", "h4", 4);
  await playMove(white, "a5", "a6", 5);
  await playMove(black, "h4", "h3", 6);
  await playMove(white, "a6", "b7", 7);
  await playMove(black, "h3", "g2", 8);

  // The final move is a promoting capture: clicking the destination opens the picker
  // instead of submitting immediately.
  await clickSquare(white, "b7");
  await clickSquare(white, "a8");

  const dialog = white.getByRole("dialog", { name: "Choose promotion piece" });
  await expect(dialog).toBeVisible();
  await white.getByRole("button", { name: "Promote to Queen" }).click();

  await expect(moveHistoryEntries(white)).toHaveCount(9);
  await expect(moveHistoryEntries(white).last()).toContainText("=Q");

  // The promoted piece should render as a white queen on a8.
  const a8 = white.getByRole("button", { name: "a8", exact: true });
  await expect(a8.locator("[aria-label='White Queen']")).toBeVisible();

  await whiteContext.close();
  await blackContext.close();
});
