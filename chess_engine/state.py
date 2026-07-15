"""Game state model and the ``apply_move`` substrate.

A :class:`GameState` bundles a :class:`~chess_engine.board.Board` with the
remaining FEN fields (side to move, castling rights, en-passant target,
halfmove clock, fullmove number) plus the fullmove/halfmove bookkeeping that
turns a static position into something move generation, terminal-state
detection, and an AI can work with.

Key contract
------------
:func:`apply_move` returns a **new** :class:`GameState`; the input state is
never mutated. This immutability is what makes the model usable as a search
tree node substrate and — crucially — what lets callers hash consecutive
positions for threefold-repetition detection (each position is a stable value
that can live in a set / dict without surprise aliasing).

FEN conventions (mirroring the C# ``ChessConstants.StartingFen``)
-----------------------------------------------------------------
``rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1``

Fields: ``<placement> <side> <castling> <ep> <halfmove> <fullmove>``

* ``castling``: subset of ``KQkq`` in that canonical order, or ``-``.
* ``ep``: algebraic square (e.g. ``e3``) or ``-``.
* ``halfmove``: plies since last pawn move or capture (resets to 0 on those).
* ``fullmove``: incremented after black's move; starts at 1.

This module deliberately stays pure-Python / stdlib-only so it can be dropped
into any host (the C# API, an offline tool, a test harness) without external
dependencies.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Optional

from .board import (
    Board,
    Color,
    Square,
    file_of,
    parse_square,
    rank_of,
    square_name,
)

__all__ = [
    "CastlingRights",
    "Move",
    "MoveFlag",
    "GameState",
    "SideToMoveError",
    "IllegalMoveError",
    "starting_state",
    "apply_move",
]


# ---------------------------------------------------------------------------
# Castling rights
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class CastlingRights:
    """The four castling availability flags from a FEN.

    ``white_kingside`` corresponds to ``K``, ``white_queenside`` to ``Q``,
    ``black_kingside`` to ``k`` and ``black_queenside`` to ``q``.
    """

    white_kingside: bool = False
    white_queenside: bool = False
    black_kingside: bool = False
    black_queenside: bool = False

    @classmethod
    def from_fen(cls, text: str) -> "CastlingRights":
        if text == "-":
            return cls()
        chars = set(text)
        unknown = chars - set("KQkq")
        if unknown:
            raise ValueError(f"invalid castling rights field: {text!r}")
        return cls(
            white_kingside="K" in chars,
            white_queenside="Q" in chars,
            black_kingside="k" in chars,
            black_queenside="q" in chars,
        )

    def to_fen(self) -> str:
        chars = (
            ("K" if self.white_kingside else "")
            + ("Q" if self.white_queenside else "")
            + ("k" if self.black_kingside else "")
            + ("q" if self.black_queenside else "")
        )
        return chars or "-"

    def __str__(self) -> str:
        return self.to_fen()


# ---------------------------------------------------------------------------
# Move
# ---------------------------------------------------------------------------


class MoveFlag(Enum):
    """Disambiguates how a move should be applied beyond from/to/piece.

    ``apply_move`` can infer normal moves, captures, double pawn pushes and
    castling from the board + king/rook geometry, but promotion needs an
    explicit piece choice. The flags let callers (a move generator, an AI)
    state their intent unambiguously.
    """

    NORMAL = "normal"
    DOUBLE_PAWN_PUSH = "double_pawn_push"
    EN_PASSANT = "en_passant"
    CASTLE_KINGSIDE = "castle_kingside"
    CASTLE_QUEENSIDE = "castle_queenside"
    PROMOTION = "promotion"


@dataclass(frozen=True)
class Move:
    """A single chess move.

    ``from_square``/``to_square`` are LERF indices (``a1 == 0`` ... ``h8 == 63``).
    ``promotion`` is one of ``'q'``/``'r'``/``'b'``/``'n'`` (case-insensitive;
    the correct case is derived from the moving side) or ``None``. ``flag`` is
    a hint used by ``apply_move``; callers may leave it as
    :attr:`MoveFlag.NORMAL` for ordinary moves and ``apply_move`` will still
    classify castling/double-push/en-passant itself.
    """

    from_square: Square
    to_square: Square
    promotion: Optional[str] = None
    flag: MoveFlag = MoveFlag.NORMAL

    def __post_init__(self) -> None:
        if not 0 <= self.from_square <= 63:
            raise ValueError(f"from_square out of range: {self.from_square}")
        if not 0 <= self.to_square <= 63:
            raise ValueError(f"to_square out of range: {self.to_square}")
        if self.promotion is not None:
            if self.promotion.lower() not in "qrbn":
                raise ValueError(f"invalid promotion piece: {self.promotion!r}")

    @classmethod
    def from_uci(cls, uci: str) -> "Move":
        """Parse a UCI move string like ``'e2e4'`` or ``'e7e8q'``."""
        if len(uci) not in (4, 5):
            raise ValueError(f"invalid UCI move: {uci!r}")
        from_sq = parse_square(uci[0:2])
        to_sq = parse_square(uci[2:4])
        promo = uci[4].lower() if len(uci) == 5 else None
        if promo is not None and promo not in "qrbn":
            raise ValueError(f"invalid promotion suffix in UCI move: {uci!r}")
        return cls(from_sq, to_sq, promo)

    def to_uci(self) -> str:
        """Render as a UCI move string (e.g. ``'e2e4'``, ``'e7e8q'``)."""
        base = square_name(self.from_square) + square_name(self.to_square)
        if self.promotion is not None:
            return base + self.promotion.lower()
        return base

    def __str__(self) -> str:
        return self.to_uci()


# ---------------------------------------------------------------------------
# Exceptions
# ---------------------------------------------------------------------------


class SideToMoveError(ValueError):
    """Raised when a move is made by the side that is not to move."""


class IllegalMoveError(ValueError):
    """Raised when a move is not legal in the current state."""


# ---------------------------------------------------------------------------
# Game state
# ---------------------------------------------------------------------------


#: FEN of the standard starting position. Matches ``ChessConstants.StartingFen``
#: in the C# domain so the two implementations describe the same game.
STARTING_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"

#: Castling rook origin squares, keyed by (color, kingside?).
_CASTLE_ROOK_FROM: dict[tuple[Color, bool], Square] = {
    (Color.WHITE, True): parse_square("h1"),
    (Color.WHITE, False): parse_square("a1"),
    (Color.BLACK, True): parse_square("h8"),
    (Color.BLACK, False): parse_square("a8"),
}

#: Castling rook destination squares, keyed by (color, kingside?).
_CASTLE_ROOK_TO: dict[tuple[Color, bool], Square] = {
    (Color.WHITE, True): parse_square("f1"),
    (Color.WHITE, False): parse_square("d1"),
    (Color.BLACK, True): parse_square("f8"),
    (Color.BLACK, False): parse_square("d8"),
}

#: King destination squares for castling, keyed by (color, kingside?).
_CASTLE_KING_TO: dict[tuple[Color, bool], Square] = {
    (Color.WHITE, True): parse_square("g1"),
    (Color.WHITE, False): parse_square("c1"),
    (Color.BLACK, True): parse_square("g8"),
    (Color.BLACK, False): parse_square("c8"),
}

#: Rook home squares that, when vacated or captured, revoke a castling right.
_HOME_ROOK_SQUARES: dict[Square, str] = {
    parse_square("a1"): "white_queenside",
    parse_square("h1"): "white_kingside",
    parse_square("a8"): "black_queenside",
    parse_square("h8"): "black_kingside",
}


@dataclass(frozen=True)
class GameState:
    """A complete, immutable chess position.

    Encodes all six FEN fields:

    * ``board`` — piece placement (first FEN field).
    * ``side_to_move`` — ``Color.WHITE`` or ``Color.BLACK`` (``'w'``/``'b'``).
    * ``castling`` — :class:`CastlingRights` (``KQkq``).
    * ``ep_square`` — en-passant target square index or ``None`` (``-``).
    * ``halfmove_clock`` — plies since last pawn move/capture (resets on them).
    * ``fullmove_number`` — starts at 1, increments after black's move.

    Because the dataclass is frozen, instances are hashable and safe to keep in
    a repetition set — the basis for threefold-repetition tracking.
    """

    board: Board
    side_to_move: Color
    castling: CastlingRights = field(default_factory=CastlingRights)
    ep_square: Optional[Square] = None
    halfmove_clock: int = 0
    fullmove_number: int = 1

    # --- construction -----------------------------------------------------

    @classmethod
    def from_fen(cls, fen: str) -> "GameState":
        """Parse a full FEN string into a :class:`GameState`."""
        fields_ = fen.split()
        if len(fields_) < 4 or len(fields_) > 6:
            raise ValueError(
                f"FEN must have 4-6 fields, got {len(fields_)}: {fen!r}"
            )
        placement = fields_[0]
        side = fields_[1]
        castling = fields_[2]
        ep = fields_[3]
        halfmove = fields_[4] if len(fields_) > 4 else "0"
        fullmove = fields_[5] if len(fields_) > 5 else "1"

        if side not in ("w", "b"):
            raise ValueError(f"invalid side-to-move field: {side!r}")
        color = Color.WHITE if side == "w" else Color.BLACK

        if ep == "-":
            ep_square: Optional[Square] = None
        else:
            ep_square = parse_square(ep)

        try:
            halfmove_clock = int(halfmove)
            fullmove_number = int(fullmove)
        except ValueError as exc:
            raise ValueError(f"invalid move counters in FEN: {fen!r}") from exc
        if halfmove_clock < 0 or fullmove_number < 1:
            raise ValueError(f"invalid move counters in FEN: {fen!r}")

        return cls(
            board=Board.from_fen_placement(placement),
            side_to_move=color,
            castling=CastlingRights.from_fen(castling),
            ep_square=ep_square,
            halfmove_clock=halfmove_clock,
            fullmove_number=fullmove_number,
        )

    # --- FEN / serialization ---------------------------------------------

    def to_fen(self) -> str:
        """Serialize to a full FEN string (round-trips through :meth:`from_fen`)."""
        side = self.side_to_move.fen_char
        castling = self.castling.to_fen()
        ep = "-" if self.ep_square is None else square_name(self.ep_square)
        return (
            f"{self.board.to_fen_placement()} {side} {castling} "
            f"{ep} {self.halfmove_clock} {self.fullmove_number}"
        )

    def repetition_key(self) -> str:
        """A stable string identifying this position for repetition tracking.

        For threefold-repetition we compare *positions*, not history: the side
        to move, piece placement, castling rights and (conservatively) the
        en-passant target. The halfmove clock and fullmove number are **not**
        part of a position's identity (a position can recur with different
        counters), so they are excluded. Including a pseudo-legal-only ep
        square can only *under*-count repetitions, which is the safe direction.
        """
        castling = self.castling.to_fen()
        ep = "-" if self.ep_square is None else square_name(self.ep_square)
        return (
            f"{self.board.to_fen_placement()} {self.side_to_move.fen_char} "
            f"{castling} {ep}"
        )

    def to_dict(self) -> dict:
        """Serialize to a plain dict (JSON-friendly)."""
        return {
            "board": self.board.to_fen_placement(),
            "side_to_move": self.side_to_move.value,
            "castling": self.castling.to_fen(),
            "ep_square": None
            if self.ep_square is None
            else square_name(self.ep_square),
            "halfmove_clock": self.halfmove_clock,
            "fullmove_number": self.fullmove_number,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "GameState":
        """Reconstruct a state produced by :meth:`to_dict`."""
        return cls.from_fen(
            f"{data['board']} {data['side_to_move']} {data['castling']} "
            f"{data.get('ep_square') or '-'} "
            f"{data.get('halfmove_clock', 0)} {data.get('fullmove_number', 1)}"
        )

    def __str__(self) -> str:
        return self.to_fen()


def starting_state() -> GameState:
    """The standard chess starting position."""
    return GameState.from_fen(STARTING_FEN)


# ---------------------------------------------------------------------------
# Move application
# ---------------------------------------------------------------------------


def _is_pawn(piece: Optional[str]) -> bool:
    return piece in ("P", "p")


def _promote_to(piece: str, promotion: Optional[str]) -> str:
    """Return the promoted piece char matching the side of ``piece``.

    ``promotion`` is normalized to lowercase; the result inherits the case of
    the moving pawn. Defaults to queen when ``promotion`` is ``None``.
    """
    if promotion is None:
        promotion = "q"
    promo = promotion.lower()
    if promo not in "qrbn":
        raise ValueError(f"invalid promotion piece: {promotion!r}")
    return promo.upper() if piece.isupper() else promo


def _is_capture(state: GameState, move: Move) -> bool:
    return state.board.piece_at(move.to_square) is not None


def _classify_move(state: GameState, move: Move) -> MoveFlag:
    """Infer the canonical flag for ``move`` when the caller left it NORMAL.

    Lets move generators emit plain from/to moves and still have
    castling/en-passant/double-push handled correctly.
    """
    piece = state.board.piece_at(move.from_square)
    if piece is None:
        return MoveFlag.NORMAL
    color = state.board.color_at(move.from_square)
    assert color is not None

    if piece.upper() == "K":
        if abs(move.to_square - move.from_square) == 2:
            return (
                MoveFlag.CASTLE_KINGSIDE
                if move.to_square > move.from_square
                else MoveFlag.CASTLE_QUEENSIDE
            )
        return MoveFlag.NORMAL

    if _is_pawn(piece):
        if (
            file_of(move.from_square) != file_of(move.to_square)
            and state.board.piece_at(move.to_square) is None
        ):
            return MoveFlag.EN_PASSANT
        if abs(rank_of(move.to_square) - rank_of(move.from_square)) == 2:
            return MoveFlag.DOUBLE_PAWN_PUSH
        if move.promotion is not None:
            return MoveFlag.PROMOTION
    return move.flag


def _check_squares_attacked_by(
    board: Board, squares: tuple[Square, ...], attacker: Color
) -> bool:
    """Return True if any square in ``squares`` is attacked by ``attacker``."""
    for target in squares:
        if _square_attacked(board, target, attacker):
            return True
    return False


def _square_attacked(board: Board, target: Square, attacker: Color) -> bool:
    """Whether ``attacker`` attacks ``target``. Compact and dependency-free."""
    tf, tr = file_of(target), rank_of(target)
    enemy_pawn = "P" if attacker is Color.WHITE else "p"
    enemy_knight = "N" if attacker is Color.WHITE else "n"
    enemy_king = "K" if attacker is Color.WHITE else "k"
    if attacker is Color.WHITE:
        bishop_ray, rook_ray, queen = "B", "R", "Q"
    else:
        bishop_ray, rook_ray, queen = "b", "r", "q"

    # Pawn attacks: a pawn on (f, r) attacks (f±1, r+dir) for its own color,
    # so it attacks ``target`` from (f±1, r-dir).
    pawn_dir = 1 if attacker is Color.WHITE else -1
    for df in (-1, 1):
        sf, sr = tf + df, tr - pawn_dir
        if 0 <= sf <= 7 and 0 <= sr <= 7:
            if board.piece_at(sr * 8 + sf) == enemy_pawn:
                return True

    # Knight attacks.
    for df, dr in (
        (1, 2), (2, 1), (2, -1), (1, -2),
        (-1, -2), (-2, -1), (-2, 1), (-1, 2),
    ):
        sf, sr = tf + df, tr + dr
        if 0 <= sf <= 7 and 0 <= sr <= 7:
            if board.piece_at(sr * 8 + sf) == enemy_knight:
                return True

    # King attacks.
    for df in (-1, 0, 1):
        for dr in (-1, 0, 1):
            if df == 0 and dr == 0:
                continue
            sf, sr = tf + df, tr + dr
            if 0 <= sf <= 7 and 0 <= sr <= 7:
                if board.piece_at(sr * 8 + sf) == enemy_king:
                    return True

    # Diagonal slider rays (bishop / queen).
    for df, dr in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        sf, sr = tf + df, tr + dr
        while 0 <= sf <= 7 and 0 <= sr <= 7:
            piece = board.piece_at(sr * 8 + sf)
            if piece is not None:
                if piece == bishop_ray or piece == queen:
                    return True
                break
            sf += df
            sr += dr

    # Orthogonal slider rays (rook / queen).
    for df, dr in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        sf, sr = tf + df, tr + dr
        while 0 <= sf <= 7 and 0 <= sr <= 7:
            piece = board.piece_at(sr * 8 + sf)
            if piece is not None:
                if piece == rook_ray or piece == queen:
                    return True
                break
            sf += df
            sr += dr

    return False


def _own_king_in_check(board: Board, color: Color) -> bool:
    king_sq = board.king_square(color)
    if king_sq is None:
        return False
    return _square_attacked(board, king_sq, color.opponent)


def _ray_clear(board: Board, frm: Square, to: Square) -> bool:
    """True if all strictly-between squares of the frm->to ray are empty."""
    ff, fr = file_of(frm), rank_of(frm)
    tf, tr = file_of(to), rank_of(to)
    df = (tf > ff) - (tf < ff)
    dr = (tr > fr) - (tr < fr)
    sf, sr = ff + df, fr + dr
    while (sf, sr) != (tf, tr):
        if board.piece_at(sr * 8 + sf) is not None:
            return False
        sf += df
        sr += dr
    return True


def _validate_castling(state: GameState, color: Color, flag: MoveFlag) -> None:
    kingside = flag is MoveFlag.CASTLE_KINGSIDE
    rights = state.castling
    if color is Color.WHITE:
        has_right = rights.white_kingside if kingside else rights.white_queenside
    else:
        has_right = rights.black_kingside if kingside else rights.black_queenside
    if not has_right:
        raise IllegalMoveError("no castling right for this side")

    board = state.board
    king_from = board.king_square(color)
    if king_from is None:
        raise IllegalMoveError("no king to castle")
    expected_king = parse_square("e1" if color is Color.WHITE else "e8")
    if king_from != expected_king:
        raise IllegalMoveError("king not on its home square")

    rook_from = _CASTLE_ROOK_FROM[(color, kingside)]
    rook_char = "R" if color is Color.WHITE else "r"
    if board.piece_at(rook_from) != rook_char:
        raise IllegalMoveError("castling rook not on its home square")

    king_to = _CASTLE_KING_TO[(color, kingside)]
    # Squares the king passes through (excluding start, including destination).
    step = 1 if king_to > king_from else -1
    king_path: list[Square] = []
    sq = king_from + step
    while sq != king_to + step:
        king_path.append(sq)
        sq += step

    # Squares between king and rook (exclusive) must be empty.
    rook_to = _CASTLE_ROOK_TO[(color, kingside)]
    rstep = 1 if rook_to > rook_from else -1
    rsq = rook_from + rstep
    while rsq != rook_to + rstep:
        if rsq != king_from and board.piece_at(rsq) is not None:
            raise IllegalMoveError("castling path blocked")
        rsq += rstep

    if _own_king_in_check(board, color):
        raise IllegalMoveError("cannot castle out of check")
    if _check_squares_attacked_by(board, tuple(king_path), color.opponent):
        raise IllegalMoveError("cannot castle through or into check")


def _apply_board_effects(state: GameState, move: Move, flag: MoveFlag) -> Board:
    """Apply the piece-placement effects of ``move``; return the new board.

    Pure board transformation (no state-level fields) used both for the real
    successor and for the self-check simulation.
    """
    board = state.board
    piece = board.piece_at(move.from_square)
    color = board.color_at(move.from_square)
    assert piece is not None and color is not None

    # Lift the moving piece from its origin.
    board = board.with_piece(move.from_square, None)

    if flag is MoveFlag.EN_PASSANT:
        cap_dir = -8 if color is Color.WHITE else 8
        captured_sq = move.to_square + cap_dir
        board = board.with_piece(captured_sq, None)
        board = board.with_piece(move.to_square, piece)
    elif flag in (MoveFlag.CASTLE_KINGSIDE, MoveFlag.CASTLE_QUEENSIDE):
        kingside = flag is MoveFlag.CASTLE_KINGSIDE
        king_to = _CASTLE_KING_TO[(color, kingside)]
        rook_from = _CASTLE_ROOK_FROM[(color, kingside)]
        rook_to = _CASTLE_ROOK_TO[(color, kingside)]
        rook = board.piece_at(rook_from)
        board = board.with_piece(king_to, piece)
        board = board.with_piece(rook_from, None)
        board = board.with_piece(rook_to, rook)
    elif flag is MoveFlag.PROMOTION:
        board = board.with_piece(move.to_square, _promote_to(piece, move.promotion))
    else:
        board = board.with_piece(move.to_square, piece)

    return board


def _update_castling_rights(
    rights: CastlingRights, board: Board, move: Move, flag: MoveFlag
) -> CastlingRights:
    """Compute castling rights after ``move``.

    Rights are lost when the king moves, when a rook moves off its home square,
    or when a rook is captured on its home square.
    """
    wk, wq, bk, bq = (
        rights.white_kingside,
        rights.white_queenside,
        rights.black_kingside,
        rights.black_queenside,
    )

    if flag in (MoveFlag.CASTLE_KINGSIDE, MoveFlag.CASTLE_QUEENSIDE):
        color = board.color_at(move.to_square)
        if color is Color.WHITE:
            wk = wq = False
        else:
            bk = bq = False
        return CastlingRights(wk, wq, bk, bq)

    moved_piece = board.piece_at(move.to_square)
    # King move removes both rights for that side.
    if moved_piece is not None and moved_piece.upper() == "K":
        if moved_piece.isupper():
            wk = wq = False
        else:
            bk = bq = False

    # A rook moving from, or any piece being captured on, a home rook square
    # removes that right.
    for sq in (move.from_square, move.to_square):
        attr = _HOME_ROOK_SQUARES.get(sq)
        if attr == "white_kingside":
            wk = False
        elif attr == "white_queenside":
            wq = False
        elif attr == "black_kingside":
            bk = False
        elif attr == "black_queenside":
            bq = False

    return CastlingRights(wk, wq, bk, bq)


def _en_passant_square_after(
    board: Board, move: Move, flag: MoveFlag
) -> Optional[Square]:
    """The ep target set by a double pawn push, else ``None``."""
    if flag is not MoveFlag.DOUBLE_PAWN_PUSH:
        return None
    moved = board.piece_at(move.to_square)
    if moved is None or moved.upper() != "P":
        return None
    color = Color.WHITE if moved.isupper() else Color.BLACK
    ep_rank = 2 if color is Color.WHITE else 5  # the skipped square's rank index
    ep_file = file_of(move.to_square)
    return ep_rank * 8 + ep_file


def _ensure_not_self_check(state: GameState, move: Move, flag: MoveFlag) -> None:
    """Simulate the board effect of ``move`` and reject if it self-checks."""
    color = state.side_to_move
    sim = _apply_board_effects(state, move, flag)
    if _own_king_in_check(sim, color):
        raise IllegalMoveError("move leaves own king in check")


def _validate_move_legality(state: GameState, move: Move, flag: MoveFlag) -> None:
    """Lightweight legality checks needed by ``apply_move``.

    ``apply_move`` produces a successor state; it is not the full legal-move
    generator (a downstream concern). But it must guarantee the successor is
    *legal*: refuse wrong-side moves, structurally impossible moves, castling
    violations, and moves that leave the mover's own king in check.
    """
    board = state.board
    piece = board.piece_at(move.from_square)
    if piece is None:
        raise IllegalMoveError(f"no piece on {square_name(move.from_square)}")

    color = board.color_at(move.from_square)
    assert color is not None
    if color is not state.side_to_move:
        raise SideToMoveError(
            f"it is {state.side_to_move.fen_char}'s move, "
            f"not {color.fen_char}'s"
        )

    captured = board.piece_at(move.to_square)
    if captured is not None and board.color_at(move.to_square) is color:
        raise IllegalMoveError(
            f"cannot capture own piece on {square_name(move.to_square)}"
        )

    upper = piece.upper()
    fr, tr = rank_of(move.from_square), rank_of(move.to_square)
    ff, tf = file_of(move.from_square), file_of(move.to_square)

    if upper == "P":
        forward = 1 if color is Color.WHITE else -1
        same_file = ff == tf
        advance = tr - fr
        if same_file:
            if captured is not None:
                raise IllegalMoveError("pawns cannot capture straight ahead")
            if advance == forward:
                pass
            elif advance == 2 * forward:
                start_rank = 1 if color is Color.WHITE else 6
                if fr != start_rank:
                    raise IllegalMoveError("double pawn push from wrong rank")
                if board.piece_at(move.from_square + forward * 8) is not None:
                    raise IllegalMoveError("double pawn push blocked")
            else:
                raise IllegalMoveError("illegal pawn advance")
        else:
            if abs(tf - ff) != 1 or advance != forward:
                raise IllegalMoveError("illegal pawn capture geometry")
            if flag is MoveFlag.EN_PASSANT:
                if state.ep_square is None or move.to_square != state.ep_square:
                    raise IllegalMoveError("no en-passant target for this move")
            elif captured is None:
                raise IllegalMoveError("pawn diagonal move with no capture")
        promo_rank = 7 if color is Color.WHITE else 0
        is_promoting = tr == promo_rank
        if is_promoting and move.promotion is None:
            raise IllegalMoveError("promotion piece required")
        if not is_promoting and move.promotion is not None:
            raise IllegalMoveError("promotion piece supplied for non-promotion")
    elif upper == "N":
        df, dr = abs(tf - ff), abs(tr - fr)
        if (df, dr) not in ((1, 2), (2, 1)):
            raise IllegalMoveError("illegal knight move")
    elif upper == "B":
        if (abs(tf - ff) != abs(tr - fr)) or (tf == ff and tr == fr):
            raise IllegalMoveError("illegal bishop move")
        if not _ray_clear(board, move.from_square, move.to_square):
            raise IllegalMoveError("bishop move blocked")
    elif upper == "R":
        if tf != ff and tr != fr:
            raise IllegalMoveError("illegal rook move")
        if not _ray_clear(board, move.from_square, move.to_square):
            raise IllegalMoveError("rook move blocked")
    elif upper == "Q":
        diag = abs(tf - ff) == abs(tr - fr)
        orth = tf == ff or tr == fr
        if (not diag and not orth) or (tf == ff and tr == fr):
            raise IllegalMoveError("illegal queen move")
        if not _ray_clear(board, move.from_square, move.to_square):
            raise IllegalMoveError("queen move blocked")
    elif upper == "K":
        if flag in (MoveFlag.CASTLE_KINGSIDE, MoveFlag.CASTLE_QUEENSIDE):
            _validate_castling(state, color, flag)
        else:
            if max(abs(tf - ff), abs(tr - fr)) != 1:
                raise IllegalMoveError("illegal king move")
    else:  # pragma: no cover - exhaustive over FEN piece chars
        raise IllegalMoveError(f"unknown piece: {piece!r}")

    _ensure_not_self_check(state, move, flag)


def apply_move(state: GameState, move: Move) -> GameState:
    """Apply ``move`` to ``state`` and return a **new** successor state.

    The input ``state`` is never mutated (it is a frozen dataclass, and the
    successor is built from fresh components). The returned state is guaranteed
    legal: the mover is the side to move, the move is geometrically legal for
    the piece, castling satisfies all of its preconditions (rights, empty
    path, not through/into/out of check), and the mover's own king is not left
    in check.

    Raises
    ------
    SideToMoveError
        If the piece on ``move.from_square`` does not belong to the side to move.
    IllegalMoveError
        If the move is structurally illegal, leaves the mover in check, or is
        an invalid castling/en-passant/promotion.
    """
    flag = _classify_move(state, move) if move.flag is MoveFlag.NORMAL else move.flag
    if flag is MoveFlag.NORMAL and move.promotion is not None:
        flag = MoveFlag.PROMOTION

    _validate_move_legality(state, move, flag)

    board = state.board
    color = state.side_to_move
    captured = _is_capture(state, move) or flag is MoveFlag.EN_PASSANT
    moved_piece = board.piece_at(move.from_square)
    assert moved_piece is not None
    is_pawn_move = moved_piece.upper() == "P"

    new_board = _apply_board_effects(state, move, flag)
    new_castling = _update_castling_rights(state.castling, new_board, move, flag)
    new_ep = _en_passant_square_after(new_board, move, flag)

    new_halfmove = 0 if (is_pawn_move or captured) else state.halfmove_clock + 1
    new_side = color.opponent
    new_fullmove = state.fullmove_number + (1 if color is Color.BLACK else 0)

    return GameState(
        board=new_board,
        side_to_move=new_side,
        castling=new_castling,
        ep_square=new_ep,
        halfmove_clock=new_halfmove,
        fullmove_number=new_fullmove,
    )
