using System;
using System.Collections.Generic;

// Bitboard-based board representation with make/unmake move support.
// This implementation keeps 12 piece bitboards (White: P,N,B,R,Q,K then Black same order)
// and maintains occupancy bitboards. MakeMove records a full copy of the 12 bitboards
// plus auxiliary state on a history stack so UnmakeMove can restore the exact prior state.
public enum Color { White = 0, Black = 1 }
public enum PieceType { Pawn = 0, Knight = 1, Bishop = 2, Rook = 3, Queen = 4, King = 5 }

public readonly struct Move
{
    public readonly int From; // 0..63
    public readonly int To;   // 0..63
    public readonly PieceType Piece; // moving piece type
    public readonly bool IsCapture;
    public readonly bool IsEnPassant;
    public readonly bool IsPromotion;
    public readonly PieceType? PromotionPiece; // if IsPromotion
    public readonly bool IsCastle;

    public Move(int from, int to, PieceType piece, bool isCapture = false, bool isEnPassant = false, bool isPromotion = false, PieceType? promotionPiece = null, bool isCastle = false)
    {
        From = from;
        To = to;
        Piece = piece;
        IsCapture = isCapture;
        IsEnPassant = isEnPassant;
        IsPromotion = isPromotion;
        PromotionPiece = promotionPiece;
        IsCastle = isCastle;
    }

    public override string ToString()
    {
        return $"{From}->{To} {(IsPromotion && PromotionPiece.HasValue ? PromotionPiece.Value.ToString() : Piece.ToString())}{(IsCapture ? "x" : "")}{(IsEnPassant ? " e.p." : "")}{(IsCastle ? " castle" : "")}";
    }
}

internal class Undo
{
    public ulong[] BitboardsBefore; // length 12
    public ulong OccupancyWhiteBefore;
    public ulong OccupancyBlackBefore;
    public ulong OccupancyAllBefore;
    public int CastlingRightsBefore;
    public int EnPassantBefore;
    public int HalfmoveClockBefore;
    public int FullmoveNumberBefore;
    public Color SideToMoveBefore;

    public Undo(int boards)
    {
        BitboardsBefore = new ulong[boards];
    }
}

public class Board
{
    // 0..5 = white Pawn..King, 6..11 = black Pawn..King
    private const int PieceTypesPerColor = 6;
    private const int TotalPieceBitboards = PieceTypesPerColor * 2;

    private readonly ulong[] bitboards = new ulong[TotalPieceBitboards];

    private ulong occupancyWhite;
    private ulong occupancyBlack;
    private ulong occupancyAll;

    public Color SideToMove { get; private set; } = Color.White;
    // Castling rights: bit0 = White K, bit1 = White Q, bit2 = Black K, bit3 = Black Q
    public int CastlingRights { get; private set; } = 0;
    // en passant square index 0..63 or -1 for none
    public int EnPassantSquare { get; private set; } = -1;
    public int HalfmoveClock { get; private set; } = 0;
    public int FullmoveNumber { get; private set; } = 1;

    private readonly Stack<Undo> history = new Stack<Undo>();

    public Board()
    {
        Clear();
    }

    public void Clear()
    {
        Array.Clear(bitboards, 0, bitboards.Length);
        occupancyWhite = occupancyBlack = occupancyAll = 0UL;
        SideToMove = Color.White;
        CastlingRights = 0;
        EnPassantSquare = -1;
        HalfmoveClock = 0;
        FullmoveNumber = 1;
        history.Clear();
    }

    private static int IndexOf(Color color, PieceType piece)
    {
        return ((int)color * PieceTypesPerColor) + (int)piece;
    }

    public ulong GetBitboard(Color color, PieceType piece)
    {
        return bitboards[IndexOf(color, piece)];
    }

    private void RecalcOccupancies()
    {
        occupancyWhite = occupancyBlack = 0UL;
        for (int p = 0; p < PieceTypesPerColor; p++)
            occupancyWhite |= bitboards[IndexOf(Color.White, (PieceType)p)];
        for (int p = 0; p < PieceTypesPerColor; p++)
            occupancyBlack |= bitboards[IndexOf(Color.Black, (PieceType)p)];
        occupancyAll = occupancyWhite | occupancyBlack;
    }

