// Pure FEN piece-placement parsing. This is straightforward string parsing,
// not chess rules logic: no move generation, no legality, no check
// detection. That lives on the server; the client only needs to know what
// piece sits on what square to render the board and offer cosmetic move
// hints (see src/state/useClickToMove.ts).

import type { PlayerColor } from "../types/gameTypes";

export type BoardPieceType =
  | "King"
  | "Queen"
  | "Rook"
  | "Bishop"
  | "Knight"
  | "Pawn";

export interface BoardPiece {
  type: BoardPieceType;
  color: PlayerColor;
}

export interface BoardSquare {
  /** 0-based file index, 0 = a, 7 = h */
  file: number;
  /** 1-based rank, 1-8 */
  rank: number;
  /** Algebraic square name, e.g. "e4" */
  square: string;
  piece: BoardPiece | null;
}

/** 8x8 grid, outer index 0 = rank 8 down to outer index 7 = rank 1; inner index 0 = file a to 7 = file h. */
export type BoardGrid = BoardSquare[][];

const FILES = "abcdefgh";

const PIECE_LETTERS: Record<string, BoardPieceType> = {
  p: "Pawn",
  n: "Knight",
  b: "Bishop",
  r: "Rook",
  q: "Queen",
  k: "King",
};

export function squareName(file: number, rank: number): string {
  return `${FILES[file]}${rank}`;
}

export function squareToCoords(square: string): { file: number; rank: number } | null {
  if (square.length < 2) return null;
  const file = FILES.indexOf(square[0]);
  const rank = Number.parseInt(square.slice(1), 10);
  if (file === -1 || Number.isNaN(rank) || rank < 1 || rank > 8) return null;
  return { file, rank };
}

/**
 * Parses the piece-placement field of a FEN string (the part before the
 * first space, or the whole string if it has no spaces) into an 8x8 grid.
 */
export function parseFen(fen: string): BoardGrid {
  const placement = fen.split(" ")[0] ?? "";
  const rankStrings = placement.split("/");

  const grid: BoardGrid = [];

  for (let i = 0; i < 8; i++) {
    const rank = 8 - i;
    const rankString = rankStrings[i] ?? "";
    const row: BoardSquare[] = [];
    let file = 0;

    for (const char of rankString) {
      if (file >= 8) break;
      const digit = Number.parseInt(char, 10);
      if (!Number.isNaN(digit)) {
        for (let e = 0; e < digit && file < 8; e++, file++) {
          row.push({ file, rank, square: squareName(file, rank), piece: null });
        }
        continue;
      }

      const lower = char.toLowerCase();
      const type = PIECE_LETTERS[lower];
      if (!type) continue; // ignore unrecognized characters defensively
      const color: PlayerColor = char === lower ? "Black" : "White";
      row.push({
        file,
        rank,
        square: squareName(file, rank),
        piece: { type, color },
      });
      file++;
    }

    // Pad defensively if the FEN rank was short, so the grid is always 8x8.
    while (row.length < 8) {
      row.push({ file: row.length, rank, square: squareName(row.length, rank), piece: null });
    }

    grid.push(row);
  }

  return grid;
}

export function pieceAt(grid: BoardGrid, square: string): BoardPiece | null {
  const coords = squareToCoords(square);
  if (!coords) return null;
  const row = grid[8 - coords.rank];
  return row?.[coords.file]?.piece ?? null;
}
