import { describe, expect, it } from "vitest";
import { layoutForColor, WHITE_PERSPECTIVE, BLACK_PERSPECTIVE } from "./boardLayout";

describe("boardLayout", () => {
  it("puts rank 8, file a first for the White perspective", () => {
    expect(WHITE_PERSPECTIVE.rows[0][0]).toEqual({ file: 0, rank: 8, square: "a8" });
    expect(WHITE_PERSPECTIVE.rows[0][7]).toEqual({ file: 7, rank: 8, square: "h8" });
    expect(WHITE_PERSPECTIVE.rows[7][0]).toEqual({ file: 0, rank: 1, square: "a1" });
  });

  it("mirrors the visit order for the Black perspective", () => {
    expect(BLACK_PERSPECTIVE.rows[0][0]).toEqual({ file: 7, rank: 1, square: "h1" });
    expect(BLACK_PERSPECTIVE.rows[0][7]).toEqual({ file: 0, rank: 1, square: "a1" });
    expect(BLACK_PERSPECTIVE.rows[7][0]).toEqual({ file: 7, rank: 8, square: "h8" });
  });

  it("covers all 64 squares exactly once in each orientation", () => {
    for (const layout of [WHITE_PERSPECTIVE, BLACK_PERSPECTIVE]) {
      const squares = layout.rows.flat().map((cell) => cell.square);
      expect(squares).toHaveLength(64);
      expect(new Set(squares).size).toBe(64);
    }
  });

  it("resolves a null viewer color to the White perspective", () => {
    expect(layoutForColor(null)).toBe(WHITE_PERSPECTIVE);
    expect(layoutForColor(undefined)).toBe(WHITE_PERSPECTIVE);
  });

  it("preserves grid parity: shading depends only on file+rank, never orientation", () => {
    // Every square must keep the same (file+rank) parity whichever way the
    // board is flipped, so the board never re-shades on orientation change.
    const bySquare = new Map(
      WHITE_PERSPECTIVE.rows.flat().map((cell) => [cell.square, (cell.file + cell.rank) % 2]),
    );
    for (const cell of BLACK_PERSPECTIVE.rows.flat()) {
      expect((cell.file + cell.rank) % 2).toBe(bySquare.get(cell.square));
    }
  });
});
