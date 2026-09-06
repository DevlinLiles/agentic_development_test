import { useState } from "react";
import type { GameStateResponse } from "../../types/gameTypes";
import "./joinLinkPanel.css";

export interface JoinLinkPanelProps {
  gameState: GameStateResponse;
}

export function JoinLinkPanel({ gameState }: JoinLinkPanelProps) {
  const [copied, setCopied] = useState(false);

  // The join link exists to invite a human opponent into an open seat. A VsAi
  // game has no open seat — the AI is the opponent and the game starts Active
  // immediately — so the panel must never render for AI games (AC-6). It is
  // also only relevant while the creator (White) is still waiting for a human
  // to claim Black; once active or for the joining player it stays hidden.
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
