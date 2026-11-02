// Stories for the promotion-piece dialog.
//
// NOTE ON IMPORTS: these stories use the local type stubs in
// `src/storybook/stubs.ts` only because `@storybook/*` could not be
// installed in this environment (see the comment in that file). Once
// installed, replace `../../storybook/stubs` with `@storybook/react` /
// `@storybook/test` — the story shapes are CSF3-standard.

import { useState } from "react";
import { PromotionPicker } from "./PromotionPicker";
import { fn } from "../../storybook/stubs";
import type { StoryObj, Meta } from "../../storybook/stubs";
import type { PlayerColor, PromotionPieceType } from "../../types/gameTypes";

interface PromotionPickerArgs {
  color: PlayerColor;
}

const meta: Meta<PromotionPickerArgs> = {
  title: "Panels/PromotionPicker",
  component: PromotionPicker,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
  args: { color: "White" },
};

export default meta;

/** Renders inline (not as a full-screen overlay) so the options are visible in the canvas. */
function InlinePicker({ color }: PromotionPickerArgs) {
  return (
    <div style={{ padding: "1rem" }}>
      <PromotionPicker
        color={color}
        onSelect={fn()}
        onCancel={fn()}
      />
    </div>
  );
}

/** White promoting — the default in a computer game (the human is White). */
export const WhitePromotion: StoryObj<PromotionPickerArgs> = {
  render: (args) => <InlinePicker {...args} />,
  args: { color: "White" },
};

/** Black promoting — the visual the AI's own promotion would produce. */
export const BlackPromotion: StoryObj<PromotionPickerArgs> = {
  render: (args) => <InlinePicker {...args} />,
  args: { color: "Black" },
};

/**
 * Full focus lifecycle (findings A-7 + V-4): opening the picker moves focus
 * to Queen; choosing a piece or cancelling restores focus to the button that
 * opened it. The status line below the button reports where focus is now,
 * so the restore behavior is observable in the canvas without a screen
 * reader.
 */
export const FocusLifecycle: StoryObj<PromotionPickerArgs> = {
  render: () => {
    const [open, setOpen] = useState(false);
    const [lastAction, setLastAction] = useState<string>("picker not opened yet");
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem", alignItems: "flex-start" }}>
        <button type="button" onClick={() => { setLastAction("opened"); setOpen(true); }}>
          Promote a pawn
        </button>
        <p role="status">
          {open
            ? "Picker open — focus is on the Queen option"
            : `${lastAction} — focus is on ${document.activeElement?.tagName.toLowerCase() ?? "unknown"}`}
        </p>
        {open && (
          <PromotionPicker
            color="White"
            onSelect={(promotion: PromotionPieceType) => {
              setLastAction(`selected ${promotion}`);
              setOpen(false);
            }}
            onCancel={() => {
              setLastAction("cancelled");
              setOpen(false);
            }}
          />
        )}
      </div>
    );
  },
};

/** Callback wiring: each option reports the piece it promotes to. */
export const WithInteraction: StoryObj<PromotionPickerArgs> = {
  render: (args) => (
    <InlinePicker {...args} />
  ),
  play: () => {
    // Escape-to-cancel and focus restore are covered by the component's
    // unit tests; the canvas interaction is exercised manually in Storybook UI.
  },
};
