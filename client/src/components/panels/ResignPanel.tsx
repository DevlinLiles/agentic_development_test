// The resign control. Surfaces a "Resign" button to a seated participant of an
// active game; clicking it confirms, then posts to the resign endpoint. The
// server is the sole authority on game state, so the resulting GameStateResponse
// (and the SignalR push to the opponent) is what flips the UI to ended.

import { useState } from "react";
import * as gamesApi from "../../api/gamesApi";
import { describeApiError } from "../../utils/describeApiError";
import type { GameStateResponse } from "../../types/gameTypes";
import "./resignPanel.css";

export interface ResignPanelProps {
  gameId: string;
  playerToken: string;
  gameState: GameStateResponse;
  onStateUpdate: (state: GameStateResponse) => void;
}

export function ResignPanel({ gameId, playerToken, gameState, onStateUpdate }: ResignPanelProps) {
  const [isConfirming, setIsConfirming] = useState(false);
  const [isResigning, setIsResigning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Only a seated participant of an in-progress game may resign.
  if (gameState.status !== "Active" || gameState.yourColor === null) {
    return null;
  }

  async function handleResign() {
    setError(null);
    setIsResigning(true);
    try {
      const newState = await gamesApi.resignGame(gameId, playerToken);
      onStateUpdate(newState);
      setIsConfirming(false);
    } catch (err) {
      setError(describeApiError(err));
    } finally {
      setIsResigning(false);
    }
  }

  return (
    <div className="resign-panel">
      {!isConfirming ? (
        <button
          type="button"
          className="resign-panel__button"
          onClick={() => {
            setError(null);
            setIsConfirming(true);
          }}
          disabled={isResigning}
        >
          Resign
        </button>
      ) : (
        <div className="resign-panel__confirm">
          <p>Are you sure you want to resign?</p>
          <div className="resign-panel__confirm-actions">
            <button
              type="button"
              className="resign-panel__button resign-panel__button--danger"
              onClick={handleResign}
              disabled={isResigning}
            >
              {isResigning ? "Resigning…" : "Confirm Resign"}
            </button>
            <button
              type="button"
              className="resign-panel__button resign-panel__button--cancel"
              onClick={() => setIsConfirming(false)}
              disabled={isResigning}
            >
              Cancel
            </button>
          </div>
        </div>
      )}
      {error && <p className="resign-panel__error" role="alert">{error}</p>}
    </div>
  );
}
