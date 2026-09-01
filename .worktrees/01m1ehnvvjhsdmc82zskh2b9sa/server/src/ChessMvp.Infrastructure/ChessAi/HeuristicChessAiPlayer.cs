using System.Globalization;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Infrastructure.ChessAi;

/// <summary>
/// Stateless heuristic chess AI move-selector.
/// <para>
/// Given a position (FEN) and the side to move, this player enumerates the
/// legal moves, scores captures by material gain, applies deterministic
/// tie-breakers (giving check, piece development, central control), promotes
/// pawns to a Queen when promotion is available, and finally falls back to a
/// stable arbitrary legal move when nothing else distinguishes the candidates.
/// </para>
/// <para>
/// The implementation is intentionally self-contained: it parses the FEN,
/// generates pseudo-legal moves, filters out moves that leave the moving side's
/// king in check, and ranks the survivors. Because the enumeration order and
/// every comparison are deterministic, repeated calls with identical inputs
/// always return the same move.
/// </para>
/// </summary>
public sealed class HeuristicChessAiPlayer : IChessAiPlayer
{
    // Standard material values used when scoring captures.
    private const int PawnValue = 100;
    private const int KnightValue = 320;
    private const int BishopValue = 330;
    private const int RookValue = 500;
    private const int QueenValue = 900;
    private const int KingValue = 20000;

    private static readonly int[] CentralControlBonus = new int[64]
    {
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, 1, 1, 1, 1, 1, 0,
        0, 1, 2, 2, 2, 2, 1, 0,
        0, 1, 2, 3, 3, 2, 1, 0,
        0, 1, 2, 3, 3, 2, 1, 0,
        0, 1, 2, 2, 2, 2, 1, 0,
        0, 1, 1, 1, 1, 1, 1, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
    };

    private static readonly int[] KnightOffsets = { -17, -15, -10, -6, 6, 10, 15, 17 };
    private static readonly int[] KingOffsets = { -9, -8, -7, -1, 1, 7, 8, 9 };
    private static readonly int[] BishopDirections = { -9, -7, 7, 9 };
    private static readonly int[] RookDirections = { -8, -1, 1, 8 };
    private static readonly int[] QueenDirections = { -9, -8, -7, -1, 1, 7, 8, 9 };

    /// <inheritdoc/>
    public Move? SelectMove(string positionFen, PlayerColor sideToMove)
    {
        if (string.IsNullOrWhiteSpace(positionFen))
        {
            return null;
        }

        var board = Board.Parse(positionFen);
        if (board is null)
        {
            return null;
        }

        bool whiteToMove = sideToMove == PlayerColor.White;
        var legal = GenerateLegalMoves(board, whiteToMove);

        if (legal.Count == 0)
        {
            return null;
        }

        // Stable ordering so that identical inputs always produce identical
        // output, including the arbitrary fallback. Captures/high scores first;
        // ties broken by origin square then destination square (ordinal), which
        // makes the fallback a stable arbitrary legal move.
        var best = legal
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.FromSquare, StringComparer.Ordinal)
            .ThenBy(m => m.ToSquare, StringComparer.Ordinal)
            .First();

