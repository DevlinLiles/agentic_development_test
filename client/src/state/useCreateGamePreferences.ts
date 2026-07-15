import { useCallback, useState } from "react";
import type { GameOpponent, PlayerColor } from "../types/gameTypes";

/**
 * The user's choices on the game-creation entry point: who they want to play
 * against and which color they'd like to play. These are *preferences* the
 * user expresses up front and that we remember for the duration of the
 * browser session — they are distinct from the per-game session
 * (`useGameSession`), which records the color the player was actually
 * assigned once a game is created.
 *
 * The actual color a player ends up playing is decided by the backend at
 * create/join time and is returned in the create-game response; honoring the
 * preferred color server-side is a separate, downstream unit. Here we only
 * present the picker and keep the selection around so a user who navigates
 * away and comes back doesn't have to re-pick.
 */
export interface CreateGamePreferences {
  opponent: GameOpponent;
  color: PlayerColor;
}

export const DEFAULT_CREATE_GAME_PREFERENCES: CreateGamePreferences = {
  opponent: "Human",
  color: "White",
};

const STORAGE_KEY = "chess:createPrefs";

function isOpponent(value: unknown): value is GameOpponent {
  return value === "Human" || value === "Ai";
}

function isColor(value: unknown): value is PlayerColor {
  return value === "White" || value === "Black";
}

function readPreferences(): CreateGamePreferences {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return DEFAULT_CREATE_GAME_PREFERENCES;
    const parsed = JSON.parse(raw) as Partial<CreateGamePreferences>;
    // Be defensive: never trust a corrupted/partial payload from storage.
    if (isOpponent(parsed.opponent) && isColor(parsed.color)) {
      return { opponent: parsed.opponent, color: parsed.color };
    }
    return DEFAULT_CREATE_GAME_PREFERENCES;
  } catch {
    return DEFAULT_CREATE_GAME_PREFERENCES;
  }
}

function writePreferences(preferences: CreateGamePreferences): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(preferences));
  } catch {
    // sessionStorage unavailable (e.g. disabled, quota) — the selection just
    // won't survive a reload for this session; nothing else we can do here.
  }
}

/**
 * Reads the user's saved game-creation preferences from sessionStorage once on
 * mount, and writes through to sessionStorage on every change so the
 * selection persists for the lifetime of the browser session (the tab).
 */
export function useCreateGamePreferences() {
  const [preferences, setPreferencesState] =
    useState<CreateGamePreferences>(readPreferences);

  const setPreferences = useCallback((next: CreateGamePreferences): void => {
    setPreferencesState(next);
    writePreferences(next);
  }, []);

  return { preferences, setPreferences };
}
