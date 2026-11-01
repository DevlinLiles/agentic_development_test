import { forwardRef } from "react";
import type { ButtonHTMLAttributes } from "react";
import type { BoardPiece } from "../../utils/fen";
import { buildSquareName } from "../../utils/squareNaming";
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

/**
 * A single board square: a real `<button>` (keyboard operable for free) whose
 * accessible name is built by `buildSquareName` — the square name itself plus
 * its piece content and, when relevant, its legal-destination state. The
 * visual selection/destination/last-move affordances are therefore never the
 * only signal for those states (WCAG 1.4.1 / 4.1.2).
 *
 * Forwarded refs keep the button focusable programmatically, which dialog
 * overlays (the promotion picker) need for focus management.
 *
 * The native `onClick` is omitted from the spread-in button attributes: this
 * component owns the click contract (`(square: string) => void`) and adapts
 * it to the DOM handler internally. Intersecting the two signatures would
 * require a handler assignable to both, which no function can satisfy.
 */
export const Square = forwardRef<
  HTMLButtonElement,
  SquareProps & Omit<ButtonHTMLAttributes<HTMLButtonElement>, "onClick">
>(function Square(
  {
    square,
    piece,
    isDarkSquare,
    isSelected,
    isLegalDestination,
    isLastMoveSquare,
    onClick,
    ...rest
  },
  ref,
) {
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
        ref={ref}
        className={classNames}
        data-square={square}
        aria-label={buildSquareName({ square, piece, isLegalDestination })}
        aria-pressed={isSelected ? true : undefined}
        onClick={() => onClick(square)}
        {...rest}
      >
        {piece && <Piece piece={piece} />}
        {isLegalDestination && !piece && <span className="chess-square__hint" aria-hidden="true" />}
      </button>
    );
  },
);
