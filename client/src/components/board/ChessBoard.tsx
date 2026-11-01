import { useMemo } from "react";
import { parseFen, pieceAt } from "../../utils/fen";
import { layoutForColor } from "../../utils/boardLayout";
import type { GameStateResponse, PlayerColor } from "../../types/gameTypes";
import { useClickToMove } from "../../state/useClickToMove";
import { Square } from "./Square";
import { PromotionPicker } from "../panels/PromotionPicker";
import "./chessBoard.css";
import "./chessBoardBusy.css";

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

  // Square visit order for this viewer: rank 8 at the top for White, rank 1
  // at the top for Black (see utils/boardLayout.ts).
  const layout = layoutForColor(yourColor);

  const lastMoveSquares = new Set(
    gameState.lastMove ? [gameState.lastMove.from, gameState.lastMove.to] : [],
  );

  // While a submission is in flight the board is paused. In a computer game
  // that window includes the AI computing its reply (up to ~2s), so the busy
  // state is a visible text status — never conveyed by the dimmed board
  // alone (UX verification finding A-5, WCAG 1.4.1).
  const isComputerGame = gameState.opponentType === "Computer";
  const busyMessage = clickToMove.isAwaitingServer
    ? isComputerGame
      ? "Computer is thinking…"
      : "Sending your move…"
    : null;

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

      <div
        className={`chess-board ${clickToMove.isAwaitingServer ? "chess-board--busy" : ""}`}
        aria-busy={clickToMove.isAwaitingServer ? "true" : undefined}
      >
        {layout.rows.map((row) =>
          row.map(({ square, file, rank }) => {
            // Square shading parity is unchanged from the previous renderer:
            // (file + rank) even = the "--dark" class. (Standard tournament
            // boards put a1 on a dark square; this app's parity is the
            // inverse. Left untouched deliberately — it is a pre-existing
            // visual quirk, out of scope for the AI-opponent work, and every
            // existing rendering depends on it.)
            const isDarkSquare = (file + rank) % 2 === 0;
            return (
              <Square
                key={square}
                square={square}
                piece={pieceAt(grid, square)}
                isDarkSquare={isDarkSquare}
                isSelected={clickToMove.selectedSquare === square}
                isLegalDestination={clickToMove.legalDestinations.includes(square)}
                isLastMoveSquare={lastMoveSquares.has(square)}
                onClick={interactive ? clickToMove.handleSquareClick : noop}
              />
            );
          }),
        )}
      </div>

      {/* Visible, announced busy status — sits with the board it pauses. */}
      {busyMessage && (
        <p className="chess-board__busy" role="status">
          {busyMessage}
        </p>
      )}

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
