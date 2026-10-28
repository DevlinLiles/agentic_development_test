// Component tests for the create-game opponent-mode selector (OpponentChooser).
//
// These cover the accessibility-critical wiring the chooser relies on: the
// native radio group semantics, label-click selection, the change callback
// reporting the picked OpponentKind, and the disabled state blocking input.

import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OpponentChooser } from "./OpponentChooser";
import type { OpponentKind } from "../../types/gameTypes";

function renderChooser(overrides: { value?: OpponentKind; disabled?: boolean } = {}) {
  const onChange = vi.fn();
  render(
    <OpponentChooser
      value={overrides.value ?? "Human"}
      onChange={onChange}
      disabled={overrides.disabled}
    />,
  );
  return { onChange };
}

describe("OpponentChooser", () => {
  it("renders both opponent modes inside a fieldset with a shared legend", () => {
    renderChooser();

    // The fieldset/legend pair gives the two radios their group semantics;
    // the mode must never be conveyed by visuals alone.
    const fieldset = screen
      .getByRole("radio", { name: "Play a friend" })
      .closest("fieldset");
    expect(fieldset).not.toBeNull();
    expect(screen.getByText("Opponent")).toBeVisible();
    expect(fieldset).toContainElement(screen.getByText("Opponent"));

    // Each radio's accessible name is its visible label.
    expect(screen.getByRole("radio", { name: "Play a friend" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Play the computer" })).toBeInTheDocument();
  });

  it("marks only the currently selected mode as checked", () => {
    renderChooser({ value: "Human" });
    expect(screen.getByRole("radio", { name: "Play a friend" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Play the computer" })).not.toBeChecked();
  });

  it("checks the computer mode when it is the current value", () => {
    renderChooser({ value: "Computer" });
    expect(screen.getByRole("radio", { name: "Play the computer" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Play a friend" })).not.toBeChecked();
  });

  it("reports the picked mode through onChange when clicking a radio", async () => {
    const { onChange } = renderChooser();
    const user = userEvent.setup();

    await user.click(screen.getByRole("radio", { name: "Play the computer" }));
    expect(onChange).toHaveBeenCalledWith("Computer");
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it("does not report a change when the already-selected mode is re-clicked", async () => {
    // The chooser is controlled: `value` only moves when the parent applies it.
    // Re-clicking the option that is already checked is a no-op — the radio
    // never fires a change event for its own current state — so the parent is
    // not asked to re-commit a mode it already holds.
    const { onChange } = renderChooser({ value: "Human" });
    const user = userEvent.setup();

    await user.click(screen.getByRole("radio", { name: "Play a friend" }));
    expect(onChange).not.toHaveBeenCalled();
  });

  it("selects a mode by clicking its visible label", async () => {
    const { onChange } = renderChooser();
    const user = userEvent.setup();

    // The label is htmlFor-wired to the input, so label clicks toggle the
    // radio — the same interaction a keyboard user reaches via the label.
    await user.click(screen.getByText("Play the computer"));
    expect(onChange).toHaveBeenCalledWith("Computer");
  });

  it("switches modes with the keyboard", async () => {
    const { onChange } = renderChooser();
    const user = userEvent.setup();

    // Focus the checked Human radio, then move within the radio group the
    // way native arrow-key navigation does.
    await user.click(screen.getByRole("radio", { name: "Play a friend" }));
    await user.keyboard("{ArrowDown}");
    expect(onChange).toHaveBeenCalledWith("Computer");
  });

  it("does not fire onChange while the group is disabled", async () => {
    const { onChange } = renderChooser({ value: "Human", disabled: true });

    expect(screen.getByRole("radio", { name: "Play a friend" })).toBeDisabled();
    expect(screen.getByRole("radio", { name: "Play the computer" })).toBeDisabled();

    const user = userEvent.setup({ pointerEventsCheck: 0 });
    await user.click(screen.getByRole("radio", { name: "Play the computer" }));
    expect(onChange).not.toHaveBeenCalled();
  });
});
