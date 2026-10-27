// Stories for JoinLinkPanel, covering the new suppression behavior for
// computer games.

import { JoinLinkPanel } from "./JoinLinkPanel";
import type { StoryObj, Meta } from "../../storybook/stubs";
import type { GameStateResponse } from "../../types/gameTypes";

const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

function makeGameState(overrides: Partial<GameStateResponse>): GameStateResponse {
  return {
    gameId: "game-1",
    status: "WaitingForPlayer2",
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

interface JoinLinkPanelArgs {
  gameState: GameStateResponse;
}

const meta: Meta<JoinLinkPanelArgs> = {
  title: "Panels/JoinLinkPanel",
  component: JoinLinkPanel,
  parameters: { layout: "centered" },
};

export default meta;

/** Human game, waiting for player 2 — the classic shareable link panel. */
export const WaitingForPlayer2: StoryObj<JoinLinkPanelArgs> = {
  args: { gameState: makeGameState({}) },
};

/**
 * Computer game: no joinable second seat, so the panel never renders — this
 * story exists to keep that regression visible in review.
 */
export const HiddenForComputerGame: StoryObj<JoinLinkPanelArgs> = {
  args: {
    gameState: makeGameState({
      status: "Active",
      opponentType: "Computer",
    }),
  },
};
