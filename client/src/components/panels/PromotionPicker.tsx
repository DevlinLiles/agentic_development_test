import type { PlayerColor, PromotionPieceType } from "../../types/gameTypes";
import "./promotionPicker.css";

const OPTIONS: PromotionPieceType[] = ["Queen", "Rook", "Bishop", "Knight"];

const GLYPHS: Record<PlayerColor, Record<PromotionPieceType, string>> = {
  White: { Queen: "♕", Rook: "♖", Bishop: "♗", Knight: "♘" },
  Black: { Queen: "♛", Rook: "♜", Bishop: "♝", Knight: "♞" },
};

export interface PromotionPickerProps {
  color: PlayerColor;
  onSelect: (promotion: PromotionPieceType) => void;
  onCancel: () => void;
}

export function PromotionPicker({ color, onSelect, onCancel }: PromotionPickerProps) {
  return (
    <div className="promotion-picker__overlay" role="dialog" aria-label="Choose promotion piece">
      <div className="promotion-picker">
        <p>Promote pawn to:</p>
        <div className="promotion-picker__options">
          {OPTIONS.map((option) => (
            <button
              key={option}
              type="button"
              className="promotion-picker__option"
              onClick={() => onSelect(option)}
              aria-label={`Promote to ${option}`}
            >
              <span aria-hidden="true">{GLYPHS[color][option]}</span>
              <span className="promotion-picker__option-label">{option}</span>
            </button>
          ))}
        </div>
        <button type="button" className="promotion-picker__cancel" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  );
}
