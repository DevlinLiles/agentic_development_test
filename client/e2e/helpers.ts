import type { Page } from "@playwright/test";
import { expect } from "@playwright/test";

/** Creates a new two-player game from the landing screen and returns its id (parsed from the URL). */
export async function createGame(page: Page): Promise<string> {
  await page.goto("/");
  await page.getByRole("button", { name: "Create Game" }).click();
  await page.waitForURL(/\/game\/[0-9a-f-]{36}/);
  const match = page.url().match(/\/game\/([0-9a-f-]{36})/);
  if (!match) throw new Error(`Could not parse game id from URL: ${page.url()}`);
  return match[1];
}

/** Creates a new vs-computer (AI) game from the landing screen and returns its id (parsed from the URL). */
export async function createVsAiGame(page: Page): Promise<string> {
  await page.goto("/");
  await page.getByRole("button", { name: "Play vs Computer", exact: true }).click();
  await page.waitForURL(/\/game\/[0-9a-f-]{36}/);
  const match = page.url().match(/\/game\/([0-9a-f-]{36})/);
  if (!match) throw new Error(`Could not parse game id from URL: ${page.url()}`);
  return match[1];
}

/** Reads the shareable join link out of the JoinLinkPanel (only visible to the waiting White player). */
export async function readJoinPath(page: Page): Promise<string> {
  const input = page.getByLabel("Join link");
  await expect(input).toBeVisible();
  const url = await input.inputValue();
  return new URL(url).pathname;
}

/** Navigates a (typically separate) browser context to the join link, claiming the second seat. */
export async function joinGame(page: Page, joinPath: string): Promise<void> {
  await page.goto(joinPath);
}

/** Clicks a square by its algebraic name (e.g. "e4") regardless of board orientation. */
export async function clickSquare(page: Page, square: string): Promise<void> {
  await page.getByRole("button", { name: square, exact: true }).click();
}

/** Plays one ply by clicking the origin then destination square and waits for the board to update. */
export async function playMove(page: Page, from: string, to: string, expectedMoveCount: number): Promise<void> {
  await clickSquare(page, from);
  await clickSquare(page, to);
  await expect(page.locator(".move-history-panel__list li")).toHaveCount(expectedMoveCount);
}

export function statusPanel(page: Page) {
  return page.locator(".status-panel");
}

export function moveHistoryEntries(page: Page) {
  return page.locator(".move-history-panel__list li");
}
