import { useState } from "react";
import { useNavigate } from "react-router-dom";
import * as gamesApi from "../api/gamesApi";
import type { GameMode } from "../types/gameTypes";
import { useGameSession } from "../state/useGameSession";
import { describeApiError } from "../utils/describeApiError";
import "./createGameScreen.css";

export function CreateGameScreen() {
  const navigate = useNavigate();
  const { saveSession } = useGameSession();
  // Tracks the label of the creation option currently in flight so the
  // button can show a "Creating…" state while disabling both options.
  const [creatingMode, setCreatingMode] = useState<GameMode | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleCreate = async (mode: GameMode) => {
    setCreatingMode(mode);
    setError(null);
    try {
      const response = await gamesApi.createGame(mode);
      saveSession({
        gameId: response.gameId,
        playerToken: response.playerToken,
        color: response.color,
      });
      navigate(`/game/${response.gameId}`);
    } catch (err) {
      setError(describeApiError(err));
      setCreatingMode(null);
    }
  };

  const isBusy = creatingMode !== null;

  return (
    <div className="create-game-screen">
      <h1>Online Chess</h1>
      <p>Choose how you'd like to start a new game.</p>
      <div className="create-game-screen__options">
        <button
          type="button"
          className="create-game-screen__option"
          onClick={() => handleCreate("TwoPlayer")}
          disabled={isBusy}
        >
          <span className="create-game-screen__option-title">
            {creatingMode === "TwoPlayer" ? "Creating…" : "Create Game"}
          </span>
          <span className="create-game-screen__option-subtitle">
            Play a friend — share the join link once the game is created.
          </span>
        </button>
        <button
          type="button"
          className="create-game-screen__option"
          onClick={() => handleCreate("VsAi")}
          disabled={isBusy}
        >
          <span className="create-game-screen__option-title">
            {creatingMode === "VsAi" ? "Creating…" : "Play vs Computer"}
          </span>
          <span className="create-game-screen__option-subtitle">
            Face the AI right away — no second player needed.
          </span>
        </button>
      </div>
      {error && (
        <p className="create-game-screen__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