    // FEN loader - supports placement, side to move, castling rights, en-passant, halfmove/fullmove
    public void SetPositionFromFen(string fen)
    {
        Clear();
        var parts = fen.Split(' ');
        if (parts.Length < 4)
            throw new ArgumentException("Invalid FEN: not enough fields");

        // placement
        int sq = 56; // start at a8
        foreach (char c in parts[0])
        {
            if (c == '/') { sq -= 16; continue; }
            if (char.IsDigit(c)) { sq += c - '0'; continue; }
            Color color = char.IsUpper(c) ? Color.White : Color.Black;
            PieceType pt = c switch
            {
                'P' or 'p' => PieceType.Pawn,
                'N' or 'n' => PieceType.Knight,
                'B' or 'b' => PieceType.Bishop,
                'R' or 'r' => PieceType.Rook,
                'Q' or 'q' => PieceType.Queen,
                'K' or 'k' => PieceType.King,
                _ => throw new ArgumentException($"Invalid FEN piece char: {c}")
            };
            bitboards[IndexOf(color, pt)] |= 1UL << sq;
            sq++;
        }

        RecalcOccupancies();

        // side to move
        SideToMove = parts[1] == "w" ? Color.White : Color.Black;

        // castling rights
        CastlingRights = 0;
        if (parts[2].Contains('K')) CastlingRights |= 1;
        if (parts[2].Contains('Q')) CastlingRights |= 2;
        if (parts[2].Contains('k')) CastlingRights |= 4;
        if (parts[2].Contains('q')) CastlingRights |= 8;

        // en passant
        EnPassantSquare = -1;
        if (parts[3] != "-")
        {
            EnPassantSquare = AlgebraicToSquare(parts[3]);
        }

        if (parts.Length > 4 && int.TryParse(parts[4], out int hm)) HalfmoveClock = hm; else HalfmoveClock = 0;
        if (parts.Length > 5 && int.TryParse(parts[5], out int fm)) FullmoveNumber = fm; else FullmoveNumber = 1;
    }

    public static int AlgebraicToSquare(string sq)
    {
        if (sq.Length != 2) throw new ArgumentException("Invalid square");
        int file = sq[0] - 'a';
        int rank = sq[1] - '1';
        return rank * 8 + file;
    }

    public static string SquareToAlgebraic(int square)
    {
        int file = square % 8;
        int rank = square / 8;
        return ((char)('a' + file)).ToString() + ((char)('1' + rank)).ToString();
    }

    // Returns -1 if no piece; otherwise returns bitboard index (0..11)
    public int GetPieceIndexAt(int square)
    {
        ulong mask = 1UL << square;
        for (int i = 0; i < bitboards.Length; i++)
            if ((bitboards[i] & mask) != 0) return i;
        return -1;
    }

    // MakeMove stores a copy of current state and applies the move; UnmakeMove restores state.
    public void MakeMove(Move move)
    {
        // push undo with copies
        var u = new Undo(TotalPieceBitboards);
        Array.Copy(bitboards, u.BitboardsBefore, bitboards.Length);
        u.OccupancyWhiteBefore = occupancyWhite;
        u.OccupancyBlackBefore = occupancyBlack;
        u.OccupancyAllBefore = occupancyAll;
        u.CastlingRightsBefore = CastlingRights;
        u.EnPassantBefore = EnPassantSquare;
        u.HalfmoveClockBefore = HalfmoveClock;
        u.FullmoveNumberBefore = FullmoveNumber;
        u.SideToMoveBefore = SideToMove;
        history.Push(u);

        // Reset en-passant; set if this move is a pawn double push
        EnPassantSquare = -1;

        int movingIndex = IndexOf(SideToMove, move.Piece);
        ulong fromBit = 1UL << move.From;
        ulong toBit = 1UL << move.To;

        // Remove moving piece from 'from'
        bitboards[movingIndex] &= ~fromBit;

        // Handle captures (normal capture or en-passant)
        if (move.IsEnPassant)
        {
            // captured pawn is behind the to-square
            int capSq = (SideToMove == Color.White) ? move.To - 8 : move.To + 8;
            ulong capBit = 1UL << capSq;
            // captured pawn is opposite color pawn
            bitboards[IndexOf(Opposite(SideToMove), PieceType.Pawn)] &= ~capBit;
        }
        else if (move.IsCapture)
        {
            // find which piece occupies 'to'
            int capIndex = GetPieceIndexAt(move.To);
            if (capIndex >= 0)
                bitboards[capIndex] &= ~toBit;
        }

        // Promotions: remove pawn and add promoted piece
        if (move.IsPromotion && move.PromotionPiece.HasValue)
        {
            // pawn removed from from (already removed)
            // add promoted piece on 'to'
            bitboards[IndexOf(SideToMove, move.PromotionPiece.Value)] |= toBit;
        }
        else
        {
            // normal move: place moving piece on 'to'
            bitboards[movingIndex] |= toBit;
        }

        // Castling handling: move rook as well
        if (move.IsCastle)
        {
            // White castling from e1 (4) to g1(6) or c1(2)
            if (SideToMove == Color.White)
            {
                if (move.To == 6) // king side
                {
                    // move rook h1(7) to f1(5)
                    bitboards[IndexOf(Color.White, PieceType.Rook)] &= ~(1UL << 7);
                    bitboards[IndexOf(Color.White, PieceType.Rook)] |= 1UL << 5;
                }
                else if (move.To == 2) // queen side
                {
                    // move rook a1(0) to d1(3)
                    bitboards[IndexOf(Color.White, PieceType.Rook)] &= ~(1UL << 0);
                    bitboards[IndexOf(Color.White, PieceType.Rook)] |= 1UL << 3;
                }
            }
            else
            {
                if (move.To == 62) // black kingside (e8->g8)
                {
                    bitboards[IndexOf(Color.Black, PieceType.Rook)] &= ~(1UL << 63);
                    bitboards[IndexOf(Color.Black, PieceType.Rook)] |= 1UL << 61;
                }
                else if (move.To == 58) // black queenside (e8->c8)
                {
                    bitboards[IndexOf(Color.Black, PieceType.Rook)] &= ~(1UL << 56);
                    bitboards[IndexOf(Color.Black, PieceType.Rook)] |= 1UL << 59;
                }
            }
        }

        // Update castling rights: if king or rook moved or rook captured
        UpdateCastlingRightsOnMove(move);

        // Update occupancies
        RecalcOccupancies();

        // If the move was a pawn double-push, set EnPassantSquare to the square behind the pawn
        if (move.Piece == PieceType.Pawn && Math.Abs(move.To - move.From) == 16)
        {
            // en-passant square is the square between from and to
            EnPassantSquare = (move.From + move.To) / 2;
        }

        // Update halfmove/fullmove counters
        if (move.Piece == PieceType.Pawn || move.IsCapture) HalfmoveClock = 0; else HalfmoveClock++;
        if (SideToMove == Color.Black) FullmoveNumber++;

        // flip side
        SideToMove = Opposite(SideToMove);
    }

