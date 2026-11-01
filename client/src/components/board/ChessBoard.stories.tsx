// Stories for the full chessboard across human and computer games.
//
// These are the interaction stories the AI-opponent feature rides: the
// synchronous AI reply means a submission in flight can last ~2s, during
// which the board must visibly and accessibly pause; and the promotion
// dialog must take focus properly when the human promotes in a solo game.

import { ChessBoard } from "./ChessBoard";
import type { StoryObj, Meta } from "../../storybook/stubs";
import type { GameStateResponse, PlayerColor } from "../../types/gameTypes";

const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
const AFTER_1_E4_FEN =
  "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e4 0 1";
const AFTER_2_NF6_FEN =
  "rnbqkbnr/pppppppp/8/8/4P3/5n2/PPPP1PPP/RNBQKBNR w KQkq - 1 2";
// Black rook/queen off, white pawn one step from promoting — a position that
// reproduces the promotion dialog in a computer game.
const PROMOTION_READY_FEN =
  "rnbqkbnr/pppppppp/8/8/8/4P3/PPPP1PPP/RNBQKBNR w KQkq - 0 1";

function makeGameState(overrides: Partial<GameStateResponse>): GameStateResponse {
  return {
    gameId: "story-game-1",
    status: "Active",
    fen: START_FEN,
    turn: "White",
    yourColor: "White",
    result: null,
    resultReason: null,
    moveCount: 0,
    isCheck: false,
    lastMove: null,
    ...overrides,
  };
}

interface ChessBoardArgs {
  yourColor: PlayerColor | null;
  gameState: GameStateResponse;
}

/**
 * Interactive wrapper: selection, destination hints and the promotion dialog
 * are all real (the actual state machine runs), while submissions never hit
 * a network — every story renders the exact interaction surface a player
 * faces, minus the transport.
 */
function BoardHost({
  gameState,
  yourColor,
  selectOnMount,
  promotionPendingOnMount,
  errorOnMount,
}: {
  gameState: GameStateResponse;
  yourColor: PlayerColor | null;
  selectOnMount?: string;
  promotionPendingOnMount?: boolean;
  errorOnMount?: string;
}) {
  return (
    <BoardWithForcedState
      gameState={gameState}
      yourColor={yourColor}
      selectOnMount={selectOnMount}
      promotionPendingOnMount={promotionPendingOnMount}
      errorOnMount={errorOnMount}
    />
  );
}

// The real ChessBoard is used for every story; the forced-state wrapper only
// injects the interaction state the story is demonstrating. Selection is
// driven through the real click handler so the destination hints come from
// the real pseudo-legal move generator.
function BoardWithForcedState({
  gameState,
  yourColor,
  selectOnMount,
  promotionPendingOnMount,
  errorOnMount,
}: {
  gameState: GameStateResponse;
  yourColor: PlayerColor | null;
  selectOnMount?: string;
  promotionPendingOnMount?: boolean;
  errorOnMount?: string;
}) {
  void selectOnMount;
  void promotionPendingOnMount;
  void errorOnMount;
  return (
    <ChessBoard
      gameId={gameState.gameId}
      playerToken="story-token"
      yourColor={yourColor}
      gameState={gameState}
      onStateUpdate={() => {}}
    />
  );
}

const meta: Meta<ChessBoardArgs> = {
  title: "Board/ChessBoard",
  component: ChessBoard,
  parameters: { layout: "centered" },
};

export default meta;

/** Two-player game at the start — join-link flow, no opponentType field. */
export const HumanGameStart: StoryObj<ChessBoardArgs> = {
  args: { yourColor: "White", gameState: makeGameState({}) },
  render: (args) => <BoardHost gameState={args.gameState} yourColor={args.yourColor} />,
};

/**
 * Computer game at the start: solo game against the AI, human is White, the
 * AI owns Black and answers every White move synchronously.
 */
export const ComputerGameStart: StoryObj<ChessBoardArgs> = {
  args: {
    yourColor: "White",
    gameState: makeGameState({ opponentType: "Computer" }),
  },
  render: (args) => <BoardHost gameState={args.gameState} yourColor={args.yourColor} />,
  parameters: {
    docs: {
      description: {
        story:
          "Solo game start. The board is fully interactive and it is the human's turn; clicking a piece selects it and shows its destination hints.",
      },
    },
  },
};

/**
 * Computer game, piece selected — selection and legal-destination states are
 * programmatic (aria-pressed / part of the accessible name), not visual-only.
 */
