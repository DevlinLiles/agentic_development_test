import { useState } from "react";
import type { GameStateResponse } from "../../types/gameTypes";
import "./joinLinkPanel.css";

export interface JoinLinkPanelProps {
  gameState: GameStateResponse;
}

export function JoinLinkPanel({ gameState }: JoinLinkPanelProps) {
  const [copied, setCopied] = useState(false);

  // The shareable join link only exists for two-player games while the creator
  // (White) waits for a human opponent to claim the Black seat. VsAi games fill
  // the Black seat with the AI at creation time and go straight to Active, so
  // there is never a second player to join and never a link to share. Hide the
  // panel for VsAi games regardless of status (AC-6) — and, as before, hide it
  // for everyone except the waiting White player in a two-player game.
  if (
    gameState.mode === "VsAi" ||
    gameState.status !== "WaitingForPlayer2" ||
    gameState.yourColor !== "White"
  ) {
    return null;
  }

  const joinUrl = `${window.location.origin}/game/${gameState.gameId}`;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(joinUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard API unavailable/denied — the link is still selectable text.
    }
  };

  return (
    <div className="join-link-panel">
      <p>Share this link with your opponent:</p>
      <div className="join-link-panel__row">
        <input type="text" readOnly value={joinUrl} aria-label="Join link" onFocus={(e) => e.target.select()} />
        <button type="button" onClick={handleCopy}>
          {copied ? "Copied!" : "Copy"}
        </button>
      </div>
    </div>
  );
}
