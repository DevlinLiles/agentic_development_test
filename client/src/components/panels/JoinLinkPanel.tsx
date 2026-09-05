import { useState } from "react";
import type { GameStateResponse } from "../../types/gameTypes";
import "./joinLinkPanel.css";

export interface JoinLinkPanelProps {
  gameState: GameStateResponse;
}

export function JoinLinkPanel({ gameState }: JoinLinkPanelProps) {
  const [copied, setCopied] = useState(false);

  // The shareable join link only makes sense for a TwoPlayer game that is
  // still waiting for a second human to claim the Black seat, and only for
  // the waiting White player who created it. VsAi games have no second
  // human seat (the server fills Black with a synthetic token and starts
  // the game Active), so the panel is always hidden for them (AC-6) —
  // independent of status, so a future status change can't accidentally
  // surface a meaningless link.
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
