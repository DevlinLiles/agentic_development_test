import { useCallback } from "react";
import type { PlayerColor } from "../types/gameTypes";

export interface StoredGameSession {
  gameId: string;
  playerToken: string;
  color: PlayerColor;
}

const STORAGE_PREFIX = "chess:session:";

// NOTE: sessions are persisted in localStorage, which is shared across every
// tab/window for the same browser profile + origin. Opening two tabs for two
// "different" players on this machine will collide (the second tab's session
// overwrites the first for a given gameId). That's expected and acceptable
// per the product spec: no spectators, one active session per browser
// profile. Play the second seat from a different browser/profile/device.

/**
 * Hook for reading/writing the current player's session (gameId, token,
 * color) for a given game, backed by localStorage.
 */
export function useGameSession() {
  const getSession = useCallback((gameId: string): StoredGameSession | null => {
    try {
      const raw = localStorage.getItem(STORAGE_PREFIX + gameId);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as StoredGameSession;
      if (!parsed.gameId || !parsed.playerToken || !parsed.color) return null;
      return parsed;
    } catch {
      return null;
    }
  }, []);

  const saveSession = useCallback((session: StoredGameSession): void => {
    try {
      localStorage.setItem(
        STORAGE_PREFIX + session.gameId,
        JSON.stringify(session),
      );
    } catch {
      // localStorage unavailable (e.g. private browsing quota) — session
      // just won't persist across reloads; nothing else we can do here.
    }
  }, []);

  return { getSession, saveSession };
}
