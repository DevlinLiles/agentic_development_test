"""Tests for chess_engine.state — game state model and apply_move."""

from __future__ import annotations

import pytest

from chess_engine.board import Board, Color, parse_square, square_name
from chess_engine.state import (
    CastlingRights,
    GameState,
    IllegalMoveError,
    Move,
    MoveFlag,
    SideToMoveError,
    apply_move,
    starting_state,
)

START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"


# ---------------------------------------------------------------------------
# FEN parsing / serialization
# ---------------------------------------------------------------------------


def test_starting_state_fen():
    s = starting_state()
    assert s.to_fen() == START
    assert s.side_to_move is Color.WHITE
    assert s.halfmove_clock == 0
    assert s.fullmove_number == 1
    assert s.ep_square is None
    assert s.castling.to_fen() == "KQkq"


def test_fen_round_trip():
    fens = [
        START,
        "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
        "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3",
        "8/8/8/8/8/8/8/4K2k w - - 0 1",
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
    ]
    for fen in fens:
        assert GameState.from_fen(fen).to_fen() == fen


def test_fen_with_partial_counters():
    # FEN may omit the last one or two fields.
    s = GameState.from_fen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -")
    assert s.halfmove_clock == 0
    assert s.fullmove_number == 1


def test_fen_invalid_field_count():
    with pytest.raises(ValueError):
        GameState.from_fen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w")
    with pytest.raises(ValueError):
        GameState.from_fen("8/8/8/8/8/8/8/8 w KQkq - 0 1 2 3")


def test_fen_invalid_side():
    with pytest.raises(ValueError):
        GameState.from_fen("8/8/8/8/8/8/8/4K2k x - - 0 1")


def test_castling_rights_from_fen():
    assert CastlingRights.from_fen("-").to_fen() == "-"
    assert CastlingRights.from_fen("KQkq").to_fen() == "KQkq"
    assert CastlingRights.from_fen("kq").to_fen() == "kq"
    cr = CastlingRights.from_fen("Kq")
    assert cr.white_kingside and cr.black_queenside
    assert not cr.white_queenside and not cr.black_kingside
    with pytest.raises(ValueError):
        CastlingRights.from_fen("X")


# ---------------------------------------------------------------------------
# Serialization for repetition tracking
# ---------------------------------------------------------------------------


def test_repetition_key_excludes_counters():
    s1 = GameState.from_fen("8/8/8/8/8/8/8/4K2k w - - 0 1")
    s2 = GameState.from_fen("8/8/8/8/8/8/8/4K2k w - - 5 7")
    # Same position, different counters -> same repetition key.
    assert s1.repetition_key() == s2.repetition_key()


def test_repetition_key_differs_by_side_to_move():
    s1 = GameState.from_fen("8/8/8/8/8/8/8/4K2k w - - 0 1")
    s2 = GameState.from_fen("8/8/8/8/8/8/8/4K2k b - - 0 1")
    assert s1.repetition_key() != s2.repetition_key()


def test_repetition_key_differs_by_placement():
    s1 = GameState.from_fen("8/8/8/8/8/8/8/4K2k w - - 0 1")
    s2 = GameState.from_fen("8/8/8/8/8/8/8/3K3k w - - 0 1")
    assert s1.repetition_key() != s2.repetition_key()


def test_gamestate_is_hashable():
    s = starting_state()
    assert hash(s) == hash(s)
    # Usable in a set (threefold-repetition set).
    seen = {s}
    assert s in seen


def test_to_dict_round_trip():
    s = GameState.from_fen(
        "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"
    )
    d = s.to_dict()
    assert d == {
        "board": "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR",
        "side_to_move": "b",
        "castling": "KQkq",
        "ep_square": "e3",
        "halfmove_clock": 0,
        "fullmove_number": 1,
    }
    assert GameState.from_dict(d).to_fen() == s.to_fen()


def test_to_dict_ep_none():
    s = GameState.from_fen("8/8/8/8/8/8/8/4K2k w - - 0 1")
    assert s.to_dict()["ep_square"] is None


# ---------------------------------------------------------------------------
# Immutability of apply_move
# ---------------------------------------------------------------------------


