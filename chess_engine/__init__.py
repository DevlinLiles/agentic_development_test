"""Chess engine core: board representation, game state, and move application.

Public API
----------
* :class:`board.Board` / :class:`board.Color` — immutable piece placement.
* :class:`state.GameState` — full FEN state (placement + side/castling/ep/clocks).
* :func:`state.apply_move` — produce a legal successor state without mutating input.
* :class:`state.Move` / :class:`state.MoveFlag` — move representation.

This is the substrate for move generation, terminal-state detection, and AI
integration. It is pure-Python / stdlib-only.
"""

from __future__ import annotations

from .board import Board, Color, Square, square_name, parse_square, file_of, rank_of
from .state import (
    CastlingRights,
    GameState,
    IllegalMoveError,
    Move,
    MoveFlag,
    SideToMoveError,
    apply_move,
    starting_state,
)

__all__ = [
    "Board",
    "Color",
    "Square",
    "square_name",
    "parse_square",
    "file_of",
    "rank_of",
    "CastlingRights",
    "GameState",
    "IllegalMoveError",
    "Move",
    "MoveFlag",
    "SideToMoveError",
    "apply_move",
    "starting_state",
]
