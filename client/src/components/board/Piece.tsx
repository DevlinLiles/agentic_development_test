import type { BoardPiece } from "../../utils/fen";

const GLYPHS: Record<BoardPiece["color"], Record<BoardPiece["type"], string>> = {
  White: {
    King: "♔",
    Queen: "♕",
    Rook: "♖",
    Bishop: "♗",
    Knight: "♘",
    Pawn: "♙",
  },
  Black: {
    King: "♚",
    Queen: "♛",
    Rook: "♜",
    Bishop: "♝",
    Knight: "♞",
    Pawn: "♟",
  },
};

export interface PieceProps {
  piece: BoardPiece;
}

export function Piece({ piece }: PieceProps) {
  return (
    <span
      className={`chess-piece chess-piece--${piece.color.toLowerCase()}`}
      role="img"
      aria-label={`${piece.color} ${piece.type}`}
    >
      {GLYPHS[piece.color][piece.type]}
    </span>
  );
}