def test_apply_move_does_not_mutate_input():
    s = starting_state()
    original_fen = s.to_fen()
    move = Move.from_uci("e2e4")
    s2 = apply_move(s, move)
    # Input unchanged.
    assert s.to_fen() == original_fen
    # New object, different state.
    assert s2 is not s
    assert s2.to_fen() != original_fen


def test_apply_move_returns_new_state_objects():
    s = starting_state()
    s2 = apply_move(s, Move.from_uci("e2e4"))
    assert s2.board is not s.board
    assert s2.castling is not s.castling


# ---------------------------------------------------------------------------
# Basic move application
# ---------------------------------------------------------------------------


def test_pawn_double_push_sets_ep_and_resets_clock():
    s = starting_state()
    s2 = apply_move(s, Move.from_uci("e2e4"))
    assert s2.ep_square == parse_square("e3")
    assert s2.side_to_move is Color.BLACK
    # Pawn move resets halfmove clock.
    assert s2.halfmove_clock == 0
    assert s2.fullmove_number == 1  # only increments after black's move


def test_pawn_single_push():
    s = starting_state()
    s2 = apply_move(s, Move.from_uci("e2e3"))
    assert s2.ep_square is None
    assert s2.halfmove_clock == 0


def test_knight_move_increments_halfmove():
    s = starting_state()
    s2 = apply_move(s, Move.from_uci("g1f3"))
    assert s2.ep_square is None
    assert s2.halfmove_clock == 1
    assert s2.fullmove_number == 1


def test_black_move_increments_fullmove():
    s = GameState.from_fen(
        "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"
    )
    s2 = apply_move(s, Move.from_uci("e7e5"))
    assert s2.side_to_move is Color.WHITE
    assert s2.fullmove_number == 2
    assert s2.halfmove_clock == 0


def test_capture_resets_halfmove():
    # White pawn e4 captures black pawn d5.
    s = GameState.from_fen("4k3/8/8/3p4/4P3/8/8/4K3 w - - 10 20")
    s2 = apply_move(s, Move.from_uci("e4d5"))
    assert s2.halfmove_clock == 0
    assert s2.board.piece_at(parse_square("d5")) == "P"


# ---------------------------------------------------------------------------
# En passant
# ---------------------------------------------------------------------------


def test_en_passant_capture():
    # White pawn e5, black just played d7-d5 so ep target is d6.
    s = GameState.from_fen(
        "rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3"
    )
    s2 = apply_move(s, Move.from_uci("e5d6"))
    # Captured pawn removed from d5, white pawn on d6.
    assert s2.board.piece_at(parse_square("d6")) == "P"
    assert s2.board.piece_at(parse_square("d5")) is None
    assert s2.board.piece_at(parse_square("e5")) is None
    assert s2.halfmove_clock == 0
    assert s2.ep_square is None


def test_en_passant_wrong_target_rejected():
    s = GameState.from_fen(
        "rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3"
    )
    # No ep target on f6.
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e5f6"))


def test_en_passant_no_target_rejected():
    s = GameState.from_fen(
        "rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3"
    )
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e5d6"))


# ---------------------------------------------------------------------------
# Castling
# ---------------------------------------------------------------------------


def test_castle_kingside_white():
    s = GameState.from_fen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQK2R w KQkq - 0 1")
    s2 = apply_move(s, Move.from_uci("e1g1"))
    assert s2.board.piece_at(parse_square("g1")) == "K"
    assert s2.board.piece_at(parse_square("f1")) == "R"
    assert s2.board.piece_at(parse_square("e1")) is None
    assert s2.board.piece_at(parse_square("h1")) is None
    assert not s2.castling.white_kingside
    assert not s2.castling.white_queenside
    # Black rights untouched.
    assert s2.castling.black_kingside
    assert s2.castling.black_queenside


def test_castle_queenside_white():
    s = GameState.from_fen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/R3KBNR w KQkq - 0 1")
    s2 = apply_move(s, Move.from_uci("e1c1"))
    assert s2.board.piece_at(parse_square("c1")) == "K"
    assert s2.board.piece_at(parse_square("d1")) == "R"
    assert s2.board.piece_at(parse_square("a1")) is None
    assert not s2.castling.white_kingside
    assert not s2.castling.white_queenside


