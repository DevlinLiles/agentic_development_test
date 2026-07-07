// Wires up the SignalR game hub connection. Every GameStateUpdated push is a
// full snapshot of the game (per the API contract), so this hook does no
// diffing/merging — it just exposes the latest snapshot and lets callers
// replace their state with it wholesale.

import { useEffect, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import type { GameStateResponse } from "../types/gameTypes";

export type HubConnectionStatus = "Connecting" | "Connected" | "Reconnecting" | "Disconnected";

const DEFAULT_HUB_URL = "https://localhost:7124/hubs/game";

function getHubUrl(): string {
  return import.meta.env.VITE_HUB_URL ?? DEFAULT_HUB_URL;
}

export interface UseGameHubConnectionResult {
  latestState: GameStateResponse | null;
  connectionStatus: HubConnectionStatus;
}

/**
 * Connects to the game hub and joins the channel for (gameId, playerToken).
 * Pass null for either argument to skip connecting (e.g. before a session
 * exists yet).
 */
export function useGameHubConnection(
  gameId: string | null | undefined,
  playerToken: string | null | undefined,
): UseGameHubConnectionResult {
  const [latestState, setLatestState] = useState<GameStateResponse | null>(null);
  const [connectionStatus, setConnectionStatus] = useState<HubConnectionStatus>("Disconnected");
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    if (!gameId || !playerToken) {
      setConnectionStatus("Disconnected");
      return;
    }

    let disposed = false;
    setLatestState(null);
    setConnectionStatus("Connecting");

    const connection = new HubConnectionBuilder()
      .withUrl(getHubUrl())
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.on("GameStateUpdated", (state: GameStateResponse) => {
      if (!disposed) setLatestState(state);
    });

    connection.onreconnecting(() => {
      if (!disposed) setConnectionStatus("Reconnecting");
    });

    connection.onreconnected(() => {
      if (disposed) return;
      setConnectionStatus("Connected");
      void connection.invoke("JoinGameChannel", gameId, playerToken).catch(() => {
        // Rejoin failures surface as a stale connectionStatus; a future
        // GameStateUpdated push (or manual refresh) will recover.
      });
    });

    connection.onclose(() => {
      if (!disposed) setConnectionStatus("Disconnected");
    });

    connection
      .start()
      .then(() => {
        if (disposed) return;
        setConnectionStatus("Connected");
        return connection.invoke("JoinGameChannel", gameId, playerToken);
      })
      .catch(() => {
        if (!disposed) setConnectionStatus("Disconnected");
      });

    return () => {
      disposed = true;
      connectionRef.current = null;
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop();
      }
    };
  }, [gameId, playerToken]);

  return { latestState, connectionStatus };
}
