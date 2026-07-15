import { useState } from "react";
import { useNavigate } from "react-router-dom";
import * as gamesApi from "../api/gamesApi";
import { useGameSession } from "../state/useGameSession";
import { useCreateGamePreferences } from "../state/useCreateGamePreferences";
import { describeApiError } from "../utils/describeApiError";
import type { GameOpponent, PlayerColor } from "../types/gameTypes";
import "./createGameScreen.css";

type OpponentChoice = GameOpponent;
type ColorChoice = PlayerColor;

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

const COLOR_OPTIONS: ReadonlyArray<{
  value: ColorChoice;
  label: string;
  description: string;
}> = [
  {
    value: "White",
    label: "White",
    description: "Move first. Your pieces start on the bottom of the board.",
  },
  {
    value: "Black",
    label: "Black",
    description: "Respond to White's opening. Your pieces start on top.",
  },
];

export function CreateGameScreen() {
  const navigate = useNavigate();
  const { saveSession } = useGameSession();
  const { preferences, setPreferences } = useCreateGamePreferences();
  const { opponent, color } = preferences;
  const [isCreating, setIsCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleOpponentChange = (next: OpponentChoice): void => {
    setPreferences({ ...preferences, opponent: next });
  };

  const handleColorChange = (next: ColorChoice): void => {
    setPreferences({ ...preferences, color: next });
  };

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
      <p>Start a new game and choose your opponent and color.</p>

      <p className="create-game-screen__group-heading" id="opponent-group-label">
        Opponent
      </p>
      <div
        className="create-game-screen__options"
        role="radiogroup"
        aria-labelledby="opponent-group-label"
      >
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
                onChange={() => handleOpponentChange(option.value)}
              />
              <span className="create-game-screen__option-label">{option.label}</span>
              <span className="create-game-screen__option-description">
                {option.description}
              </span>
            </label>
          );
        })}
      </div>

      <p className="create-game-screen__group-heading" id="color-group-label">
        Your color
      </p>
      <div
        className="create-game-screen__options"
        role="radiogroup"
        aria-labelledby="color-group-label"
      >
        {COLOR_OPTIONS.map((option) => {
          const selected = option.value === color;
          return (
            <label
              key={option.value}
              className={`create-game-screen__option${selected ? " create-game-screen__option--selected" : ""}`}
            >
              <input
                type="radio"
                name="color"
                value={option.value}
                checked={selected}
                onChange={() => handleColorChange(option.value)}
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
