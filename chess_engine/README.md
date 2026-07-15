# chess_engine — Board Representation & Game State Model

A pure-Python, stdlib-only substrate for chess board representation and game
state. It is the foundation for move generation, terminal-state detection, and
AI integration.

This module mirrors the terminology of the C# `ChessMvp.Domain` layer
(`ChessConstants.StartingFen`, `PlayerColor`, `PromotionPieceType`, the
FEN-in/FEN-out statelessness of `IChessRulesEngine`) so the two implementations
describe the same game.

## Files

| File | Contents |
| --- | --- |
| `chess_engine/board.py` | `Board` (immutable piece-placement grid), `Color`, square helpers, FEN placement (de)serialization. |
| `chess_engine/state.py` | `GameState` (full FEN state), `CastlingRights`, `Move`/`MoveFlag`, exceptions, and `apply_move`. |
| `chess_engine/__init__.py` | Public API re-exports. |

## Acceptance criteria mapping

- **Board representation encodes piece placement, side to move, castling
  rights, en-passant target, halfmove clock, and fullmove number.**
  `GameState` is a frozen dataclass holding `board` (placement) plus
  `side_to_move`, `castling` (`CastlingRights`), `ep_square`,
  `halfmove_clock`, and `fullmove_number` — all six FEN fields. Round-trips
  through `from_fen` / `to_fen`.

- **`apply_move(state, move)` produces a legal successor state without
  mutating the input.**
  `Board` and `GameState` are frozen dataclasses; `apply_move` builds a brand
  new `GameState` from fresh components and never touches its argument. It
  validates side-to-move, per-piece geometry, castling preconditions (rights,
  empty path, not through/into/out of check), promotion, en-passant, and that
  the move does not leave the mover's own king in check.

- **State model supports serialization sufficient for threefold-repetition
  tracking.**
  `GameState` is hashable (frozen dataclass) and exposes `repetition_key()` —
  a stable position identity (placement + side + castling + ep, **excluding**
  the halfmove/fullmove counters that are not part of a position's identity)
  plus `to_fen()`/`to_dict()`/`from_dict()` for full serialization.

## Usage

```python
from chess_engine import GameState, Move, apply_move, starting_state

state = starting_state()
state = apply_move(state, Move.from_uci("e2e4"))
state = apply_move(state, Move.from_uci("e7e5"))
print(state.to_fen())
# rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2

# Threefold-repetition tracking
seen = {state.repetition_key(): 1}
```

## Tests

```
python3 -m pytest tests/ -q
```
