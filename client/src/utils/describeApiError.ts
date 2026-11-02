import { ApiError } from "../api/httpClient";
import type { MoveErrorBody } from "../types/gameTypes";

function isMoveErrorBody(value: unknown): value is MoveErrorBody {
  return typeof value === "object" && value !== null && "error" in value;
}

/**
 * The user action an API call belongs to. Every failure message is voiced for
 * its context — a failed create must never tell the user a "move" failed, and
 * a failed load must not talk about submitting anything (UX verification
 * finding V-3).
 */
export type ApiErrorContext = "move" | "create" | "load";

interface ContextCopy {
  /** Status-specific overrides. Unlisted statuses use `generic`. */
  byStatus: Partial<Record<number, string>>;
  /** Fallback for any other HTTP status. `{status}` is substituted. */
  generic: (status: number) => string;
  /** Fallback when the failure isn't an ApiError at all. */
  unknown: string;
}

const CONTEXT_COPY: Record<ApiErrorContext, ContextCopy> = {
  // Submitting a move (board interaction). This is the default context so
  // existing call sites keep their exact wording.
  move: {
    byStatus: {
      401: "Your session isn't valid for this game.",
      403: "It's not your turn.",
      404: "This game could not be found.",
      // Deliberately no longer claims a refresh happens — recovery depends
      // on the next push/action, and promising one is misleading (finding V-6).
      409: "That move couldn't be applied — the game may have moved on. Try again.",
    },
    generic: (status) => `Move failed (HTTP ${status}).`,
    unknown: "Something went wrong submitting that move.",
  },
  // Creating a game (create screen).
  create: {
    byStatus: {
      409: "The game couldn't be created — please try again.",
    },
    generic: (status) => `Couldn't create the game (HTTP ${status}). Please try again.`,
    unknown: "Something went wrong creating the game. Please try again.",
  },
  // Loading an existing game's state (game screen initial load).
  load: {
    byStatus: {
      401: "Your session isn't valid for this game.",
      403: "You're not allowed to view this game.",
      404: "This game could not be found.",
      409: "This game couldn't be loaded — it may have changed. Try again.",
    },
    generic: (status) => `Couldn't load this game (HTTP ${status}).`,
    unknown: "Something went wrong loading this game.",
  },
};

/**
 * Turns a thrown error from an API call into a short, user-facing message.
 * Handles the documented move-submission failure shapes explicitly and falls
 * back to a generic, context-appropriate message by HTTP status for
 * everything else.
 *
 * @param context Which user action failed — selects the wording. Defaults to
 * `"move"` (the historically only call site) so existing behavior is
 * unchanged.
 */
export function describeApiError(err: unknown, context: ApiErrorContext = "move"): string {
  if (err instanceof ApiError) {
    // Documented move-submission body shapes. They only ever come back from
    // POST /moves, so they are checked first regardless of context.
    if (isMoveErrorBody(err.body)) {
      if (err.body.error === "IllegalMove") {
        return err.body.message ?? "That move isn't legal.";
      }
      if (err.body.error === "PromotionRequired") {
        return "This move requires choosing a promotion piece.";
      }
    }

    const copy = CONTEXT_COPY[context];
    return copy.byStatus[err.status] ?? copy.generic(err.status);
  }

  if (err instanceof Error) return err.message;
  return CONTEXT_COPY[context].unknown;
}
