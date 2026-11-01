import type { Page } from "@playwright/test";
import { expect } from "@playwright/test";

/** Creates a new game from the landing screen and returns its id (parsed from the URL). */
export async function createGame(page: Page): Promise<string> {
  await page.goto("/");
  await page.getByRole("button", { name: "Create Game" }).click();
  await page.waitForURL(/\/game\/[0-9a-f-]{36}/);
  const match = page.url().match(/\/game\/([0-9a-f-]{36})/);
  if (!match) throw new Error(`Could not parse game id from URL: ${page.url()}`);
  return match[1];
}

/**
 * Creates a new solo game against the server-side computer and returns its id
 * (parsed from the URL).
 *
 * `createGame` above covers the default (Human) flow, where the creator is
 * left waiting for a second player. This variant first selects the
 * "Play the computer" radio on the create screen — the only client surface
 * difference between the two flows — and is otherwise byte-for-byte the same
 * create-and-navigate sequence.
 *
 * The radio's selection is asserted (not assumed) so a broken label/id wiring
 * on the create screen fails loudly here, instead of surfacing later as a
 * confusing "the computer never replied" timeout in the game spec.
 */
export async function createComputerGame(page: Page): Promise<string> {
  await page.goto("/");
  const computerOption = page.getByRole("radio", { name: "Play the computer" });
  await computerOption.click();
  await expect(computerOption).toBeChecked();
  await page.getByRole("button", { name: "Create Game" }).click();
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

/**
 * The status panel's "Computer played &lt;san&gt;" announcement line. It only
 * renders in a computer game, and only when the last move on the board was the
 * computer's — i.e. exactly when it is the human's turn again — so its
 * appearance is the client-side proof that a reply landed.
 */
export function aiReplyLine(page: Page) {
  return page.locator(".status-panel__ai-reply");
}
