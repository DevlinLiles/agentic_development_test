// Renders purely from a GameStateResponse. No client-side rules evaluation
// happens here — every displayed fact comes straight from the response
// fields (status, turn, yourColor, isCheck, result, resultReason).

import type { GameStateResponse } from "../../types/gameTypes";
import "./statusPanel.css";

export interface StatusPanelProps {
  gameState: GameStateResponse;
}

function terminalMessage(gameState: GameStateResponse): string | null {
  const { result, resultReason } = gameState;
  if (!result) return null;

  const winner = result === "WhiteWins" ? "White" : result === "BlackWins" ? "Black" : null;

  switch (resultReason) {
    case "Checkmate":
      return `Checkmate — ${winner} wins`;
    case "Resignation":
      return `Resignation — ${winner} wins`;
    case "Stalemate":
      return "Stalemate — Draw";
    case "FiftyMoveRule":
      return "Draw — Fifty-move rule";
    default:
      if (result === "Draw") return "Draw";
      if (winner) return `${winner} wins`;
      return null;
  }
}

export function StatusPanel({ gameState }: StatusPanelProps) {
  const { status, turn, yourColor, isCheck } = gameState;

  if (status === "Ended") {
    const message = terminalMessage(gameState);
    return (
      <div className="status-panel status-panel--ended" role="status">
        <p className="status-panel__banner">{message ?? "Game over"}</p>
      </div>
    );
  }

  if (status === "WaitingForPlayer2") {
    return (
      <div className="status-panel status-panel--waiting" role="status">
        <p>Waiting for opponent</p>
      </div>
    );
  }

  // status === "Active"
  const turnMessage = turn === yourColor ? "Your turn" : "Opponent's turn";

  return (
    <div className="status-panel status-panel--active" role="status">
      <p className="status-panel__turn">{turnMessage}</p>
      {isCheck && <p className="status-panel__check">Check!</p>}
    </div>
  );
}
