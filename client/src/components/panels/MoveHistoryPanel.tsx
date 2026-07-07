import { useEffect, useState } from "react";
import * as gamesApi from "../../api/gamesApi";
import type { MoveHistoryEntry } from "../../types/gameTypes";
import { describeApiError } from "../../utils/describeApiError";
import "./moveHistoryPanel.css";

export interface MoveHistoryPanelProps {
  gameId: string;
  token?: string | null;
  /** Bump whenever a new move has landed (e.g. gameState.moveCount) to trigger a refetch. */
  refreshKey: number;
}

export function MoveHistoryPanel({ gameId, token, refreshKey }: MoveHistoryPanelProps) {
  const [moves, setMoves] = useState<MoveHistoryEntry[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    gamesApi
      .getMoveHistory(gameId, token)
      .then((response) => {
        if (!cancelled) {
          setMoves(response.moves);
          setError(null);
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(describeApiError(err));
      });

    return () => {
      cancelled = true;
    };
  }, [gameId, token, refreshKey]);

  return (
    <div className="move-history-panel">
      <h3>Moves</h3>
      {error && <p className="move-history-panel__error">{error}</p>}
      {moves.length === 0 && !error ? (
        <p className="move-history-panel__empty">No moves yet</p>
      ) : (
        <ol className="move-history-panel__list">
          {moves.map((move, index) => (
            <li key={`${move.moveNumber}-${move.color}-${index}`}>
              <span className="move-history-panel__number">{move.moveNumber}.</span>
              <span className="move-history-panel__color">{move.color}</span>
              <span className="move-history-panel__san">{move.san}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
