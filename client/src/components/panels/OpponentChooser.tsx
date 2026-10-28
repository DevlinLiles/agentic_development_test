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

import type { OpponentKind } from "../../types/gameTypes";
import "./opponentChooser.css";

export interface OpponentChooserProps {
  /** Currently selected opponent mode. */
  value: OpponentKind;
  /** Fired when the user switches modes. */
  onChange: (value: OpponentKind) => void;
  /** Disables the whole group (e.g. while a create request is in flight). */
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
    <fieldset className="opponent-chooser" disabled={disabled}>
      <legend className="opponent-chooser__legend">Opponent</legend>
      <div className="opponent-chooser__options">
        {OPTIONS.map((option) => {
          const inputId = `${idPrefix}-${option.kind.toLowerCase()}`;
          const hintId = `${inputId}-hint`;
          return (
            <div
              key={option.kind}
              className={
                "opponent-chooser__option" +
                (value === option.kind ? " opponent-chooser__option--selected" : "")
              }
            >
              <input
                id={inputId}
                type="radio"
                name={`${idPrefix}-kind`}
                value={option.kind}
                checked={value === option.kind}
                onChange={() => onChange(option.kind)}
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