def test_castle_black_kingside():
    s = GameState.from_fen("rnbqk2r/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1")
    s2 = apply_move(s, Move.from_uci("e8g8"))
    assert s2.board.piece_at(parse_square("g8")) == "k"
    assert s2.board.piece_at(parse_square("f8")) == "r"
    assert s2.fullmove_number == 2
    assert not s2.castling.black_kingside
    assert not s2.castling.black_queenside


def test_castle_no_right_rejected():
    s = GameState.from_fen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQK2R w kq - 0 1")
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e1g1"))


def test_castle_through_check_rejected():
    # Black rook on e8 gives check along e-file; white cannot castle out of check.
    s = GameState.from_fen("4r3/8/8/8/8/8/8/R3K2R w KQ - 0 1")
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e1g1"))


def test_castle_into_check_rejected():
    # Black rook on g8 attacks g1 (king's destination).
    s = GameState.from_fen("6r1/8/8/8/8/8/8/R3K2R w KQ - 0 1")
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e1g1"))


def test_castle_path_blocked_rejected():
    # Bishop on f1 blocks the kingside castling path (king e1 -> g1).
    s = GameState.from_fen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKB1R w KQkq - 0 1")
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e1g1"))


def test_castling_right_lost_when_rook_moves():
    # h-file clear (h2 empty) so the rook can move off h1.
    s = GameState.from_fen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPP1/RNBQK2R w KQkq - 0 1")
    s2 = apply_move(s, Move.from_uci("h1h2"))
    assert s2.board.piece_at(parse_square("h2")) == "R"
    assert not s2.castling.white_kingside
    assert s2.castling.white_queenside


def test_castling_right_lost_when_rook_captured():
    # Black rook on h4 captures white h1 rook (h-file clear) -> white loses K.
    s = GameState.from_fen(
        "rnbqkbnr/pppppppp/8/8/7r/8/PPPPPPP1/RNBQK2R b KQkq - 0 1"
    )
    s2 = apply_move(s, Move.from_uci("h4h1"))
    assert s2.board.piece_at(parse_square("h1")) == "r"
    assert not s2.castling.white_kingside


# ---------------------------------------------------------------------------
# Promotion
# ---------------------------------------------------------------------------


def test_promotion_to_queen():
    s = GameState.from_fen("8/4P3/8/8/8/8/8/4k2K w - - 0 1")
    s2 = apply_move(s, Move.from_uci("e7e8q"))
    assert s2.board.piece_at(parse_square("e8")) == "Q"
    assert s2.halfmove_clock == 0


def test_promotion_to_knight_uppercase_input():
    s = GameState.from_fen("8/4P3/8/8/8/8/8/4k2K w - - 0 1")
    s2 = apply_move(s, Move.from_uci("e7e8N"))
    assert s2.board.piece_at(parse_square("e8")) == "N"


def test_promotion_black_lowercase_output():
    s = GameState.from_fen("4k2K/8/8/8/8/8/4p3/8 b - - 0 1")
    s2 = apply_move(s, Move.from_uci("e2e1r"))
    assert s2.board.piece_at(parse_square("e1")) == "r"


def test_promotion_required():
    s = GameState.from_fen("8/4P3/8/8/8/8/8/4k2K w - - 0 1")
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e7e8"))


def test_promotion_unnecessary_rejected():
    s = GameState.from_fen("8/8/4P3/8/8/8/8/4k2K w - - 0 1")
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e3e4q"))


def test_promotion_with_capture():
    s = GameState.from_fen("3r4/4P3/8/8/8/8/8/4k2K w - - 0 1")
    s2 = apply_move(s, Move.from_uci("e7d8q"))
    assert s2.board.piece_at(parse_square("d8")) == "Q"
    assert s2.halfmove_clock == 0


# ---------------------------------------------------------------------------
# Legality: self-check and wrong side
# ---------------------------------------------------------------------------


def test_wrong_side_rejected():
    s = starting_state()  # white to move
    with pytest.raises(SideToMoveError):
        apply_move(s, Move.from_uci("e7e5"))  # black pawn


