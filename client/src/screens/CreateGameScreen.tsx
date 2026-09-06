import { useState } from "react";
import { useNavigate } from "react-router-dom";
import * as gamesApi from "../api/gamesApi";
import { useGameSession } from "../state/useGameSession";
import { OpponentTypeSelector } from "../components/panels/OpponentTypeSelector";
import { describeApiError } from "../utils/describeApiError";
import type { OpponentType } from "../types/gameTypes";
import "./createGameScreen.css";

export function CreateGameScreen() {
  const navigate = useNavigate();
  const { saveSession } = useGameSession();
  const [isCreating, setIsCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Default to Human (legacy behavior) per AC-1.1.
  const [opponentType, setOpponentType] = useState<OpponentType>("Human");

  const handleCreate = async () => {
    setIsCreating(true);
    setError(null);
    try {
      const response = await gamesApi.createGame(opponentType);
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
      <p>Start a new game against a friend or the computer.</p>
      <OpponentTypeSelector
        value={opponentType}
        onChange={setOpponentType}
        disabled={isCreating}
      />
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
