// Stories for StatusPanel across game modes, including the new
// computer-opponent (solo) states.

import { StatusPanel } from "./StatusPanel";
import type { StoryObj, Meta } from "../../storybook/stubs";
import type { GameStateResponse } from "../../types/gameTypes";

const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
const AFTER_1_E4_FEN =
  "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e4 0 1";
const AFTER_2_NF6_FEN =
  "rnbqkbnr/pppppppp/8/8/4P3/5n2/PPPP1PPP/RNBQKBNR w KQkq - 1 2";

interface StatusPanelArgs {
  gameState: GameStateResponse;
}

function makeGameState(overrides: Partial<GameStateResponse>): GameStateResponse {
  return {
    gameId: "game-1",
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

const meta: Meta<StatusPanelArgs> = {
  title: "Panels/StatusPanel",
  component: StatusPanel,
  parameters: { layout: "centered" },
};

export default meta;

export const WaitingForOpponent: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({ status: "WaitingForPlayer2" }),
  },
};

export const HumanGameYourTurn: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({ status: "Active", turn: "White", yourColor: "White" }),
  },
};

export const HumanGameOpponentTurn: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({
      status: "Active",
      turn: "Black",
      yourColor: "White",
      fen: AFTER_1_E4_FEN,
      moveCount: 1,
      lastMove: { from: "e2", to: "e4", san: "e4" },
    }),
  },
};

/** Computer game, computer to move — no reply line yet. */
export const ComputerGameThinking: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({
      status: "Active",
      turn: "Black",
      yourColor: "White",
      opponentType: "Computer",
      fen: AFTER_1_E4_FEN,
      moveCount: 1,
      lastMove: { from: "e2", to: "e4", san: "e4" },
    }),
  },
};

/**
 * Computer game after the reply lands: "Computer played Nf6" is announced in
 * the panel's live region, above "Your turn".
 */
export const ComputerGameAfterReply: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({
      status: "Active",
      turn: "White",
      yourColor: "White",
      opponentType: "Computer",
      fen: AFTER_2_NF6_FEN,
      moveCount: 2,
      lastMove: { from: "g8", to: "f6", san: "Nf6" },
    }),
  },
};

/** Computer game, human in check after the computer's reply. */
export const ComputerGameCheckOnHuman: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({
      status: "Active",
      turn: "White",
      yourColor: "White",
      opponentType: "Computer",
      fen: AFTER_2_NF6_FEN,
      moveCount: 2,
      isCheck: true,
      lastMove: { from: "g8", to: "f6", san: "Nf6" },
    }),
  },
};

/** Computer checkmates the human — terminal banner in a solo game. */
export const ComputerGameCheckmateByComputer: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({
      status: "Ended",
      turn: "White",
      yourColor: "White",
      opponentType: "Computer",
      result: "BlackWins",
      resultReason: "Checkmate",
      moveCount: 85,
      lastMove: { from: "h5", to: "h3", san: "Qh3#" },
    }),
  },
};

export const ComputerGameStalemateDraw: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({
      status: "Ended",
      turn: "White",
      yourColor: "White",
      opponentType: "Computer",
      result: "Draw",
      resultReason: "Stalemate",
      moveCount: 42,
    }),
  },
};

export const HumanGameCheckmateWhiteWins: StoryObj<StatusPanelArgs> = {
  args: {
    gameState: makeGameState({
      status: "Ended",
      result: "WhiteWins",
      resultReason: "Checkmate",
    }),
  },
};