def test_move_leaves_king_in_check_rejected():
    # White king on e1, black rook on e8 pinning the e-file; bishop on e2 must
    # not move off the e-file (it would expose the king).
    s = GameState.from_fen("4r3/8/8/8/8/8/4B3/4K3 w - - 0 1")
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e2d3"))  # exposes king to rook


def test_move_blocks_check_allowed():
    # Bishop d2 interposes on e3 to block the e-file check from the rook on e8.
    s = GameState.from_fen("4r3/8/8/8/8/8/3B4/4K3 w - - 0 1")
    s2 = apply_move(s, Move.from_uci("d2e3"))
    assert s2.board.piece_at(parse_square("e3")) == "B"


def test_king_moves_out_of_check():
    s = GameState.from_fen("4r3/8/8/8/8/8/8/4K3 w - - 0 1")
    s2 = apply_move(s, Move.from_uci("e1d2"))
    assert s2.board.piece_at(parse_square("d2")) == "K"


def test_king_moves_into_check_rejected():
    s = GameState.from_fen("4r3/8/8/8/8/8/8/4K3 w - - 0 1")
    # King to e2 stays on the rook's file -> still in check.
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e1e2"))


def test_cannot_capture_own_piece():
    s = starting_state()
    # White bishop f1 onto pawn e2 is capturing an own piece.
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("f1e2"))


def test_no_piece_on_from_square():
    s = starting_state()
    with pytest.raises(IllegalMoveError):
        apply_move(s, Move.from_uci("e4e5"))


# ---------------------------------------------------------------------------
# Move parsing
# ---------------------------------------------------------------------------


def test_move_from_uci():
    m = Move.from_uci("e2e4")
    assert m.from_square == parse_square("e2")
    assert m.to_square == parse_square("e4")
    assert m.promotion is None
    assert m.to_uci() == "e2e4"


def test_move_from_uci_promotion():
    m = Move.from_uci("e7e8q")
    assert m.promotion == "q"
    assert m.to_uci() == "e7e8q"


def test_move_from_uci_invalid():
    with pytest.raises(ValueError):
        Move.from_uci("e2e4xx")
    with pytest.raises(ValueError):
        Move.from_uci("z9e4")
    with pytest.raises(ValueError):
        Move.from_uci("e2e4z")


def test_move_is_frozen():
    m = Move.from_uci("e2e4")
    with pytest.raises(Exception):
        m.from_square = 0  # type: ignore[misc]


# ---------------------------------------------------------------------------
# Full sequence
# ---------------------------------------------------------------------------


def test_apply_move_sequence_preserves_immutability():
    s = starting_state()
    moves = ["e2e4", "e7e5", "g1f3", "b8c6", "f1c4"]
    current = s
    original_fens = [s.to_fen()]
    for uci in moves:
        nxt = apply_move(current, Move.from_uci(uci))
        # Previous states are untouched.
        assert current.to_fen() == original_fens[-1]
        original_fens.append(nxt.to_fen())
        current = nxt
    # Final position after 1.e4 e5 2.Nf3 Nc6 3.Bc4 (Italian game).
    # Halfmove clock: e4(0) e5(0) Nf3(1) Nc6(2) Bc4(3).
    assert current.to_fen() == (
        "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3"
    )


def test_repetition_set_accumulation():
    """Simulate building a threefold-repetition tracker over a knight shuffle.

    1.Nf3 Nf6 2.Ng1 Ng8 3.Nf3 Nf6 4.Ng1 Ng8 returns to the start position
    three times (including the initial one), exercising repetition tracking.
    """
    s = starting_state()
    seen: dict[str, int] = {}
    seen[s.repetition_key()] = 1
    current = s
    moves = [
        "g1f3", "g8f6", "f3g1", "f6g8",  # back to start (2nd occurrence)
        "g1f3", "g8f6", "f3g1", "f6g8",  # back to start (3rd occurrence)
    ]
    for uci in moves:
        current = apply_move(current, Move.from_uci(uci))
        key = current.repetition_key()
        seen[key] = seen.get(key, 0) + 1
    # The start position recurred three times total (threefold).
    assert seen[s.repetition_key()] == 3
