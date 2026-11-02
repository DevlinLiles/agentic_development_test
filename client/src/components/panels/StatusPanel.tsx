// Renders purely from a GameStateResponse. No client-side rules evaluation
// happens here — every displayed fact comes straight from the response
// fields (status, turn, yourColor, isCheck, result, resultReason,
// opponentType, moveCount).
//
// Accessibility notes:
// - The panel itself is a `role="status"` region, so every message it renders
//   (turn changes, "Check!", the computer's reply, terminal results) is
//   announced politely by screen readers without stealing focus (WCAG 4.1.3).
// - In computer games the reply line appears in the same live region, above
//   the turn line, so a listener hears what the computer did before hearing
//   that it's their move again. It only renders when the last move on the
//   board was the computer's — in a computer game that is exactly when it's
//   the human's turn.
// - When the computer's reply ends the game — by checkmate OR by a draw —
//   that reply is still announced (above the terminal banner) rather than
//   being swallowed by it. See `computerDeliveredLastMove` below (UX
//   verification finding V-1).
// - The opponent is always named in text (never icon/color alone).

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

/**
 * Whether the computer's own move ended the game, and so deserves to be
 * announced before the terminal banner.
 *
 * The wire's `lastMove` carries no color, so this is inferred from the
 * outcome plus move parity:
 * - A Black win can only have been delivered by the computer's final move —
 *   the human always plays White in a computer game.
 * - A draw can be delivered by either side, but in a computer game the human
 *   is always White and plies alternate starting with White, so an even
 *   `moveCount` means the last ply was Black's — i.e. the computer's. An odd
 *   count means the human's own move drew the game and there was no reply,
 *   so nothing is announced (UX verification finding V-1).
 */
function computerDeliveredLastMove(gameState: GameStateResponse): boolean {
  if (gameState.opponentType !== "Computer" || gameState.lastMove === null) {
    return false;
  }
  if (gameState.result === "BlackWins") return true;
  return gameState.result === "Draw" && gameState.moveCount % 2 === 0;
}

export function StatusPanel({ gameState }: StatusPanelProps) {
  const { status, turn, yourColor, isCheck } = gameState;

  if (status === "Ended") {
    const message = terminalMessage(gameState);
    const computerReply = computerDeliveredLastMove(gameState)
      ? `Computer played ${gameState.lastMove!.san}`
      : null;
    return (
      <div className="status-panel status-panel--ended" role="status">
        {computerReply && <p className="status-panel__ai-reply">{computerReply}</p>}
        <p className="status-panel__banner">{message ?? "Game over"}</p>
        {gameState.opponentType === "Computer" && (
          <p className="status-panel__opponent">You played the computer</p>
        )}
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
  const isComputerGame = gameState.opponentType === "Computer";
  const turnMessage = turn === yourColor ? "Your turn" : isComputerGame ? "Computer's turn" : "Opponent's turn";

  // In a computer game the previous mover was the computer exactly when it's
  // the human's turn now; announce that reply in the live region above the
  // turn line.
  const aiReply =
    isComputerGame && gameState.lastMove && turn === yourColor
      ? `Computer played ${gameState.lastMove.san}`
      : null;

  return (
    <div className="status-panel status-panel--active" role="status">
      {aiReply && <p className="status-panel__ai-reply">{aiReply}</p>}
      <p className="status-panel__turn">{turnMessage}</p>
      {isCheck && <p className="status-panel__check">Check!</p>}
    </div>
  );
}
