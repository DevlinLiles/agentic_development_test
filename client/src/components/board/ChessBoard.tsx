import { useMemo } from "react";
import { parseFen } from "../../utils/fen";
import type { GameStateResponse, PlayerColor } from "../../types/gameTypes";
import { useClickToMove } from "../../state/useClickToMove";
import { Square } from "./Square";
import { PromotionPicker } from "../panels/PromotionPicker";
import "./chessBoard.css";

export interface ChessBoardProps {
  gameId: string;
  playerToken: string | null;
  yourColor: PlayerColor | null;
  gameState: GameStateResponse;
  onStateUpdate: (state: GameStateResponse) => void;
}

export function ChessBoard({ gameId, playerToken, yourColor, gameState, onStateUpdate }: ChessBoardProps) {
  const grid = useMemo(() => parseFen(gameState.fen), [gameState.fen]);
  const interactive = gameState.status === "Active";

  const clickToMove = useClickToMove({
    gameId,
    playerToken,
    yourColor,
    gameState,
    onStateUpdate,
  });

  const orientedRows = yourColor === "Black" ? [...grid].reverse() : grid;

  const lastMoveSquares = new Set(
    gameState.lastMove ? [gameState.lastMove.from, gameState.lastMove.to] : [],
  );

  return (
    <div className="chess-board-wrapper">
      {clickToMove.error && (
        <div className="chess-board__error" role="alert">
          <span>{clickToMove.error}</span>
          <button type="button" onClick={clickToMove.clearError} aria-label="Dismiss error">
            ×
          </button>
        </div>
      )}

      <div className={`chess-board ${clickToMove.isAwaitingServer ? "chess-board--busy" : ""}`}>
        {orientedRows.map((row) => {
          const orientedSquares = yourColor === "Black" ? [...row].reverse() : row;
          return orientedSquares.map((cell) => {
            const isDarkSquare = (cell.file + cell.rank) % 2 === 0;
            return (
              <Square
                key={cell.square}
                square={cell.square}
                piece={cell.piece}
                isDarkSquare={isDarkSquare}
                isSelected={clickToMove.selectedSquare === cell.square}
                isLegalDestination={clickToMove.legalDestinations.includes(cell.square)}
                isLastMoveSquare={lastMoveSquares.has(cell.square)}
                onClick={interactive ? clickToMove.handleSquareClick : noop}
              />
            );
          });
        })}
      </div>

      {clickToMove.promotionPending && (
        <PromotionPicker
          color={yourColor ?? "White"}
          onSelect={clickToMove.handlePromotionSelect}
          onCancel={clickToMove.handleCancelPromotion}
        />
      )}
    </div>
  );
}

function noop() {
  // Board is non-interactive (game not Active); swallow clicks.
}
