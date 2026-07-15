"""Tests for chess_engine.board."""

from __future__ import annotations

import pytest

from chess_engine.board import (
    Board,
    Color,
    FILES,
    RANKS,
    file_of,
    parse_square,
    rank_of,
    square_name,
)


def test_square_name_round_trip():
    for sq in range(64):
        name = square_name(sq)
        assert parse_square(name) == sq
    assert square_name(0) == "a1"
    assert square_name(7) == "h1"
    assert square_name(56) == "a8"
    assert square_name(63) == "h8"


def test_file_and_rank_of():
    assert file_of(parse_square("a1")) == 0
    assert file_of(parse_square("h1")) == 7
    assert rank_of(parse_square("a1")) == 0
    assert rank_of(parse_square("a8")) == 7


def test_parse_square_invalid():
    with pytest.raises(ValueError):
        parse_square("z9")
    with pytest.raises(ValueError):
        parse_square("a")
    with pytest.raises(ValueError):
        parse_square("a12")


def test_color_properties():
    assert Color.WHITE.is_white
    assert not Color.BLACK.is_white
    assert Color.WHITE.opponent is Color.BLACK
    assert Color.BLACK.opponent is Color.WHITE
    assert Color.WHITE.fen_char == "w"
    assert Color.BLACK.fen_char == "b"


def test_board_size_validation():
    with pytest.raises(ValueError):
        Board(tuple(None for _ in range(63)))


def test_empty_board():
    b = Board.empty()
    for sq in range(64):
        assert b.piece_at(sq) is None
    assert b.to_fen_placement() == "8/8/8/8/8/8/8/8"


def test_starting_placement_round_trip():
    placement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"
    b = Board.from_fen_placement(placement)
    assert b.to_fen_placement() == placement
    # White king on e1.
    assert b.piece_at(parse_square("e1")) == "K"
    # Black king on e8.
    assert b.piece_at(parse_square("e8")) == "k"
    # Empty e4.
    assert b.piece_at(parse_square("e4")) is None


def test_with_piece_immutability():
    b = Board.empty()
    b2 = b.with_piece(parse_square("e4"), "P")
    # Original unchanged.
    assert b.piece_at(parse_square("e4")) is None
    assert b2.piece_at(parse_square("e4")) == "P"
    # Clear a piece.
    b3 = b2.with_piece(parse_square("e4"), None)
    assert b3.piece_at(parse_square("e4")) is None


def test_with_piece_validation():
    b = Board.empty()
    with pytest.raises(ValueError):
        b.with_piece(64, "P")
    with pytest.raises(ValueError):
        b.with_piece(0, "X")


def test_king_square():
    placement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"
    b = Board.from_fen_placement(placement)
    assert b.king_square(Color.WHITE) == parse_square("e1")
    assert b.king_square(Color.BLACK) == parse_square("e8")


def test_king_square_absent():
    b = Board.empty()
    assert b.king_square(Color.WHITE) is None


def test_color_at():
    placement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"
    b = Board.from_fen_placement(placement)
    assert b.color_at(parse_square("e1")) is Color.WHITE
    assert b.color_at(parse_square("e8")) is Color.BLACK
    assert b.color_at(parse_square("e4")) is None


def test_fen_placement_with_empty_squares():
    placement = "8/8/8/3P4/8/8/8/8"
    b = Board.from_fen_placement(placement)
    assert b.piece_at(parse_square("d5")) == "P"
    assert b.to_fen_placement() == placement


def test_fen_placement_invalid_ranks():
    with pytest.raises(ValueError):
        Board.from_fen_placement("8/8/8/8/8/8/8")  # 7 ranks
    with pytest.raises(ValueError):
        Board.from_fen_placement("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR/8")
    with pytest.raises(ValueError):
        Board.from_fen_placement("9/8/8/8/8/8/8/8")  # 9 files
    with pytest.raises(ValueError):
        Board.from_fen_placement("8/8/8/8/8/8/8/7")  # 7 files
    with pytest.raises(ValueError):
        Board.from_fen_placement("8/8/8/8/8/8/8/x7")  # bad char


def test_board_iter():
    b = Board.from_fen_placement("8/8/8/8/8/8/8/P7")
    filled = [(sq, piece) for sq, piece in b if piece is not None]
    assert filled == [(parse_square("a1"), "P")]
