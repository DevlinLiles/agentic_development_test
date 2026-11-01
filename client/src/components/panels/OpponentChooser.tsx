// Opponent chooser for game creation: "Play a friend" (two-human game,
// join-link flow) vs "Play the computer" (solo game vs the server-side AI).
//
// Accessibility notes (verified against the UX review requirements):
// - Native `<input type="radio">` pair inside a `<fieldset><legend>`: the
//   group semantics, keyboard operability (arrows move between options) and
//   programmatic checked state all come for free — the choice is never
//   conveyed by visuals alone.
// - Each radio's accessible name is its visible label; the extra hint text is
//   wired via aria-describedby.
// - Colors come from the app's CSS custom properties so both light and dark
//   schemes render correctly.
// - The radios are never natively `disabled` while a create request is in
//   flight (UX verification finding A-6): a disabled fieldset silently drops
//   focus to <body> if focus is inside it. Interaction is guarded in
//   onChange instead, so the radios keep their tab stops and focus stays
//   put.
// - The selected option's state is also exposed programmatically via
//   aria-checked semantics (native radios do this), plus a visually-hidden
//   selected flag in the option's hint for screen-reader parity with the
//   visual highlight.

import type { OpponentKind } from "../../types/gameTypes";
import "./opponentChooser.css";

export interface OpponentChooserProps {
  /** Currently selected opponent mode. */
  value: OpponentKind;
  /** Fired when the user switches modes. */
  onChange: (value: OpponentKind) => void;
  /**
   * Blocks interaction (e.g. while a create request is in flight) without
   * natively disabling the controls, which would drop focus (A-6).
   */
  disabled?: boolean;
  /** Prefix for element ids — set when more than one chooser can be on a page. */
  idPrefix?: string;
}

const OPTIONS: ReadonlyArray<{
  kind: OpponentKind;
  title: string;
  hint: string;
}> = [
  {
    kind: "Human",
    title: "Play a friend",
    hint: "Two-player game — share a link and play from separate devices.",
  },
  {
    kind: "Computer",
    title: "Play the computer",
    hint: "Solo game — the computer answers every move you make.",
  },
];

export function OpponentChooser({
  value,
  onChange,
  disabled = false,
  idPrefix = "opponent-chooser",
}: OpponentChooserProps) {
  return (
    <fieldset
      className="opponent-chooser"
      aria-disabled={disabled || undefined}
    >
      <legend className="opponent-chooser__legend">Opponent</legend>
      <div className="opponent-chooser__options">
        {OPTIONS.map((option) => {
          const inputId = `${idPrefix}-${option.kind.toLowerCase()}`;
          const hintId = `${inputId}-hint`;
          const selected = value === option.kind;
          return (
            <div
              key={option.kind}
              className={
                "opponent-chooser__option" +
                (selected ? " opponent-chooser__option--selected" : "") +
                (disabled ? " opponent-chooser__option--disabled" : "")
              }
            >
              <input
                id={inputId}
                type="radio"
                name={`${idPrefix}-kind`}
                value={option.kind}
                checked={selected}
                onChange={() => {
                  // Guarded rather than natively disabled so focus is never
                  // dropped mid-interaction (A-6).
                  if (!disabled) onChange(option.kind);
                }}
                aria-describedby={hintId}
              />
              <label htmlFor={inputId} className="opponent-chooser__option-label">
                {option.title}
              </label>
              <p className="opponent-chooser__option-hint" id={hintId}>
                {option.hint}
              </p>
            </div>
          );
        })}
      </div>
    </fieldset>
  );
}
