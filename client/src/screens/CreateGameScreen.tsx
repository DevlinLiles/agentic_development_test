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
  const [isCreating, setIsCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleCreate = async (mode: GameMode) => {
    setIsCreating(true);
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
      setIsCreating(false);
    }
  };

  return (
    <div className="create-game-screen">
      <h1>Online Chess</h1>
      <p>Start a new game and share the link with your opponent, or play against the computer.</p>
      <div className="create-game-screen__options">
        <button
          type="button"
          className="create-game-screen__option"
          onClick={() => handleCreate("TwoPlayer")}
          disabled={isCreating}
        >
          <span className="create-game-screen__option-title">Create Game</span>
          <span className="create-game-screen__option-desc">
            Play a two-player match and share the join link with a friend.
          </span>
        </button>
        <button
          type="button"
          className="create-game-screen__option"
          onClick={() => handleCreate("VsAi")}
          disabled={isCreating}
        >
          <span className="create-game-screen__option-title">Play vs Computer</span>
          <span className="create-game-screen__option-desc">
            Start a game right away against the AI opponent.
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
