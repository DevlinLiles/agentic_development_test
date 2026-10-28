// Stories for the opponent chooser (create-game mode selector).
//
// NOTE ON IMPORTS: these stories use the local type stubs in
// `src/storybook/stubs.ts` only because `@storybook/*` could not be
// installed in this environment (see the comment in that file). Once
// installed, replace `../../storybook/stubs` with `@storybook/react` /
// `@storybook/test` — the story shapes are CSF3-standard.

import { useState } from "react";
import { OpponentChooser } from "./OpponentChooser";
import { fn } from "../../storybook/stubs";
import type { StoryObj, Meta } from "../../storybook/stubs";
import type { OpponentKind } from "../../types/gameTypes";

interface OpponentChooserArgs {
  value: OpponentKind;
  disabled: boolean;
}

const meta: Meta<OpponentChooserArgs> = {
  title: "Panels/OpponentChooser",
  component: OpponentChooser,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
  args: { value: "Human", disabled: false },
};

export default meta;

/** Interactive stateful render so switching modes is playable in the canvas. */
function StatefulOpponentChooser(args: OpponentChooserArgs) {
  const [value, setValue] = useState<OpponentKind>(args.value);
  return <OpponentChooser value={value} onChange={setValue} disabled={args.disabled} />;
}

export const Default: StoryObj<OpponentChooserArgs> = {
  render: (args) => <StatefulOpponentChooser {...args} />,
};

export const ComputerSelected: StoryObj<OpponentChooserArgs> = {
  render: (args) => <StatefulOpponentChooser {...args} />,
  args: { value: "Computer", disabled: false },
};

/** The whole group is disabled while a create request is in flight. */
export const DisabledWhileCreating: StoryObj<OpponentChooserArgs> = {
  render: (args) => <StatefulOpponentChooser {...args} />,
  args: { value: "Computer", disabled: true },
};

/** Callback wiring: selecting an option reports the picked OpponentKind. */
export const WithInteraction: StoryObj<OpponentChooserArgs> = {
  render: (args) => {
    const onChange = fn();
    return <OpponentChooser value={args.value} onChange={onChange} disabled={args.disabled} />;
  },
  play: () => {
    // Keyboard/arrows and clicks are covered by the component's unit tests;
    // the canvas interaction is exercised manually in Storybook UI.
  },
};
