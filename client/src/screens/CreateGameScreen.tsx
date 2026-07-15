import { useState } from "react";
import { useNavigate } from "react-router-dom";
import * as gamesApi from "../api/gamesApi";
import { useGameSession } from "../state/useGameSession";
import { describeApiError } from "../utils/describeApiError";
import type { GameOpponent } from "../types/gameTypes";
import "./createGameScreen.css";

type OpponentChoice = GameOpponent;

const OPPONENT_OPTIONS: ReadonlyArray<{
  value: OpponentChoice;
  label: string;
  description: string;
}> = [
  {
    value: "Human",
    label: "Play a friend",
    description: "Create a game and share the link with your opponent.",
  },
  {
    value: "Ai",
    label: "Play the computer",
    description: "Solo game against a basic heuristic AI opponent.",
  },
];

export function CreateGameScreen() {
  const navigate = useNavigate();
  const { saveSession } = useGameSession();
  const [opponent, setOpponent] = useState<OpponentChoice>("Human");
  const [isCreating, setIsCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleCreate = async () => {
    setIsCreating(true);
    setError(null);
    try {
      const response = await gamesApi.createGame(opponent);
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
      <p>Start a new game and choose your opponent.</p>

      <div className="create-game-screen__options" role="radiogroup" aria-label="Opponent">
        {OPPONENT_OPTIONS.map((option) => {
          const selected = option.value === opponent;
          return (
            <label
              key={option.value}
              className={`create-game-screen__option${selected ? " create-game-screen__option--selected" : ""}`}
            >
              <input
                type="radio"
                name="opponent"
                value={option.value}
                checked={selected}
                onChange={() => setOpponent(option.value)}
              />
              <span className="create-game-screen__option-label">{option.label}</span>
              <span className="create-game-screen__option-description">
                {option.description}
              </span>
            </label>
          );
        })}
      </div>

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
