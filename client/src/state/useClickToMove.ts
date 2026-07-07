// Click-to-move state machine: Idle -> PieceSelected -> AwaitingServerResponse
// -> (Idle | PromotionPending) -> AwaitingServerResponse -> Idle.
//
// This hook never decides whether a move is legal — it only decides whether
// a click *could* be the start/continuation of a move attempt (cosmetic
// piece-shape hints from pseudoLegalMoves.ts) and submits candidate moves to
// the server, which is the sole legality authority.

import { useCallback, useMemo, useState } from "react";
import * as gamesApi from "../api/gamesApi";
import { parseFen, pieceAt } from "../utils/fen";
import { getPseudoLegalDestinations } from "../utils/pseudoLegalMoves";
import { describeApiError } from "../utils/describeApiError";
import type { GameStateResponse, PlayerColor, PromotionPieceType } from "../types/gameTypes";

export type ClickToMovePhase =
  | { phase: "Idle" }
  | { phase: "PieceSelected"; square: string; destinations: string[] }
  | { phase: "AwaitingServerResponse"; from: string; to: string; promotion: PromotionPieceType | null }
  | { phase: "PromotionPending"; from: string; to: string };

export interface UseClickToMoveOptions {
  gameId: string | null | undefined;
  playerToken: string | null | undefined;
  yourColor: PlayerColor | null | undefined;
  gameState: GameStateResponse | null;
  onStateUpdate: (state: GameStateResponse) => void;
}

export interface UseClickToMoveResult {
  phase: ClickToMovePhase;
  selectedSquare: string | null;
  legalDestinations: string[];
  promotionPending: { from: string; to: string } | null;
  isAwaitingServer: boolean;
  error: string | null;
  handleSquareClick: (square: string) => void;
  handlePromotionSelect: (promotion: PromotionPieceType) => void;
  handleCancelPromotion: () => void;
  clearError: () => void;
}

function isPromotionMove(toSquare: string, movingColor: PlayerColor, isPawn: boolean): boolean {
  if (!isPawn) return false;
  const destinationRank = Number.parseInt(toSquare.slice(1), 10);
  return movingColor === "White" ? destinationRank === 8 : destinationRank === 1;
}

export function useClickToMove(options: UseClickToMoveOptions): UseClickToMoveResult {
  const { gameId, playerToken, yourColor, gameState, onStateUpdate } = options;
  const [phaseState, setPhaseState] = useState<ClickToMovePhase>({ phase: "Idle" });
  const [error, setError] = useState<string | null>(null);

  const grid = useMemo(() => (gameState ? parseFen(gameState.fen) : null), [gameState]);

  const submit = useCallback(
    async (fromSquare: string, toSquare: string, promotion: PromotionPieceType | null) => {
      if (!gameId || !playerToken) {
        setError("Can't submit a move without an active session.");
        setPhaseState({ phase: "Idle" });
        return;
      }
      setError(null);
      setPhaseState({ phase: "AwaitingServerResponse", from: fromSquare, to: toSquare, promotion });
      try {
        const newState = await gamesApi.submitMove(gameId, playerToken, fromSquare, toSquare, promotion);
        onStateUpdate(newState);
        setPhaseState({ phase: "Idle" });
      } catch (err) {
        setError(describeApiError(err));
        setPhaseState({ phase: "Idle" });
      }
    },
    [gameId, playerToken, onStateUpdate],
  );

  const handleSquareClick = useCallback(
    (square: string) => {
      if (!grid || !gameState || !yourColor) return;
      if (phaseState.phase === "AwaitingServerResponse" || phaseState.phase === "PromotionPending") {
        return; // ignore board clicks while a move is in flight or a promotion choice is pending
      }

      const clickedPiece = pieceAt(grid, square);
      const isOwnPiece = clickedPiece !== null && clickedPiece.color === yourColor;
      const isMyTurn = gameState.turn === yourColor;

      if (phaseState.phase === "Idle") {
        if (isOwnPiece && isMyTurn) {
          setError(null);
          setPhaseState({
            phase: "PieceSelected",
            square,
            destinations: getPseudoLegalDestinations(grid, square),
          });
        }
        return;
      }

      // phase === "PieceSelected"
      if (isOwnPiece && isMyTurn) {
        // Same piece or a different one of my own pieces: (re)select it.
        setError(null);
        setPhaseState({
          phase: "PieceSelected",
          square,
          destinations: getPseudoLegalDestinations(grid, square),
        });
        return;
      }

      if (phaseState.destinations.includes(square)) {
        const movingPiece = pieceAt(grid, phaseState.square);
        const needsPromotion =
          movingPiece !== null &&
          isPromotionMove(square, movingPiece.color, movingPiece.type === "Pawn");

        if (needsPromotion) {
          setError(null);
          setPhaseState({ phase: "PromotionPending", from: phaseState.square, to: square });
          return;
        }

        void submit(phaseState.square, square, null);
        return;
      }

      // Click elsewhere: deselect.
      setPhaseState({ phase: "Idle" });
    },
    [grid, gameState, yourColor, phaseState, submit],
  );

  const handlePromotionSelect = useCallback(
    (promotion: PromotionPieceType) => {
      if (phaseState.phase !== "PromotionPending") return;
      void submit(phaseState.from, phaseState.to, promotion);
    },
    [phaseState, submit],
  );

  const handleCancelPromotion = useCallback(() => {
    if (phaseState.phase !== "PromotionPending") return;
    setPhaseState({ phase: "Idle" });
  }, [phaseState]);

  const clearError = useCallback(() => setError(null), []);

  return {
    phase: phaseState,
    selectedSquare: phaseState.phase === "PieceSelected" ? phaseState.square : null,
    legalDestinations: phaseState.phase === "PieceSelected" ? phaseState.destinations : [],
    promotionPending:
      phaseState.phase === "PromotionPending" ? { from: phaseState.from, to: phaseState.to } : null,
    isAwaitingServer: phaseState.phase === "AwaitingServerResponse",
    error,
    handleSquareClick,
    handlePromotionSelect,
    handleCancelPromotion,
    clearError,
  };
}
