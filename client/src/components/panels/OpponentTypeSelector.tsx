import type { OpponentType } from "../../types/gameTypes";
import "./opponentTypeSelector.css";

export interface OpponentTypeSelectorProps {
  /** Currently selected opponent type. */
  value: OpponentType;
  /** Called when the user selects a different opponent type. */
  onChange: (value: OpponentType) => void;
  /** Disables all options (e.g. while a game is being created). */
  disabled?: boolean;
}

interface OpponentOption {
  value: OpponentType;
  label: string;
  description: string;
}

const OPPONENT_OPTIONS: readonly OpponentOption[] = [
  {
    value: "Human",
    label: "Human (share link)",
    description: "Share a link and play with a friend online.",
  },
  {
    value: "Ai",
    label: "Computer (AI)",
    description: "Play solo against a basic computer opponent.",
  },
];

/**
 * Radio-button group for choosing the opponent type at game-creation time.
 * Defaults to "Human" (legacy behavior) per AC-1.1.
 */
export function OpponentTypeSelector({
  value,
  onChange,
  disabled = false,
}: OpponentTypeSelectorProps) {
  return (
    <fieldset className="opponent-selector" disabled={disabled}>
      <legend className="opponent-selector__legend">Opponent</legend>
      <div className="opponent-selector__options">
        {OPPONENT_OPTIONS.map((option) => {
          const checked = value === option.value;
          return (
            <label
              key={option.value}
              className={`opponent-selector__option${checked ? " opponent-selector__option--selected" : ""}`}
            >
              <input
                type="radio"
                name="opponentType"
                value={option.value}
                checked={checked}
                onChange={() => onChange(option.value)}
                disabled={disabled}
              />
              <span className="opponent-selector__text">
                <span className="opponent-selector__label">{option.label}</span>
                <span className="opponent-selector__description">
                  {option.description}
                </span>
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}
