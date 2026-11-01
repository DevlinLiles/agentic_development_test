// Stories for the board square across its states, focused on the accessible
// naming behavior (UX verification findings A-1/A-4): a square's accessible
// name always carries the square itself plus its piece content and, when
// relevant, its legal-destination state — never visuals alone.
//
// Every story's parameters.docs.description cites the exact accessible name a
// screen reader announces, so the naming contract is reviewable from the
// story itself.

import { Square } from "./Square";
import type { StoryObj, Meta } from "../../storybook/stubs";
import type { BoardPiece } from "../../utils/fen";

const whitePawn: BoardPiece = { type: "Pawn", color: "White" };
const blackQueen: BoardPiece = { type: "Queen", color: "Black" };

interface SquareArgs {
  square: string;
  piece: BoardPiece | null;
  isSelected: boolean;
  isLegalDestination: boolean;
  isLastMoveSquare: boolean;
}

function isDark(square: string): boolean {
  const file = square.charCodeAt(0) - "a".charCodeAt(0);
  const rank = Number.parseInt(square.slice(1), 10);
  return (file + rank) % 2 === 0;
}

function renderSquare(args: SquareArgs) {
  return (
    <Square
      square={args.square}
      piece={args.piece}
      isDarkSquare={isDark(args.square)}
      isSelected={args.isSelected}
      isLegalDestination={args.isLegalDestination}
      isLastMoveSquare={args.isLastMoveSquare}
      onClick={() => {}}
    />
  );
}

const meta: Meta<SquareArgs> = {
  title: "Board/Square",
  component: Square,
  parameters: { layout: "centered" },
  args: { isSelected: false, isLegalDestination: false, isLastMoveSquare: false },
};

export default meta;

/**
 * Occupied square — the accessible name carries piece content ("e4, white
 * pawn"), not the bare square. Finding A-1: without this, a screen-reader
 * user activating squares in a solo game never learns what is on them.
 */
export const OccupiedWhitePawn: StoryObj<SquareArgs> = {
  args: { square: "e4", piece: whitePawn },
  render: renderSquare,
  parameters: {
    docs: { description: { story: 'Accessible name: "e4, white pawn" (A-1).' } },
  },
};

/** Occupied square, opposite color: "d8, black queen". */
export const OccupiedBlackQueen: StoryObj<SquareArgs> = {
  args: { square: "d8", piece: blackQueen },
  render: renderSquare,
  parameters: {
    docs: { description: { story: 'Accessible name: "d8, black queen" (A-1).' } },
  },
};

/** Empty square — named by the square alone: "a1". */
export const EmptySquare: StoryObj<SquareArgs> = {
  args: { square: "a1", piece: null },
  render: renderSquare,
  parameters: {
    docs: { description: { story: 'Accessible name: "a1".' } },
  },
};

/**
 * Selected square — selection is exposed programmatically (aria-pressed) in
 * addition to the outline, so it is never conveyed by color alone (A-4).
 */
export const SelectedSquare: StoryObj<SquareArgs> = {
  args: { square: "e2", piece: whitePawn, isSelected: true },
  render: renderSquare,
  parameters: {
    docs: { description: { story: 'Accessible name: "e2, white pawn"; aria-pressed="true" (A-4).' } },
  },
};

/**
 * Legal destination of the selected piece — the destination state is part of
 * the accessible name ("e4, can move here"), not just the blue dot (A-4).
 */
export const LegalDestinationEmpty: StoryObj<SquareArgs> = {
  args: { square: "e4", piece: null, isLegalDestination: true },
  render: renderSquare,
  parameters: {
    docs: { description: { story: 'Accessible name: "e4, can move here" (A-4).' } },
  },
};

/** Legal destination that captures — the piece stays in the name: "d5, black pawn, can move here". */
export const LegalDestinationCapture: StoryObj<SquareArgs> = {
  args: { square: "d5", piece: blackQueen, isLegalDestination: true },
  render: renderSquare,
  parameters: {
    docs: { description: { story: 'Accessible name: "d5, black queen, can move here" (A-4).' } },
  },
};

/** Last-move highlight — visual affordance only; naming is unaffected. */
export const LastMoveSquare: StoryObj<SquareArgs> = {
  args: { square: "e4", piece: whitePawn, isLastMoveSquare: true },
  render: renderSquare,
  parameters: {
    docs: { description: { story: 'Accessible name: "e4, white pawn". Highlight is decorative.' } },
  },
};

/** Combined state: selected piece sitting on a last-move square. */
export const SelectedAndLastMove: StoryObj<SquareArgs> = {
  args: { square: "e4", piece: whitePawn, isSelected: true, isLastMoveSquare: true },
  render: renderSquare,
  parameters: {
    docs: { description: { story: 'Accessible name: "e4, white pawn"; aria-pressed="true".' } },
  },
};
