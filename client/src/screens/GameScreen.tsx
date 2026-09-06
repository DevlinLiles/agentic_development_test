import { useEffect, useState, useCallback } from "react";
import { useParams } from "react-router-dom";
import * as gamesApi from "../api/gamesApi";
import { useGameSession, type StoredGameSession } from "../state/useGameSession";
import { useGameHubConnection } from "../signalr/useGameHubConnection";
import { ChessBoard } from "../components/board/ChessBoard";
import { StatusPanel } from "../components/panels/StatusPanel";
import { JoinLinkPanel } from "../components/panels/JoinLinkPanel";
import { MoveHistoryPanel } from "../components/panels/MoveHistoryPanel";
import { describeApiError } from "../utils/describeApiError";
import type { GameStateResponse } from "../types/gameTypes";
import "./gameScreen.css";

export function GameScreen() {
  const { gameId } = useParams<{ gameId: string }>();
  const { getSession, saveSession } = useGameSession();

  const [session, setSession] = useState<StoredGameSession | null>(null);
  const [gameState, setGameState] = useState<GameStateResponse | null>(null);
  const [notParticipant, setNotParticipant] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [isInitializing, setIsInitializing] = useState(true);

  // Initial load: reuse a saved session for this game, or join if the game
  // is still open. Per product spec, there is no spectator mode — a
  // participant-less viewer of an in-progress/ended game gets a clear
  // message, not a read-only board.
  useEffect(() => {
    if (!gameId) return;
    const currentGameId = gameId;
    let cancelled = false;

    async function load() {
      setIsInitializing(true);
      setLoadError(null);
      setNotParticipant(false);

      try {
        const existingSession = getSession(currentGameId);

        if (existingSession) {
          const state = await gamesApi.getGameState(currentGameId, existingSession.playerToken);
          if (cancelled) return;
          setSession(existingSession);
          setGameState(state);
          return;
        }

        const state = await gamesApi.getGameState(currentGameId);
        if (cancelled) return;

        // VsAi games have no second human seat to claim — the AI is the
        // opponent and the game starts Active immediately. Skip the join
        // path entirely; a session-less viewer of an AI game has no seat to
        // claim (no spectator mode, per spec), so surface the
        // not-a-participant message rather than attempting to join.
        if (state.mode === "VsAi") {
          setNotParticipant(true);
          setGameState(state);
          return;
        }

        if (state.status !== "WaitingForPlayer2") {
          setNotParticipant(true);
          setGameState(state);
          return;
        }

        const joinResult = await gamesApi.joinGame(currentGameId);
        if (cancelled) return;
        const newSession: StoredGameSession = {
          gameId: currentGameId,
          playerToken: joinResult.playerToken,
          color: joinResult.color,
        };
        saveSession(newSession);
        setSession(newSession);
        setGameState(joinResult.gameState);
      } catch (err) {
        if (!cancelled) setLoadError(describeApiError(err));
      } finally {
        if (!cancelled) setIsInitializing(false);
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [gameId, getSession, saveSession]);

  const hub = useGameHubConnection(
    session ? gameId : null,
    session ? session.playerToken : null,
  );

  // Every push is a full snapshot, so replace rather than merge — except
  // yourColor, which the hub always sends as null (it's a per-connection
  // fact the group broadcast doesn't know). We already know it from the
  // session established at create/join time, so patch it back in.
  useEffect(() => {
    if (hub.latestState) {
      setGameState({
        ...hub.latestState,
        yourColor: session?.color ?? hub.latestState.yourColor,
      });
    }
  }, [hub.latestState, session]);

  const handleStateUpdate = useCallback((state: GameStateResponse) => {
    setGameState(state);
  }, []);

  if (!gameId) {
    return <p role="alert">Missing game id.</p>;
  }

  if (isInitializing) {
    return <p>Loading game…</p>;
  }

  if (loadError) {
    return <p role="alert">{loadError}</p>;
  }

  if (notParticipant) {
    return (
      <div className="game-screen__not-participant" role="alert">
        <p>You're not a participant in this game.</p>
        <p>This game is already in progress or finished, and no saved session for it was found in this browser.</p>
      </div>
    );
  }

  if (!gameState) {
    return <p role="alert">Unable to load game state.</p>;
  }

  return (
    <div className="game-screen">
      <StatusPanel gameState={gameState} />
      <JoinLinkPanel gameState={gameState} />
      <div className="game-screen__layout">
        <ChessBoard
          gameId={gameId}
          playerToken={session?.playerToken ?? null}
          yourColor={session?.color ?? gameState.yourColor}
          gameState={gameState}
          onStateUpdate={handleStateUpdate}
        />
        <MoveHistoryPanel
          gameId={gameId}
          token={session?.playerToken}
          refreshKey={gameState.moveCount}
        />
      </div>
    </div>
  );
}