    private void UpdateCastlingRightsOnMove(Move move)
    {
        // If king moves, clear both castling rights for that color
        if (move.Piece == PieceType.King)
        {
            if (SideToMove == Color.White) CastlingRights &= ~3; else CastlingRights &= ~12;
        }
        // If rook moves from initial squares, clear that rook's castling right
        if (move.Piece == PieceType.Rook)
        {
            if (SideToMove == Color.White)
            {
                if (move.From == 0) CastlingRights &= ~2; // a1
                if (move.From == 7) CastlingRights &= ~1; // h1
            }
            else
            {
                if (move.From == 56) CastlingRights &= ~8; // a8
                if (move.From == 63) CastlingRights &= ~4; // h8
            }
        }
        // If a rook was captured on initial squares, clear that castling right
        if (move.IsCapture && !move.IsEnPassant)
        {
            int capIndex = GetPieceIndexAt(move.To);
            // capIndex is -1 if capture was already applied; but we called this after captures were removed.
            // Instead detect capture square matching rook initial squares by inspecting move.To and original side.
            // For simplicity: if capture square equals one of the rook starting squares, clear rights accordingly.
            if (move.To == 0) CastlingRights &= ~2;
            if (move.To == 7) CastlingRights &= ~1;
            if (move.To == 56) CastlingRights &= ~8;
            if (move.To == 63) CastlingRights &= ~4;
        }
    }

    public void UnmakeMove()
    {
        if (history.Count == 0) throw new InvalidOperationException("No move to unmake");
        var u = history.Pop();
        Array.Copy(u.BitboardsBefore, bitboards, bitboards.Length);
        occupancyWhite = u.OccupancyWhiteBefore;
        occupancyBlack = u.OccupancyBlackBefore;
        occupancyAll = u.OccupancyAllBefore;
        CastlingRights = u.CastlingRightsBefore;
        EnPassantSquare = u.EnPassantBefore;
        HalfmoveClock = u.HalfmoveClockBefore;
        FullmoveNumber = u.FullmoveNumberBefore;
        SideToMove = u.SideToMoveBefore;
    }

    private static Color Opposite(Color c) => c == Color.White ? Color.Black : Color.White;

    // Utility to print bitboard (for debugging)
    public static string BitboardToString(ulong b)
    {
        char[] sq = new char[64];
        for (int i = 0; i < 64; i++) sq[i] = ((b >> i) & 1UL) != 0 ? '1' : '.';
        // print ranks 8->1
        var rows = new List<string>();
        for (int r = 7; r >= 0; r--)
        {
            var row = new char[8];
            for (int f = 0; f < 8; f++) row[f] = sq[r * 8 + f];
            rows.Add(new string(row));
        }
        return string.Join("\n", rows);
    }
}
