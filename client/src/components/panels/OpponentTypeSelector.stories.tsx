import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { OpponentTypeSelector } from "./OpponentTypeSelector";
import type { OpponentType } from "../../types/gameTypes";

const meta: Meta<typeof OpponentTypeSelector> = {
  title: "Panels/OpponentTypeSelector",
  component: OpponentTypeSelector,
  tags: ["autodocs"],
  argTypes: {
    value: {
      control: "radio",
      options: ["Human", "Ai"],
    },
    disabled: { control: "boolean" },
  },
};

export default meta;
type Story = StoryObj<typeof OpponentTypeSelector>;

export const Default: Story = {
  args: {
    value: "Human",
    onChange: () => {},
  },
};

export const AiSelected: Story = {
  args: {
    value: "Ai",
    onChange: () => {},
  },
};

export const Disabled: Story = {
  args: {
    value: "Human",
    onChange: () => {},
    disabled: true,
  },
};

/**
 * Interactive story demonstrating the user switching between Human and AI.
 */
export const Interactive: Story = {
  render: () => {
    const [value, setValue] = useState<OpponentType>("Human");
    return <OpponentTypeSelector value={value} onChange={setValue} />;
  },
};
