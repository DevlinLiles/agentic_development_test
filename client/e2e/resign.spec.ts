import { test, expect } from "@playwright/test";
import { createGame, joinGame, playMove, readJoinPath, resign, statusPanel } from "./helpers";

// Drives two real, independent browser contexts through a resignation: White makes a move,
// then resigns. Confirms both tabs land on the resignation terminal state via SignalR and that
// the opponent is notified without a manual refresh.

test("a player can resign and the opponent is notified via SignalR", async ({ browser }) => {
  const whiteContext = await browser.newContext();
  const white = await whiteContext.newPage();

  await createGame(white);
  const joinPath = await readJoinPath(white);

  const blackContext = await browser.newContext();
  const black = await blackContext.newPage();
  await joinGame(black, joinPath);

  // Wait for SignalR to flip both tabs to Active before resigning.
  await expect(statusPanel(white)).toContainText("Your turn", { timeout: 10_000 });
  await expect(statusPanel(black)).toContainText("Opponent's turn");

  await playMove(white, "e2", "e4", 1);

  // White resigns. White is the resigning player, so Black wins.
  await resign(white);

  // The resigning player's own tab reflects the ended game immediately.
  await expect(statusPanel(white)).toContainText("Resignation — Black wins");

  // The opponent must be notified via the game hub without a manual refresh.
  await expect(statusPanel(black)).toContainText("Resignation — Black wins", { timeout: 10_000 });

  // Durability: a fresh page load must re-derive the resigned state from the server.
  await black.reload();
  await expect(statusPanel(black)).toContainText("Resignation — Black wins");

  await whiteContext.close();
  await blackContext.close();
});
