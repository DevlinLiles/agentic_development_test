"""Board representation: piece placement on a chess board.

This module encodes only the *piece placement* component of a chess position
(the first field of a FEN string). The remaining FEN state — side to move,
castling rights, en-passant target, halfmove clock and fullmove number — lives
in :mod:`chess_engine.state`, which composes a :class:`Board` with those fields
to form a complete :class:`~chess_engine.state.GameState`.

Design notes
------------
* Squares are addressed with the little-endian rank-file (LERF) mapping used
  by most engine code: ``a1 == 0`` ... ``h1 == 7``, ``a2 == 8`` ... ``h8 == 63``.
  Thus ``file = square % 8`` (0=a ... 7=h) and ``rank = square // 8``
  (0=rank-1 ... 7=rank-8).
* Pieces are single characters, exactly as in FEN: uppercase ``PNBRQK`` for
  white, lowercase ``pnbrqk`` for black, and ``None`` for an empty square.
* :class:`Board` is an immutable, frozen dataclass backed by a 64-tuple. Every
  mutating operation returns a *new* :class:`Board`, which is what lets
  :func:`chess_engine.state.apply_move` produce successor states without
  touching its input (a requirement for threefold-repetition tracking and for
  building a clean search tree).
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Iterator, Optional

__all__ = [
    "Color",
    "Square",
    "FILES",
    "RANKS",
    "square_name",
    "parse_square",
    "file_of",
    "rank_of",
    "WHITE_PIECES",
    "BLACK_PIECES",
    "PIECE_CHARS",
    "Board",
]

#: Number of squares on a board edge.
FILES = "abcdefgh"
RANKS = "12345678"

#: Type alias for a square index in ``[0, 63]``.
Square = int

WHITE_PIECES = frozenset("PNBRQK")
BLACK_PIECES = frozenset("pnbrqk")
PIECE_CHARS = WHITE_PIECES | BLACK_PIECES


class Color(Enum):
    """The side to move / a piece's color."""

    WHITE = "w"
    BLACK = "b"

    @property
    def fen_char(self) -> str:
        """The FEN character for this color (``'w'`` or ``'b'``)."""
        return self.value

    @property
    def opponent(self) -> "Color":
        """The other color."""
        return Color.BLACK if self is Color.WHITE else Color.WHITE

    @property
    def is_white(self) -> bool:
        return self is Color.WHITE


def file_of(square: Square) -> int:
    """File index ``0..7`` (a..h) of ``square``."""
    return square % 8


def rank_of(square: Square) -> int:
    """Rank index ``0..7`` (1..8) of ``square``."""
    return square // 8


def square_name(square: Square) -> str:
    """Convert a square index to algebraic notation, e.g. ``0`` -> ``'a1'``."""
    if not 0 <= square <= 63:
        raise ValueError(f"square index out of range: {square}")
    return FILES[file_of(square)] + RANKS[rank_of(square)]


def parse_square(name: str) -> Square:
    """Convert algebraic notation to a square index, e.g. ``'a1'`` -> ``0``.

    Raises :class:`ValueError` for malformed names.
    """
    if len(name) != 2:
        raise ValueError(f"invalid square name: {name!r}")
    file_char, rank_char = name[0], name[1]
    if file_char not in FILES or rank_char not in RANKS:
        raise ValueError(f"invalid square name: {name!r}")
    return rank_of_char(rank_char) * 8 + FILES.index(file_char)


def rank_of_char(rank_char: str) -> int:
    return RANKS.index(rank_char)


@dataclass(frozen=True)
class Board:
    """Immutable piece-placement grid for an 8x8 chess board.

    Internally a 64-tuple indexed by LERF square (``a1 == 0`` ... ``h8 == 63``).
    Each entry is a single FEN piece character (``'P'`` ... ``'k'``) or ``None``
    for an empty square.
    """

    pieces: tuple[Optional[str], ...]

    def __post_init__(self) -> None:
        if len(self.pieces) != 64:
            raise ValueError(
                f"Board requires exactly 64 entries, got {len(self.pieces)}"
            )

    # --- access -----------------------------------------------------------

    def piece_at(self, square: Square) -> Optional[str]:
        """Return the piece character on ``square`` or ``None`` if empty."""
        return self.pieces[square]

    def color_at(self, square: Square) -> Optional[Color]:
        """Return the :class:`Color` of the piece on ``square`` or ``None``."""
        piece = self.pieces[square]
        if piece is None:
            return None
        return Color.WHITE if piece.isupper() else Color.BLACK

    def king_square(self, color: Color) -> Optional[Square]:
        """Return the square of ``color``'s king, or ``None`` if absent."""
        target = "K" if color is Color.WHITE else "k"
        for sq, piece in enumerate(self.pieces):
            if piece == target:
                return sq
        return None

    def __iter__(self) -> Iterator[tuple[Square, Optional[str]]]:
        for sq, piece in enumerate(self.pieces):
            yield sq, piece

    # --- mutation (returns new boards) ------------------------------------

    def with_piece(self, square: Square, piece: Optional[str]) -> "Board":
        """Return a new board with ``piece`` placed on ``square``."""
        if not 0 <= square <= 63:
            raise ValueError(f"square index out of range: {square}")
        if piece is not None and piece not in PIECE_CHARS:
            raise ValueError(f"invalid piece character: {piece!r}")
        new_pieces = list(self.pieces)
        new_pieces[square] = piece
        return Board(tuple(new_pieces))

    def copy(self) -> "Board":
        """Return an equal board (boards are immutable, so this is cheap)."""
        return Board(self.pieces)

    # --- FEN ---------------------------------------------------------------

    @classmethod
    def empty(cls) -> "Board":
        """A board with no pieces."""
        return cls(tuple(None for _ in range(64)))

    @classmethod
    def from_fen_placement(cls, placement: str) -> "Board":
        """Build a board from the first FEN field (e.g. ``'rnbqkbnr/...'``)."""
        ranks = placement.split("/")
        if len(ranks) != 8:
            raise ValueError(
                f"FEN placement must have 8 ranks, got {len(ranks)}: {placement!r}"
            )
        pieces: list[Optional[str]] = [None] * 64
        for row_index, row in enumerate(ranks):
            rank = 7 - row_index  # FEN lists rank 8 first
            file = 0
            for ch in row:
                if ch.isdigit():
                    file += int(ch)
                elif ch in PIECE_CHARS:
                    if file > 7:
                        raise ValueError(
                            f"too many files in FEN rank {row!r}: {placement!r}"
                        )
                    pieces[rank * 8 + file] = ch
                    file += 1
                else:
                    raise ValueError(f"invalid character in FEN placement: {ch!r}")
            if file != 8:
                raise ValueError(
                    f"FEN rank {row!r} does not cover 8 files: {placement!r}"
                )
        return cls(tuple(pieces))

    def to_fen_placement(self) -> str:
        """Render this board as the first FEN field."""
        rows: list[str] = []
        for rank in range(7, -1, -1):  # rank 8 first
            row = ""
            empty = 0
            for file in range(8):
                piece = self.pieces[rank * 8 + file]
                if piece is None:
                    empty += 1
                else:
                    if empty:
                        row += str(empty)
                        empty = 0
                    row += piece
            if empty:
                row += str(empty)
            rows.append(row)
        return "/".join(rows)

    def __str__(self) -> str:
        return self.to_fen_placement()
