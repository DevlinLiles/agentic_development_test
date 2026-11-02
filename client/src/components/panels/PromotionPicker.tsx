import { useEffect, useRef } from "react";
import type { KeyboardEvent } from "react";
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

/**
 * Promotion-piece dialog.
 *
 * Focus management (UX verification findings A-7 and V-4): when the dialog
 * opens, focus moves onto the default choice (Queen) instead of staying on
 * the board square behind the overlay, and when it closes — via a selection,
 * Cancel, or Escape — focus is restored to the element that had it before
 * the dialog opened, rather than falling back to <body> and leaving
 * keyboard/screen-reader users stranded off the board.
 */
export function PromotionPicker({ color, onSelect, onCancel }: PromotionPickerProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const defaultOptionRef = useRef<HTMLButtonElement>(null);
  // The element to hand focus back to when the dialog unmounts (V-4).
  const restoreFocusRef = useRef<HTMLElement | null>(null);

  // Move focus into the dialog as it opens, so the user lands on the
  // recommended (Queen) option rather than on a board square now covered by
  // the overlay. On unmount, put focus back where it was so the user keeps
  // their place on the board.
  useEffect(() => {
    restoreFocusRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    defaultOptionRef.current?.focus();
    return () => {
      restoreFocusRef.current?.focus();
    };
  }, []);

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      onCancel();
    }
  };

  return (
    <div className="promotion-picker__overlay" role="presentation">
      <div
        ref={dialogRef}
        className="promotion-picker"
        role="dialog"
        aria-modal="true"
        aria-label="Choose promotion piece"
        onKeyDown={handleKeyDown}
      >
        <p>Promote pawn to:</p>
        <div className="promotion-picker__options">
          {OPTIONS.map((option, index) => (
            <button
              key={option}
              ref={index === 0 ? defaultOptionRef : undefined}
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
