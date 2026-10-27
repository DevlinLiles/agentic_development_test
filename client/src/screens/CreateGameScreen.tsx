import { useState } from "react";
import { useNavigate } from "react-router-dom";
import * as gamesApi from "../api/gamesApi";
import { useGameSession } from "../state/useGameSession";
import { describeApiError } from "../utils/describeApiError";
import { OpponentChooser } from "../components/panels/OpponentChooser";
import type { OpponentKind } from "../types/gameTypes";
import "./createGameScreen.css";

export function CreateGameScreen() {
  const navigate = useNavigate();
  const { saveSession } = useGameSession();
  const [opponent, setOpponent] = useState<OpponentKind>("Human");
  const [isCreating, setIsCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleOpponentChange = (value: OpponentKind) => {
    setOpponent(value);
    // A mode switch invalidates whatever the previous attempt failed with —
    // don't leave a stale error sitting above a different choice.
    if (error !== null) setError(null);
  };

  const handleCreate = async () => {
    // Guarded instead of `disabled` so the button keeps its place in the tab
    // order and focus is never silently dropped mid-request.
    if (isCreating) return;
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
      // The chosen opponent mode is deliberately kept in state so a retry
      // uses the same mode the user selected.
      setError(describeApiError(err));
      setIsCreating(false);
    }
  };

  return (
    <div className="create-game-screen">
      <h1>Online Chess</h1>
      <p>Start a new game against a friend or the computer.</p>
      <OpponentChooser
        value={opponent}
        onChange={handleOpponentChange}
        disabled={isCreating}
      />
      <button type="button" onClick={handleCreate} aria-disabled={isCreating}>
        {isCreating
          ? opponent === "Computer"
            ? "Starting game…"
            : "Creating…"
          : "Create Game"}
      </button>
      {error && (
        <p className="create-game-screen__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
