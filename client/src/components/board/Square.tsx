import type { BoardPiece } from "../../utils/fen";
import { Piece } from "./Piece";

export interface SquareProps {
  square: string;
  piece: BoardPiece | null;
  isDarkSquare: boolean;
  isSelected: boolean;
  isLegalDestination: boolean;
  isLastMoveSquare: boolean;
  onClick: (square: string) => void;
}

export function Square({
  square,
  piece,
  isDarkSquare,
  isSelected,
  isLegalDestination,
  isLastMoveSquare,
  onClick,
}: SquareProps) {
  const classNames = [
    "chess-square",
    isDarkSquare ? "chess-square--dark" : "chess-square--light",
    piece ? "chess-square--occupied" : "chess-square--empty",
    isSelected ? "chess-square--selected" : "",
    isLegalDestination ? "chess-square--legal-destination" : "",
    isLastMoveSquare ? "chess-square--last-move" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <button
      type="button"
      className={classNames}
      data-square={square}
      aria-label={square}
      onClick={() => onClick(square)}
    >
      {piece && <Piece piece={piece} />}
      {isLegalDestination && !piece && <span className="chess-square__hint" aria-hidden="true" />}
    </button>
  );
}
