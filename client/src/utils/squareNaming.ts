// Builds the accessible name of a board square: the square name itself, plus
// — when the square is occupied — ", white pawn"-style piece content, and —
// when the square is currently a legal destination — ", can move here".
//
// This is the one place the app derives a square's accessible name, so the
// board button, the screen-reader experience and the storybook documentation
// can never drift apart.
//
// Why this exists (UX verification finding A-1, WCAG 4.1.2 Name/Role/Value):
// a bare `aria-label="e4"` overrides the nested piece's own label in the
// accessible-name computation, so a screen-reader user would hear "e4, button"
// and never learn that e4 holds a white pawn. In a computer game there is no
// second human to ask what is on a square, so the board itself has to say it.

import type { BoardPiece } from "./fen";

export interface SquareNameOptions {
  /** Algebraic square name, e.g. "e4". */
  square: string;
  /** Piece occupying the square, or null when it is empty. */
  piece: BoardPiece | null;
  /**
   * Whether the square is a legal destination of the currently selected
   * piece. Conveyed in the name (rather than by color/shape alone) per
   * WCAG 1.4.1 Use of Color.
   */
  isLegalDestination?: boolean;
}

/**
 * Builds a square's accessible name, e.g. "e4, white pawn" or — for a legal
 * destination of the selected piece — "e4, can move here". Empty squares are
 * named by their square alone ("e4").
 */
export function buildSquareName({
  square,
  piece,
  isLegalDestination = false,
}: SquareNameOptions): string {
  const name = piece ? `${square}, ${piece.color.toLowerCase()} ${piece.type.toLowerCase()}` : square;
  return isLegalDestination ? `${name}, can move here` : name;
}
