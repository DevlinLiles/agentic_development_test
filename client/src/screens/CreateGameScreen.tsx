import { useState } from "react";
import { useNavigate } from "react-router-dom";
import * as gamesApi from "../api/gamesApi";
import { useGameSession } from "../state/useGameSession";
import { describeApiError } from "../utils/describeApiError";
import "./createGameScreen.css";

export function CreateGameScreen() {
  const navigate = useNavigate();
  const { saveSession } = useGameSession();
  const [isCreating, setIsCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleCreate = async () => {
    setIsCreating(true);
    setError(null);
    try {
      const response = await gamesApi.createGame();
      saveSession({
        gameId: response.gameId,
        playerToken: response.playerToken,
        color: response.color,
      });
      navigate(`/game/${response.gameId}`);
    } catch (err) {
      setError(describeApiError(err));
      setIsCreating(false);
    }
  };

  return (
    <div className="create-game-screen">
      <h1>Online Chess</h1>
      <p>Start a new game and share the link with your opponent.</p>
      <button type="button" onClick={handleCreate} disabled={isCreating}>
        {isCreating ? "Creating…" : "Create Game"}
      </button>
      {error && (
        <p className="create-game-screen__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
