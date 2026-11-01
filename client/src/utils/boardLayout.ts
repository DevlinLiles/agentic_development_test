// Chessboard square layout for a given orientation: which ranks and files
// the grid visits, in what order, for each viewer perspective.
//
// The board component renders the same 8x8 grid for everyone; only the
// *order* the squares are visited in changes with the viewer's color. This
// module owns that ordering so it can be documented (and pinned by tests)
// separately from the rendering itself.

import type { PlayerColor } from "../types/gameTypes";

/** A rank (1–8) and file (a–h) pair locating one square. */
export interface BoardFileRank {
  /** 0-based file index, 0 = a … 7 = h */
  file: number;
  /** 1-based rank number, 1 … 8 */
  rank: number;
  /** Algebraic square name, e.g. "e4". */
  square: string;
}

const FILES = "abcdefgh";
const ORIENTED_FILES = [...FILES]; // a…h

/** File order as visited when the viewer plays Black (mirrored: h…a). */
const ORIENTED_FILES_BLACK = [...ORIENTED_FILES].reverse();

/** Rank order as visited when the viewer plays White (top-to-bottom: 8→1). */
const ORIENTED_RANKS_WHITE = [8, 7, 6, 5, 4, 3, 2, 1];

/** Rank order as visited when the viewer plays Black (mirrored: 1→8). */
const ORIENTED_RANKS_BLACK = [1, 2, 3, 4, 5, 6, 7, 8];

/**
 * One orientation's full visit order: 64 rank/file pairs, row-major (each
 * visual row is one rank, walked left-to-right within it).
 */
export interface BoardLayout {
  orientation: PlayerColor;
  /** The 8 visual rows of the board, in top-to-bottom order. */
  rows: BoardFileRank[][];
}

/** The White-perspective layout: rank 8 at the top, file a at the left. */
export const WHITE_PERSPECTIVE: BoardLayout = buildLayout("White");

/** The Black-perspective layout: rank 1 at the top, file h at the left. */
export const BLACK_PERSPECTIVE: BoardLayout = buildLayout("Black");

/**
 * The layout for a viewer color. `yourColor` null/undefined resolves to White
 * (spectator default — the human-less perspective of an unsaved session).
 */
export function layoutForColor(yourColor: PlayerColor | null | undefined): BoardLayout {
  return yourColor === "Black" ? BLACK_PERSPECTIVE : WHITE_PERSPECTIVE;
}

function buildLayout(orientation: PlayerColor): BoardLayout {
  const files = orientation === "Black" ? ORIENTED_FILES_BLACK : ORIENTED_FILES;
  const ranks = orientation === "Black" ? ORIENTED_RANKS_BLACK : ORIENTED_RANKS_WHITE;

  const rows = ranks.map((rank) =>
    files.map((_, fileIndex) => ({
      file: fileIndex,
      rank,
      square: `${FILES[fileIndex]}${rank}`,
    })),
  );

  return { orientation, rows };
}
