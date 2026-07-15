import { describe, expect, it, beforeEach, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useCreateGamePreferences } from "./useCreateGamePreferences";

const STORAGE_KEY = "chess:createPrefs";

function readStored(): unknown {
  return JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? "null");
}

describe("useCreateGamePreferences", () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it("defaults to Human vs White when nothing is stored", () => {
    const { result } = renderHook(() => useCreateGamePreferences());

    expect(result.current.preferences).toEqual({
      opponent: "Human",
      color: "White",
    });
  });

  it("hydrates previously persisted preferences on mount", () => {
    sessionStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ opponent: "Ai", color: "Black" }),
    );

    const { result } = renderHook(() => useCreateGamePreferences());

    expect(result.current.preferences).toEqual({
      opponent: "Ai",
      color: "Black",
    });
  });

  it("persists updated preferences to sessionStorage for the session", () => {
    const { result } = renderHook(() => useCreateGamePreferences());

    act(() => {
      result.current.setPreferences({ opponent: "Ai", color: "Black" });
    });

    expect(result.current.preferences).toEqual({
      opponent: "Ai",
      color: "Black",
    });
    expect(readStored()).toEqual({ opponent: "Ai", color: "Black" });
  });

  it("falls back to defaults when the stored payload is corrupted", () => {
    sessionStorage.setItem(STORAGE_KEY, "{not valid json");

    const { result } = renderHook(() => useCreateGamePreferences());

    expect(result.current.preferences).toEqual({
      opponent: "Human",
      color: "White",
    });
  });

  it("falls back to defaults when the stored payload has an unknown value", () => {
    sessionStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ opponent: "Martian", color: "Green" }),
    );

    const { result } = renderHook(() => useCreateGamePreferences());

    expect(result.current.preferences).toEqual({
      opponent: "Human",
      color: "White",
    });
  });

  it("survives sessionStorage being unavailable without throwing", () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("quota");
    });
    const getItem = vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null);

    const { result } = renderHook(() => useCreateGamePreferences());

    expect(() =>
      act(() => {
        result.current.setPreferences({ opponent: "Ai", color: "Black" });
      }),
    ).not.toThrow();

    // In-memory state still reflects the selection even though persistence failed.
    expect(result.current.preferences).toEqual({
      opponent: "Ai",
      color: "Black",
    });

    setItem.mockRestore();
    getItem.mockRestore();
  });
});