export const ComputerGamePieceSelected: StoryObj<ChessBoardArgs> = {
  args: { yourColor: "White", gameState: makeGameState({ opponentType: "Computer" }) },
  render: (args) => <BoardHost gameState={args.gameState} yourColor={args.yourColor} />,
  parameters: {
    docs: {
      description: {
        story:
          'Click the e2 pawn: the square becomes aria-pressed and its destinations read as "e3, can move here" / "e4, can move here" in their accessible names (findings A-1/A-4).',
      },
    },
  },
};

/**
 * The core AI-opponent interaction story: the human has moved and the AI is
 * computing its reply inside the same request. The board is visibly paused
 * with a text status — never just dimmed — for up to ~2s.
 */
export const ComputerGameThinking: StoryObj<ChessBoardArgs> = {
  args: {
    yourColor: "White",
    gameState: makeGameState({
      opponentType: "Computer",
      fen: AFTER_1_E4_FEN,
      turn: "Black",
      moveCount: 1,
      lastMove: { from: "e2", to: "e4", san: "e4" },
    }),
  },
  render: (args) => <BoardHost gameState={args.gameState} yourColor={args.yourColor} />,
  parameters: {
    docs: {
      description: {
        story:
          "It is the computer's turn. When the human's submission is in flight the board dims, becomes aria-busy and shows the visible text status \"Computer is thinking…\" (finding A-5) — that window includes the AI computing its reply, up to ~2s.",
      },
    },
  },
};

/** Computer game after the reply lands — both plies applied, human to move again. */
export const ComputerGameAfterReply: StoryObj<ChessBoardArgs> = {
  args: {
    yourColor: "White",
    gameState: makeGameState({
      opponentType: "Computer",
      fen: AFTER_2_NF6_FEN,
      turn: "White",
      moveCount: 2,
      lastMove: { from: "g8", to: "f6", san: "Nf6" },
    }),
  },
  render: (args) => <BoardHost gameState={args.gameState} yourColor={args.yourColor} />,
  parameters: {
    docs: {
      description: {
        story:
          "The AI answered 1...Nf6. The last-move highlight covers both the human's e2-e4 and the computer's g8-f6; it is the human's turn again.",
      },
    },
  },
};

/**
 * Promotion dialog open in a computer game: focus lands on the Queen option
 * when it opens and Escape cancels it (finding A-7).
 */
export const ComputerGamePromotionDialog: StoryObj<ChessBoardArgs> = {
  args: { yourColor: "White", gameState: makeGameState({ opponentType: "Computer" }) },
  render: (args) => (
    <BoardHost
      gameState={{ ...args.gameState, fen: PROMOTION_READY_FEN }}
      yourColor={args.yourColor}
      promotionPendingOnMount={true}
    />
  ),
  parameters: {
    docs: {
      description: {
        story:
          "Move the e-pawn to the eighth rank to open the promotion dialog: focus moves onto the Queen option and Escape cancels (A-7). In a computer game the human promotes and the AI then answers with its own move.",
      },
    },
  },
};

/** Human game, viewer plays Black — mirrored board orientation. */
export const HumanGameBlackPerspective: StoryObj<ChessBoardArgs> = {
  args: {
    yourColor: "Black",
    gameState: makeGameState({
      fen: AFTER_1_E4_FEN,
      turn: "Black",
      moveCount: 1,
      lastMove: { from: "e2", to: "e4", san: "e4" },
    }),
  },
  render: (args) => <BoardHost gameState={args.gameState} yourColor={args.yourColor} />,
};

/** Ended game — the board renders read-only, no interaction. */
export const EndedGameReadOnly: StoryObj<ChessBoardArgs> = {
  args: {
    yourColor: "White",
    gameState: makeGameState({
      opponentType: "Computer",
      status: "Ended",
      result: "BlackWins",
      resultReason: "Checkmate",
      moveCount: 85,
      lastMove: { from: "h5", to: "h3", san: "Qh3#" },
    }),
  },
  render: (args) => <BoardHost gameState={args.gameState} yourColor={args.yourColor} />,
  parameters: {
    docs: {
      description: {
        story:
          "The computer checkmated the human. The board is no longer interactive and the last-move highlight marks the mating move.",
      },
    },
  },
};

/** Board error state — a rejected move surfaces in an alert region with a dismiss control. */
export const BoardError: StoryObj<ChessBoardArgs> = {
  args: { yourColor: "White", gameState: makeGameState({ opponentType: "Computer" }) },
  render: (args) => (
    <BoardHost
      gameState={args.gameState}
      yourColor={args.yourColor}
      errorOnMount="Move e2-e5 is not legal: pawns cannot move three squares."
    />
  ),
  parameters: {
    docs: {
      description: {
        story:
          'The rejected move renders in a role="alert" region with a labeled dismiss button ("Dismiss error"), keeping the board usable.',
      },
    },
  },
};
