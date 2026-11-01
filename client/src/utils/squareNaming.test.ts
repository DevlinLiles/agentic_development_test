import { describe, expect, it } from "vitest";
import { buildSquareName } from "./squareNaming";
import { parseFen, pieceAt } from "./fen";

const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

describe("buildSquareName", () => {
  it("names an empty square by its square alone", () => {
    expect(buildSquareName({ square: "e4", piece: null })).toBe("e4");
  });

  it("includes the piece content of an occupied square (finding A-1)", () => {
    const grid = parseFen(START_FEN);
    expect(buildSquareName({ square: "e2", piece: pieceAt(grid, "e2") })).toBe("e2, white pawn");
    expect(buildSquareName({ square: "e7", piece: pieceAt(grid, "e7") })).toBe("e7, black pawn");
    expect(buildSquareName({ square: "d8", piece: pieceAt(grid, "d8") })).toBe("d8, black queen");
    expect(buildSquareName({ square: "e1", piece: pieceAt(grid, "e1") })).toBe("e1, white king");
  });

  it("appends the legal-destination state to the name (finding A-4)", () => {
    const grid = parseFen(START_FEN);
    expect(
      buildSquareName({ square: "e4", piece: null, isLegalDestination: true }),
    ).toBe("e4, can move here");
    expect(
      buildSquareName({ square: "d5", piece: pieceAt(grid, "d5"), isLegalDestination: true }),
    ).toBe("d5, can move here");
  });

  it("keeps the same square name for occupied and empty squares alike", () => {
    expect(buildSquareName({ square: "a1", piece: pieceAt(parseFen(START_FEN), "a1") })).toContain("a1");
    expect(buildSquareName({ square: "a1", piece: null })).toBe("a1");
  });
});
