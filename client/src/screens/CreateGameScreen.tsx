import { useState } from "react";
import { useNavigate } from "react-router-dom";
import * as gamesApi from "../api/gamesApi";
import { useGameSession } from "../state/useGameSession";
import { describeApiError } from "../utils/describeApiError";
import type { CreateGameResponse } from "../types/gameTypes";
import "./createGameScreen.css";

export function CreateGameScreen() {
  const navigate = useNavigate();
  const { saveSession } = useGameSession();
  const [isCreating, setIsCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const startGame = async (create: () => Promise<CreateGameResponse>) => {
    setIsCreating(true);
    setError(null);
    try {
      const response = await create();
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

  const handleCreateHuman = () => void startGame(gamesApi.createGame);
  const handleCreateAi = () => void startGame(gamesApi.createAiGame);

  return (
    <div className="create-game-screen">
      <h1>Online Chess</h1>
      <p>Start a new game and share the link with your opponent, or play against the AI.</p>
      <div className="create-game-screen__actions">
        <button type="button" onClick={handleCreateHuman} disabled={isCreating}>
          {isCreating ? "Creating…" : "Create Game"}
        </button>
        <button type="button" onClick={handleCreateAi} disabled={isCreating}>
          {isCreating ? "Creating…" : "Play vs AI"}
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