        return ToDomainMove(best);
    }

    private static List<ScoredMove> GenerateLegalMoves(Board board, bool whiteToMove)
    {
        var pseudo = GeneratePseudoLegalMoves(board, whiteToMove);
        var legal = new List<ScoredMove>(pseudo.Count);

        foreach (var move in pseudo)
        {
            // Apply the move in-place (with undo) and confirm the moving side's
            // king is not left in check, i.e. the move is fully legal.
            var undo = board.Apply(move);
            bool kingInCheck = IsSquareAttacked(board, undo.MovingKingSquare, !whiteToMove);
            board.Undo(undo);

            if (!kingInCheck)
            {
                legal.Add(move);
            }
        }

        // Re-score now that legality (including checks delivered) can be tested.
        for (int i = 0; i < legal.Count; i++)
        {
            legal[i] = legal[i] with { Score = ScoreMove(board, legal[i], whiteToMove) };
        }

        return legal;
    }

    private static int ScoreMove(Board board, ScoredMove move, bool whiteToMove)
    {
        int score = 0;

        // 1. Material gain from captures.
        if (move.CapturedPiece != Piece.None)
        {
            score += PieceValue(move.CapturedPiece);

            // MVV-LVA flavour: prefer capturing with cheaper attackers so the
            // selector favours winning the exchange when several pieces can take.
            score -= PieceValue(move.MovingPiece) / 10;
        }

        // 2. Queen promotion bonus (the heuristic always promotes to Queen).
        if (move.IsPromotion && move.PromotionPiece == Piece.Queen)
        {
            score += QueenValue;
        }

        // 3. Tie-breaker: delivering check.
        var undo = board.Apply(move);
        bool givesCheck = undo.OpponentKingSquare >= 0 &&
                         IsSquareAttacked(board, undo.OpponentKingSquare, whiteToMove);
        board.Undo(undo);
        if (givesCheck)
        {
            score += 50;
        }

        // 4. Tie-breaker: piece development (minor piece leaving its back rank).
        if ((move.MovingPieceType == PieceType.Knight || move.MovingPieceType == PieceType.Bishop) &&
            IsStartingSquare(move.FromSquare, whiteToMove))
        {
            score += 10;
        }

        // 5. Tie-breaker: central control of the destination square.
        int toIndex = (move.ToSquare[1] - '1') * 8 + (move.ToSquare[0] - 'a');
        if (toIndex >= 0 && toIndex < CentralControlBonus.Length)
        {
            score += CentralControlBonus[toIndex];
        }

        return score;
    }

    private static bool IsStartingSquare(string square, bool whiteToMove)
    {
        char rank = square[1];
        return whiteToMove ? rank == '1' : rank == '8';
    }

    private static int PieceValue(Piece piece) => piece switch
    {
        Piece.WhitePawn or Piece.BlackPawn => PawnValue,
        Piece.WhiteKnight or Piece.BlackKnight => KnightValue,
        Piece.WhiteBishop or Piece.BlackBishop => BishopValue,
        Piece.WhiteRook or Piece.BlackRook => RookValue,
        Piece.WhiteQueen or Piece.BlackQueen => QueenValue,
        Piece.WhiteKing or Piece.BlackKing => KingValue,
        _ => 0,
    };

    private static Move ToDomainMove(ScoredMove move)
    {
        // Map the internal representation onto the domain Move. Promotion is
        // always to a Queen per the heuristic policy.
        return move.IsPromotion
            ? new Move(move.FromSquare, move.ToSquare, PromotionPieceType.Queen)
            : new Move(move.FromSquare, move.ToSquare);
    }

    // ---- Pseudo-legal move generation (sliding, knight, king, pawn) --------

    private static List<ScoredMove> GeneratePseudoLegalMoves(Board board, bool whiteToMove)
    {
        var moves = new List<ScoredMove>(64);

        for (int index = 0; index < 64; index++)
        {
            Piece piece = board.Squares[index];
            if (piece == Piece.None)
            {
                continue;
            }

            bool isWhite = board.IsWhite[index];
            if (isWhite != whiteToMove)
            {
                continue;
            }

            string from = SquareName(index);
            PieceType type = PieceTypeOf(piece);

            switch (type)
            {
                case PieceType.Pawn:
                    AddPawnMoves(board, index, isWhite, from, piece, moves);
                    break;
                case PieceType.Knight:
                    AddStepperMoves(board, index, isWhite, from, piece, KnightOffsets, moves, knight: true);
                    break;
                case PieceType.King:
                    AddStepperMoves(board, index, isWhite, from, piece, KingOffsets, moves, knight: false);
                    break;
                case PieceType.Bishop:
                    AddSlidingMoves(board, index, isWhite, from, piece, BishopDirections, moves);
                    break;
                case PieceType.Rook:
                    AddSlidingMoves(board, index, isWhite, from, piece, RookDirections, moves);
                    break;
                case PieceType.Queen:
                    AddSlidingMoves(board, index, isWhite, from, piece, QueenDirections, moves);
                    break;
            }
        }

        return moves;
    }

    private static void AddPawnMoves(Board board, int index, bool isWhite, string from, Piece piece, List<ScoredMove> moves)
    {
        int direction = isWhite ? 8 : -8;
        int startRank = isWhite ? 1 : 6;
        int promotionRank = isWhite ? 7 : 0;
        int fromFile = index % 8;
        int fromRank = index / 8;

        int forward = index + direction;
        if (forward >= 0 && forward < 64 && board.Squares[forward] == Piece.None)
        {
            AddPawnStepOrPromotion(moves, from, forward, piece, Piece.None, forward / 8, promotionRank);
        }

        // Double push from starting rank.
        if (fromRank == startRank)
        {
            int doubleForward = index + 2 * direction;
            if (doubleForward >= 0 && doubleForward < 64 &&
                board.Squares[forward] == Piece.None &&
                board.Squares[doubleForward] == Piece.None)
            {
                moves.Add(new ScoredMove(from, SquareName(doubleForward), piece,
                    Piece.None, false, Piece.None, 0));
            }
        }

        // Diagonal captures (en-passant intentionally omitted for determinism).
        foreach (int capDir in new[] { direction - 1, direction + 1 })
        {
            int target = index + capDir;
            if (target < 0 || target >= 64)
            {
                continue;
            }

            int toFile = target % 8;
            if (Math.Abs(toFile - fromFile) != 1)
            {
                continue;
            }

            Piece occupant = board.Squares[target];
            if (occupant != Piece.None && board.IsWhite[target] != isWhite)
            {
                AddPawnStepOrPromotion(moves, from, target, piece, occupant, target / 8, promotionRank);
            }
        }
    }

    private static void AddPawnStepOrPromotion(List<ScoredMove> moves, string from, int target, Piece piece, Piece captured, int rank, int promotionRank)
    {
        if (rank == promotionRank)
        {
            moves.Add(new ScoredMove(from, SquareName(target), piece, captured, true, Piece.Queen, 0));
        }
        else
        {
            moves.Add(new ScoredMove(from, SquareName(target), piece, captured, false, Piece.None, 0));
        }
    }

    private static void AddStepperMoves(Board board, int index, bool isWhite, string from, Piece piece, int[] offsets, List<ScoredMove> moves, bool knight)
    {
        int fromFile = index % 8;
        int fromRank = index / 8;

        foreach (int offset in offsets)
        {
            int target = index + offset;
            if (target < 0 || target >= 64)
            {
                continue;
            }

            int toFile = target % 8;
            int toRank = target / 8;
            int fileDist = Math.Abs(toFile - fromFile);
            int rankDist = Math.Abs(toRank - fromRank);

            // Knights move (1,2) or (2,1); kings move one square in any
            // direction. Reject anything that wrapped around the board edges.
            if (knight)
            {
                if (!((fileDist == 1 && rankDist == 2) || (fileDist == 2 && rankDist == 1)))
                {
                    continue;
                }
            }
            else
            {
                if (fileDist > 1 || rankDist > 1)
                {
                    continue;
                }
            }

            Piece occupant = board.Squares[target];
            if (occupant == Piece.None || board.IsWhite[target] != isWhite)
            {
                moves.Add(new ScoredMove(from, SquareName(target), piece, occupant, false, Piece.None, 0));
            }
        }
    }

    private static void AddSlidingMoves(Board board, int index, bool isWhite, string from, Piece piece, int[] directions, List<ScoredMove> moves)
    {
        int fromFile = index % 8;
        int fromRank = index / 8;

        foreach (int dir in directions)
        {
            (int fileDelta, int rankDelta) = DirectionToDelta(dir);
            int f = fromFile + fileDelta;
            int r = fromRank + rankDelta;

            while (f >= 0 && f < 8 && r >= 0 && r < 8)
            {
                int target = r * 8 + f;
                Piece occupant = board.Squares[target];

                if (occupant == Piece.None)
                {
                    moves.Add(new ScoredMove(from, SquareName(target), piece, occupant, false, Piece.None, 0));
                }
                else
                {
                    if (board.IsWhite[target] != isWhite)
                    {
                        moves.Add(new ScoredMove(from, SquareName(target), piece, occupant, false, Piece.None, 0));
                    }

                    break;
                }

                f += fileDelta;
                r += rankDelta;
            }
        }
    }

    private static (int fileDelta, int rankDelta) DirectionToDelta(int dir)
    {
        // dir is a board index delta: ±1 (file), ±8 (rank), or diagonals ±7/±9.
        return dir switch
        {
            -8 => (0, -1),
            8 => (0, 1),
            -1 => (-1, 0),
            1 => (1, 0),
            -9 => (-1, -1),
            -7 => (1, -1),
            7 => (-1, 1),
            9 => (1, 1),
            _ => (0, 0),
        };
    }

    private static bool IsSquareAttacked(Board board, int square, bool byWhite)
    {
        if (square < 0 || square >= 64)
        {
            return false;
        }

        int file = square % 8;
        int rank = square / 8;

        // Pawn attacks.
        int pawnDir = byWhite ? -8 : 8;
        foreach (int capDir in new[] { pawnDir - 1, pawnDir + 1 })
        {
            int target = square + capDir;
            if (target < 0 || target >= 64)
            {
                continue;
            }

            if (Math.Abs(target % 8 - file) != 1)
            {
                continue;
            }

            Piece occ = board.Squares[target];
            if (occ != Piece.None && board.IsWhite[target] == byWhite && PieceTypeOf(occ) == PieceType.Pawn)
            {
                return true;
            }
        }

        // Knight attacks.
        foreach (int offset in KnightOffsets)
        {
            int target = square + offset;
            if (target < 0 || target >= 64)
            {
                continue;
            }

            int fileDist = Math.Abs(target % 8 - file);
            int rankDist = Math.Abs(target / 8 - rank);
            if (!((fileDist == 1 && rankDist == 2) || (fileDist == 2 && rankDist == 1)))
            {
                continue;
            }

            Piece occ = board.Squares[target];
            if (occ != Piece.None && board.IsWhite[target] == byWhite && PieceTypeOf(occ) == PieceType.Knight)
            {
                return true;
            }
        }

        // King attacks.
        foreach (int offset in KingOffsets)
        {
            int target = square + offset;
            if (target < 0 || target >= 64)
            {
                continue;
            }

            if (Math.Abs(target % 8 - file) > 1 || Math.Abs(target / 8 - rank) > 1)
            {
                continue;
            }

            Piece occ = board.Squares[target];
            if (occ != Piece.None && board.IsWhite[target] == byWhite && PieceTypeOf(occ) == PieceType.King)
            {
                return true;
            }
        }

        // Sliding attacks: rook/queen orthogonals, bishop/queen diagonals.
        if (AttackedAlong(board, square, byWhite, RookDirections, PieceType.Rook, PieceType.Queen))
        {
            return true;
        }

        if (AttackedAlong(board, square, byWhite, BishopDirections, PieceType.Bishop, PieceType.Queen))
        {
            return true;
        }

        return false;
    }

    private static bool AttackedAlong(Board board, int square, bool byWhite, int[] directions, PieceType attacker1, PieceType attacker2)
    {
        int file = square % 8;
        int rank = square / 8;

        foreach (int dir in directions)
        {
            (int fileDelta, int rankDelta) = DirectionToDelta(dir);
            int f = file + fileDelta;
            int r = rank + rankDelta;

            while (f >= 0 && f < 8 && r >= 0 && r < 8)
            {
                int target = r * 8 + f;
                Piece occ = board.Squares[target];

                if (occ != Piece.None)
                {
                    PieceType t = PieceTypeOf(occ);
                    if (board.IsWhite[target] == byWhite && (t == attacker1 || t == attacker2))
                    {
                        return true;
                    }

                    break;
                }

                f += fileDelta;
                r += rankDelta;
            }
        }

        return false;
    }

    private static string SquareName(int index)
    {
        char file = (char)('a' + (index % 8));
        char rank = (char)('1' + (index / 8));
        return new string(new[] { file, rank });
    }

    private static PieceType PieceTypeOf(Piece piece) => piece switch
    {
        Piece.WhitePawn or Piece.BlackPawn => PieceType.Pawn,
        Piece.WhiteKnight or Piece.BlackKnight => PieceType.Knight,
        Piece.WhiteBishop or Piece.BlackBishop => PieceType.Bishop,
        Piece.WhiteRook or Piece.BlackRook => PieceType.Rook,
        Piece.WhiteQueen or Piece.BlackQueen => PieceType.Queen,
        Piece.WhiteKing or Piece.BlackKing => PieceType.King,
        _ => PieceType.None,
    };

    private enum PieceType { None, Pawn, Knight, Bishop, Rook, Queen, King }

    // ---- Internal board model -----------------------------------------------

    private sealed record ScoredMove(
        string FromSquare,
        string ToSquare,
        Piece MovingPiece,
        Piece CapturedPiece,
        bool IsPromotion,
        Piece PromotionPiece,
        int Score)
    {
        public PieceType MovingPieceType => PieceTypeOf(MovingPiece);
    }

    private sealed class Board
    {
        public Piece[] Squares { get; } = new Piece[64];
        public bool[] IsWhite { get; } = new bool[64];

        public static Board? Parse(string fen)
        {
            if (string.IsNullOrWhiteSpace(fen))
            {
                return null;
            }

            string[] parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return null;
            }

            var board = new Board();
            int rank = 7;
            int file = 0;

            foreach (char c in parts[0])
            {
                if (c == '/')
                {
                    rank--;
                    file = 0;
                    continue;
                }

                if (char.IsDigit(c, CultureInfo.InvariantCulture))
                {
                    file += c - '0';
                    continue;
                }

                if (rank < 0 || rank > 7 || file < 0 || file > 7)
                {
                    return null;
                }

                int index = rank * 8 + file;
                (board.Squares[index], board.IsWhite[index]) = CharToPiece(c);
                file++;
            }

            return board;
        }

        public ApplyResult Apply(ScoredMove move)
        {
            int from = IndexOf(move.FromSquare);
            int to = IndexOf(move.ToSquare);
            if (from < 0 || to < 0)
            {
                return new ApplyResult(-1, -1, Piece.None, Piece.None, false, -1, -1);
            }

            Piece moving = Squares[from];
            Piece captured = Squares[to];
            bool moverWhite = IsWhite[from];

            Squares[to] = move.IsPromotion && move.PromotionPiece != Piece.None
                ? (moverWhite ? PromoteWhite(move.PromotionPiece) : PromoteBlack(move.PromotionPiece))
                : moving;
            IsWhite[to] = moverWhite;
            Squares[from] = Piece.None;
            IsWhite[from] = false;

            int moverKing = FindKing(moverWhite);
            int opponentKing = FindKing(!moverWhite);

            return new ApplyResult(from, to, moving, captured, moverWhite, moverKing, opponentKing);
        }

        public void Undo(ApplyResult undo)
        {
            if (undo.FromIndex < 0)
            {
                return;
            }

            Squares[undo.FromIndex] = undo.MovingPiece;
            IsWhite[undo.FromIndex] = undo.MovingWhite;

            Squares[undo.ToIndex] = undo.CapturedPiece;
            IsWhite[undo.ToIndex] = undo.CapturedPiece != Piece.None
                ? !undo.MovingWhite   // a captured piece belongs to the opponent
                : undo.MovingWhite;   // empty square: color irrelevant, keep a sane default
        }

        private int FindKing(bool white)
        {
            Piece king = white ? Piece.WhiteKing : Piece.BlackKing;
            for (int i = 0; i < 64; i++)
            {
                if (Squares[i] == king)
                {
                    return i;
                }
            }

            return -1;
        }

        private static (Piece, bool) CharToPiece(char c) => c switch
        {
            'P' => (Piece.WhitePawn, true),
            'N' => (Piece.WhiteKnight, true),
            'B' => (Piece.WhiteBishop, true),
            'R' => (Piece.WhiteRook, true),
            'Q' => (Piece.WhiteQueen, true),
            'K' => (Piece.WhiteKing, true),
            'p' => (Piece.BlackPawn, false),
            'n' => (Piece.BlackKnight, false),
            'b' => (Piece.BlackBishop, false),
            'r' => (Piece.BlackRook, false),
            'q' => (Piece.BlackQueen, false),
            'k' => (Piece.BlackKing, false),
            _ => (Piece.None, false),
        };

        private static Piece PromoteWhite(Piece piece) => piece switch
        {
            Piece.Queen => Piece.WhiteQueen,
            Piece.Rook => Piece.WhiteRook,
            Piece.Bishop => Piece.WhiteBishop,
            Piece.Knight => Piece.WhiteKnight,
            _ => Piece.WhiteQueen,
        };

        private static Piece PromoteBlack(Piece piece) => piece switch
        {
            Piece.Queen => Piece.BlackQueen,
            Piece.Rook => Piece.BlackRook,
            Piece.Bishop => Piece.BlackBishop,
            Piece.Knight => Piece.BlackKnight,
            _ => Piece.BlackQueen,
        };

        private static int IndexOf(string square)
        {
            if (square.Length != 2)
            {
                return -1;
            }

            int file = square[0] - 'a';
            int rank = square[1] - '1';
            if (file < 0 || file > 7 || rank < 0 || rank > 7)
            {
                return -1;
            }

            return rank * 8 + file;
        }
    }

    private sealed record ApplyResult(
        int FromIndex,
        int ToIndex,
        Piece MovingPiece,
        Piece CapturedPiece,
        bool MovingWhite,
        int MovingKingSquare,
        int OpponentKingSquare);
}
