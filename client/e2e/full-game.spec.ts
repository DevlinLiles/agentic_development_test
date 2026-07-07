import { test, expect } from "@playwright/test";
import { createGame, joinGame, playMove, readJoinPath, statusPanel, moveHistoryEntries } from "./helpers";

// Drives two real, independent browser contexts (matching two separate browser
// profiles per the product spec) through a full game against the live backend.

test("two players play Scholar's Mate to checkmate", async ({ browser }) => {
  const whiteContext = await browser.newContext();
  const white = await whiteContext.newPage();

  await createGame(white);
  await expect(statusPanel(white)).toContainText("Waiting for opponent");

  const joinPath = await readJoinPath(white);

  const blackContext = await browser.newContext();
  const black = await blackContext.newPage();
  await joinGame(black, joinPath);

  // Player 1's already-open tab must flip to Active via SignalR without a manual refresh.
  await expect(statusPanel(white)).toContainText("Your turn", { timeout: 10_000 });
  await expect(statusPanel(black)).toContainText("Opponent's turn");

  await playMove(white, "e2", "e4", 1);
  await expect(statusPanel(black)).toContainText("Your turn");

  await playMove(black, "e7", "e5", 2);
  await playMove(white, "f1", "c4", 3);
  await playMove(black, "b8", "c6", 4);
  await playMove(white, "d1", "h5", 5);
  await playMove(black, "g8", "f6", 6);

  // Qxf7# — checkmate. Confirm both tabs land on the same terminal state via SignalR.
  await playMove(white, "h5", "f7", 7);
  await expect(statusPanel(white)).toContainText("Checkmate — White wins");
  await expect(statusPanel(black)).toContainText("Checkmate — White wins", { timeout: 10_000 });

  await expect(moveHistoryEntries(white)).toHaveCount(7);
  await expect(moveHistoryEntries(white).last()).toContainText("Qxf7#");
  await expect(moveHistoryEntries(black).last()).toContainText("Qxf7#");

  // Board is locked once the game has ended — clicking a square is a no-op, not an error.
  await white.getByRole("button", { name: "a2", exact: true }).click();
  await white.getByRole("button", { name: "a3", exact: true }).click();
  await expect(moveHistoryEntries(white)).toHaveCount(7);

  // Durability: a fresh page load must re-derive the ended state from the server, not
  // rely on anything held only in the SignalR connection's in-memory state.
  await white.reload();
  await expect(statusPanel(white)).toContainText("Checkmate — White wins");
  await expect(moveHistoryEntries(white)).toHaveCount(7);

  await whiteContext.close();
  await blackContext.close();
});
