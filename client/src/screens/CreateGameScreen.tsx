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
  const [pendingMode, setPendingMode] = useState<GameMode | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleCreate = async (mode: GameMode) => {
    setPendingMode(mode);
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
      setPendingMode(null);
    }
  };

  return (
    <div className="create-game-screen">
      <h1>Online Chess</h1>
      <p>Start a new game and share the link with your opponent.</p>
      <div className="create-game-screen__options">
        <button
          type="button"
          onClick={() => handleCreate("TwoPlayer")}
          disabled={pendingMode !== null}
        >
          {pendingMode === "TwoPlayer" ? "Creating…" : "Create Game"}
        </button>
        <button
          type="button"
          onClick={() => handleCreate("VsAi")}
          disabled={pendingMode !== null}
        >
          {pendingMode === "VsAi" ? "Creating…" : "Play vs Computer"}
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
