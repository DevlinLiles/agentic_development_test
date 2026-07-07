// Cosmetic-only, piece-shape move hints for the click-to-move UI. This is
// explicitly NOT a chess rules engine: it ignores checks, pins, castling,
// en passant, and whose turn it is beyond what the caller filters. It only
// answers "which squares does this piece's shape reach, given what's
// sitting on the board" so the UI can highlight plausible destinations.
// The server is the sole source of truth for legality.

import type { BoardGrid, BoardPieceType } from "./fen";
import { pieceAt, squareName, squareToCoords } from "./fen";
import type { PlayerColor } from "../types/gameTypes";

function inBounds(file: number, rank: number): boolean {
  return file >= 0 && file <= 7 && rank >= 1 && rank <= 8;
}

function slide(
  grid: BoardGrid,
  file: number,
  rank: number,
  color: PlayerColor,
  directions: Array<[number, number]>,
): string[] {
  const destinations: string[] = [];
  for (const [df, dr] of directions) {
    let f = file + df;
    let r = rank + dr;
    while (inBounds(f, r)) {
      const occupant = pieceAt(grid, squareName(f, r));
      if (!occupant) {
        destinations.push(squareName(f, r));
      } else {
        if (occupant.color !== color) destinations.push(squareName(f, r));
        break;
      }
      f += df;
      r += dr;
    }
  }
  return destinations;
}

function stepOffsets(
  grid: BoardGrid,
  file: number,
  rank: number,
  color: PlayerColor,
  offsets: Array<[number, number]>,
): string[] {
  const destinations: string[] = [];
  for (const [df, dr] of offsets) {
    const f = file + df;
    const r = rank + dr;
    if (!inBounds(f, r)) continue;
    const occupant = pieceAt(grid, squareName(f, r));
    if (!occupant || occupant.color !== color) {
      destinations.push(squareName(f, r));
    }
  }
  return destinations;
}

function pawnMoves(grid: BoardGrid, file: number, rank: number, color: PlayerColor): string[] {
  const direction = color === "White" ? 1 : -1;
  const startRank = color === "White" ? 2 : 7;
  const destinations: string[] = [];

  const oneAhead = rank + direction;
  if (inBounds(file, oneAhead) && !pieceAt(grid, squareName(file, oneAhead))) {
    destinations.push(squareName(file, oneAhead));
    const twoAhead = rank + direction * 2;
    if (rank === startRank && inBounds(file, twoAhead) && !pieceAt(grid, squareName(file, twoAhead))) {
      destinations.push(squareName(file, twoAhead));
    }
  }

  for (const df of [-1, 1]) {
    const f = file + df;
    const r = rank + direction;
    if (!inBounds(f, r)) continue;
    const occupant = pieceAt(grid, squareName(f, r));
    if (occupant && occupant.color !== color) {
      destinations.push(squareName(f, r));
    }
  }

  return destinations;
}

const ROOK_DIRECTIONS: Array<[number, number]> = [
  [1, 0],
  [-1, 0],
  [0, 1],
  [0, -1],
];

const BISHOP_DIRECTIONS: Array<[number, number]> = [
  [1, 1],
  [1, -1],
  [-1, 1],
  [-1, -1],
];

const KNIGHT_OFFSETS: Array<[number, number]> = [
  [1, 2],
  [2, 1],
  [-1, 2],
  [-2, 1],
  [1, -2],
  [2, -1],
  [-1, -2],
  [-2, -1],
];

const KING_OFFSETS: Array<[number, number]> = [
  [1, 0],
  [-1, 0],
  [0, 1],
  [0, -1],
  [1, 1],
  [1, -1],
  [-1, 1],
  [-1, -1],
];

/**
 * Returns the cosmetic set of destination squares for the piece on
 * `square`, based purely on how that piece type moves on an otherwise
 * empty-or-occupied board. Does not account for check, pins, castling
 * rights, or en passant. Returns an empty array if there's no piece there.
 */
export function getPseudoLegalDestinations(grid: BoardGrid, square: string): string[] {
  const coords = squareToCoords(square);
  if (!coords) return [];
  const piece = pieceAt(grid, square);
  if (!piece) return [];

  const { file, rank } = coords;
  const type: BoardPieceType = piece.type;

  switch (type) {
    case "Pawn":
      return pawnMoves(grid, file, rank, piece.color);
    case "Knight":
      return stepOffsets(grid, file, rank, piece.color, KNIGHT_OFFSETS);
    case "King":
      return stepOffsets(grid, file, rank, piece.color, KING_OFFSETS);
    case "Bishop":
      return slide(grid, file, rank, piece.color, BISHOP_DIRECTIONS);
    case "Rook":
      return slide(grid, file, rank, piece.color, ROOK_DIRECTIONS);
    case "Queen":
      return slide(grid, file, rank, piece.color, [...ROOK_DIRECTIONS, ...BISHOP_DIRECTIONS]);
    default:
      return [];
  }
}
