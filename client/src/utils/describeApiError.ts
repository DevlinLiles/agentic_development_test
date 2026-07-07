import { ApiError } from "../api/httpClient";
import type { MoveErrorBody } from "../types/gameTypes";

function isMoveErrorBody(value: unknown): value is MoveErrorBody {
  return typeof value === "object" && value !== null && "error" in value;
}

/**
 * Turns a thrown error from an API call into a short, user-facing message.
 * Handles the documented move-submission failure shapes explicitly and
 * falls back to a generic message by HTTP status for everything else.
 */
export function describeApiError(err: unknown): string {
  if (err instanceof ApiError) {
    if (isMoveErrorBody(err.body)) {
      if (err.body.error === "IllegalMove") {
        return err.body.message ?? "That move isn't legal.";
      }
      if (err.body.error === "PromotionRequired") {
        return "This move requires choosing a promotion piece.";
      }
    }

    switch (err.status) {
      case 401:
        return "Your session isn't valid for this game.";
      case 403:
        return "It's not your turn.";
      case 404:
        return "This game could not be found.";
      case 409:
        return "That move couldn't be applied — the game may have moved on. Refreshing state.";
      default:
        return `Move failed (HTTP ${err.status}).`;
    }
  }

  if (err instanceof Error) return err.message;
  return "Something went wrong submitting that move.";
}
