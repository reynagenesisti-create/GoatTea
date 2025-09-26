using System;
using System.Collections.Generic;
using System.Numerics;

// Accurate (not optimized) move generator. Generates pseudolegal moves then filters
// those that leave the moving side's king in check by making the move and verifying.
public static class MoveGenerator
{
    // Reusable temp list to avoid allocations for short-lived generation
    [ThreadStatic]
    private static List<Move>? tempMoves;

    private static readonly ulong[] KnightAttacks = new ulong[64];
    private static readonly ulong[] KingAttacks = new ulong[64];

    static MoveGenerator()
    {
        // build attack tables
        for (int sq = 0; sq < 64; sq++)
        {
            int r = sq / 8, f = sq % 8;
            ulong kMask = 0UL;
            for (int dr = -1; dr <= 1; dr++)
                for (int df = -1; df <= 1; df++)
                {
                    if (dr == 0 && df == 0) continue;
                    int nr = r + dr, nf = f + df;
                    if (nr >= 0 && nr < 8 && nf >= 0 && nf < 8)
                        kMask |= 1UL << (nr * 8 + nf);
                }
            KingAttacks[sq] = kMask;

            ulong nMask = 0UL;
            int[] ndr = new int[] { -1, 1, -2, 2, -2, 2, -1, 1 };
            int[] ndf = new int[] { -2, -2, -1, -1, 1, 1, 2, 2 };
            for (int i = 0; i < 8; i++)
            {
                int nr = r + ndr[i], nf = f + ndf[i];
                if (nr >= 0 && nr < 8 && nf >= 0 && nf < 8)
                    nMask |= 1UL << (nr * 8 + nf);
            }
            KnightAttacks[sq] = nMask;
        }
    }

    public static List<Move> GenerateLegalMoves(Board board)
    {
        var moves = new List<Move>();
        GenerateLegalMoves(board, moves);
        return moves;
    }

    public static void GenerateLegalMoves(Board board, List<Move> outMoves)
    {
        outMoves.Clear();
        if (tempMoves == null) tempMoves = new List<Move>(256);
        tempMoves.Clear();
        Color side = board.SideToMove;
        Color enemy = side == Color.White ? Color.Black : Color.White;

        // iterate piece bitboards for the side - avoids scanning 64 squares
        for (PieceType pt = PieceType.Pawn; pt <= PieceType.King; pt++)
        {
            ulong bb = board.GetBitboard(side, pt);
            while (bb != 0UL)
            {
                int sq = BitOperations.TrailingZeroCount(bb);
                switch (pt)
                {
                    case PieceType.Pawn: GeneratePawnMoves(board, sq, side, tempMoves); break;
                    case PieceType.Knight: GenerateKnightMoves(board, sq, side, tempMoves); break;
                    case PieceType.Bishop: GenerateSlidingMoves(board, sq, pt, side, tempMoves, new (int dr, int df)[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }); break;
                    case PieceType.Rook: GenerateSlidingMoves(board, sq, pt, side, tempMoves, new (int dr, int df)[] { (1, 0), (-1, 0), (0, 1), (0, -1) }); break;
                    case PieceType.Queen: GenerateSlidingMoves(board, sq, pt, side, tempMoves, new (int dr, int df)[] { (1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1) }); break;
                    case PieceType.King: GenerateKingMoves(board, sq, side, tempMoves); break;
                }
                bb &= bb - 1;
            }
        }

        // Filter illegal moves by making them and checking king safety
        foreach (var mv in tempMoves)
        {
            try
            {
                board.MakeMove(mv);
                // moved side is opposite of current SideToMove
                Color movedSide = board.SideToMove == Color.White ? Color.Black : Color.White;
                // find king square by scanning king bitboard
                ulong kingBb = board.GetBitboard(movedSide, PieceType.King);
                int kingSq = kingBb != 0 ? BitOperations.TrailingZeroCount(kingBb) : -1;
                bool inCheck = kingSq == -1 || IsSquareAttacked(board, kingSq, movedSide == Color.White ? Color.Black : Color.White);
                if (!inCheck) outMoves.Add(mv);
            }
            finally
            {
                board.UnmakeMove();
            }
        }
    }

    private static void GeneratePawnMoves(Board board, int from, Color side, List<Move> moves)
    {
        int dir = side == Color.White ? 1 : -1;
        int rank = from / 8;
        int file = from % 8;

        int oneForward = from + dir * 8;
        bool forwardEmpty = oneForward >= 0 && oneForward < 64 && board.GetPieceIndexAt(oneForward) == -1;

        // promotions when reaching last rank
        bool promotionRank = (side == Color.White && oneForward >= 56) || (side == Color.Black && oneForward <= 7);

        if (forwardEmpty)
        {
            if (promotionRank)
            {
                moves.Add(new Move(from, oneForward, PieceType.Pawn, false, false, true, PieceType.Queen));
                moves.Add(new Move(from, oneForward, PieceType.Pawn, false, false, true, PieceType.Rook));
                moves.Add(new Move(from, oneForward, PieceType.Pawn, false, false, true, PieceType.Bishop));
                moves.Add(new Move(from, oneForward, PieceType.Pawn, false, false, true, PieceType.Knight));
            }
            else
            {
                moves.Add(new Move(from, oneForward, PieceType.Pawn));
                // two-square push
                bool startRank = (side == Color.White && rank == 1) || (side == Color.Black && rank == 6);
                int twoForward = from + dir * 16;
                if (startRank && twoForward >= 0 && twoForward < 64 && board.GetPieceIndexAt(twoForward) == -1)
                {
                    moves.Add(new Move(from, twoForward, PieceType.Pawn));
                }
            }
        }

        // captures
        int[] capFiles = new int[] { file - 1, file + 1 };
        foreach (var f in capFiles)
        {
            if (f < 0 || f > 7) continue;
            int to = (from / 8 + dir) * 8 + f;
            if (to < 0 || to >= 64) continue;
            int target = board.GetPieceIndexAt(to);
            bool isCapture = target != -1 && ((int)target / 6) != (int)side;

            // en-passant
            bool isEnPassant = (to == board.EnPassantSquare);

            if (isCapture || isEnPassant)
            {
                bool promo = (side == Color.White && to >= 56) || (side == Color.Black && to <= 7);
                if (promo)
                {
                    moves.Add(new Move(from, to, PieceType.Pawn, true, isEnPassant, true, PieceType.Queen));
                    moves.Add(new Move(from, to, PieceType.Pawn, true, isEnPassant, true, PieceType.Rook));
                    moves.Add(new Move(from, to, PieceType.Pawn, true, isEnPassant, true, PieceType.Bishop));
                    moves.Add(new Move(from, to, PieceType.Pawn, true, isEnPassant, true, PieceType.Knight));
                }
                else
                {
                    moves.Add(new Move(from, to, PieceType.Pawn, isCapture, isEnPassant));
                }
            }
        }
    }

    private static void GenerateKnightMoves(Board board, int from, Color side, List<Move> moves)
    {
        int[] df = new int[] { -2, -2, -1, -1, 1, 1, 2, 2 };
        int[] dr = new int[] { -1, 1, -2, 2, -2, 2, -1, 1 };
        int r = from / 8, f = from % 8;
        for (int i = 0; i < 8; i++)
        {
            int nr = r + dr[i], nf = f + df[i];
            if (nr < 0 || nr > 7 || nf < 0 || nf > 7) continue;
            int to = nr * 8 + nf;
            int t = board.GetPieceIndexAt(to);
            if (t == -1 || ((int)t / 6) != (int)side)
            {
                moves.Add(new Move(from, to, PieceType.Knight, t != -1));
            }
        }
    }

    private static void GenerateSlidingMoves(Board board, int from, PieceType pt, Color side, List<Move> moves, (int dr, int df)[] directions)
    {
        int r0 = from / 8, f0 = from % 8;
        foreach (var d in directions)
        {
            int r = r0 + d.dr, f = f0 + d.df;
            while (r >= 0 && r < 8 && f >= 0 && f < 8)
            {
                int to = r * 8 + f;
                int t = board.GetPieceIndexAt(to);
                if (t == -1)
                {
                    moves.Add(new Move(from, to, pt));
                }
                else
                {
                    if (((int)t / 6) != (int)side)
                        moves.Add(new Move(from, to, pt, true));
                    break; // blocked
                }
                r += d.dr; f += d.df;
            }
        }
    }

    private static void GenerateKingMoves(Board board, int from, Color side, List<Move> moves)
    {
        int r0 = from / 8, f0 = from % 8;
        for (int dr = -1; dr <= 1; dr++)
            for (int df = -1; df <= 1; df++)
            {
                if (dr == 0 && df == 0) continue;
                int r = r0 + dr, f = f0 + df;
                if (r < 0 || r > 7 || f < 0 || f > 7) continue;
                int to = r * 8 + f;
                int t = board.GetPieceIndexAt(to);
                if (t == -1 || ((int)t / 6) != (int)side)
                    moves.Add(new Move(from, to, PieceType.King, t != -1));
            }

        // Castling
        int rights = board.CastlingRights;
        if (side == Color.White)
        {
            // king side
            if ((rights & 1) != 0)
            {
                if (board.GetPieceIndexAt(5) == -1 && board.GetPieceIndexAt(6) == -1)
                {
                    // ensure not in check and squares not attacked will be checked by legality filter
                    moves.Add(new Move(from, 6, PieceType.King, false, false, false, null, true));
                }
            }
            // queen side
            if ((rights & 2) != 0)
            {
                if (board.GetPieceIndexAt(1) == -1 && board.GetPieceIndexAt(2) == -1 && board.GetPieceIndexAt(3) == -1)
                {
                    moves.Add(new Move(from, 2, PieceType.King, false, false, false, null, true));
                }
            }
        }
        else
        {
            if ((rights & 4) != 0)
            {
                if (board.GetPieceIndexAt(61) == -1 && board.GetPieceIndexAt(62) == -1)
                    moves.Add(new Move(from, 62, PieceType.King, false, false, false, null, true));
            }
            if ((rights & 8) != 0)
            {
                if (board.GetPieceIndexAt(57) == -1 && board.GetPieceIndexAt(58) == -1 && board.GetPieceIndexAt(59) == -1)
                    moves.Add(new Move(from, 58, PieceType.King, false, false, false, null, true));
            }
        }
    }

    private static bool IsSquareAttacked(Board board, int square, Color byColor)
    {
        // pawn attacks
        if (byColor == Color.White)
        {
            // white pawns attack from square-7 (if file>0) and square-9 (if file<7)
            int f = square % 8;
            if (f > 0)
            {
                int from = square - 7;
                if (from >= 0 && from < 64)
                {
                    int pi = board.GetPieceIndexAt(from);
                    if (pi != -1 && pi / 6 == (int)Color.White && (PieceType)(pi % 6) == PieceType.Pawn) return true;
                }
            }
            if (f < 7)
            {
                int from = square - 9;
                if (from >= 0 && from < 64)
                {
                    int pi = board.GetPieceIndexAt(from);
                    if (pi != -1 && pi / 6 == (int)Color.White && (PieceType)(pi % 6) == PieceType.Pawn) return true;
                }
            }
        }
        else
        {
            int f = square % 8;
            if (f < 7)
            {
                int from = square + 7;
                if (from >= 0 && from < 64)
                {
                    int pi = board.GetPieceIndexAt(from);
                    if (pi != -1 && pi / 6 == (int)Color.Black && (PieceType)(pi % 6) == PieceType.Pawn) return true;
                }
            }
            if (f > 0)
            {
                int from = square + 9;
                if (from >= 0 && from < 64)
                {
                    int pi = board.GetPieceIndexAt(from);
                    if (pi != -1 && pi / 6 == (int)Color.Black && (PieceType)(pi % 6) == PieceType.Pawn) return true;
                }
            }
        }

        // knight attacks
        ulong knights = board.GetBitboard(byColor, PieceType.Knight);
        if ((knights & KnightAttacks[square]) != 0UL) return true;

        // king attacks
        ulong kings = board.GetBitboard(byColor, PieceType.King);
        if ((kings & KingAttacks[square]) != 0UL) return true;

        // sliding pieces: bishops/queens on diagonals, rooks/queens on ranks/files
        ulong occ = board.OccupancyAll;

        // directional rays: (dr,df) pairs
        (int dr, int df)[] diag = new (int, int)[] { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        (int dr, int df)[] orth = new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        int sr = square / 8, sf = square % 8;
        // diagonals
        foreach (var d in diag)
        {
            int r = sr + d.dr, f = sf + d.df;
            while (r >= 0 && r < 8 && f >= 0 && f < 8)
            {
                int s = r * 8 + f;
                int pi = board.GetPieceIndexAt(s);
                if (pi != -1)
                {
                    Color c = (Color)(pi / 6);
                    PieceType pt = (PieceType)(pi % 6);
                    if (c == byColor && (pt == PieceType.Bishop || pt == PieceType.Queen)) return true;
                    break;
                }
                r += d.dr; f += d.df;
            }
        }

        // orthogonals
        foreach (var d in orth)
        {
            int r = sr + d.dr, f = sf + d.df;
            while (r >= 0 && r < 8 && f >= 0 && f < 8)
            {
                int s = r * 8 + f;
                int pi = board.GetPieceIndexAt(s);
                if (pi != -1)
                {
                    Color c = (Color)(pi / 6);
                    PieceType pt = (PieceType)(pi % 6);
                    if (c == byColor && (pt == PieceType.Rook || pt == PieceType.Queen)) return true;
                    break;
                }
                r += d.dr; f += d.df;
            }
        }

        return false;
    }

    private static bool AttacksSquare(Board board, int from, PieceType pt, Color side, int target)
    {
        int fr = from / 8, ff = from % 8;
        int tr = target / 8, tf = target % 8;
        int dr = tr - fr, df = tf - ff;

        switch (pt)
        {
            case PieceType.Pawn:
                if (side == Color.White)
                {
                    // white pawn captures to from+7 or from+9
                    if ((from + 7) == target && ff > 0) return true;
                    if ((from + 9) == target && ff < 7) return true;
                }
                else
                {
                    if ((from - 7) == target && ff < 7) return true;
                    if ((from - 9) == target && ff > 0) return true;
                }
                return false;
            case PieceType.Knight:
                int[] ndf = new int[] { -2, -2, -1, -1, 1, 1, 2, 2 };
                int[] ndr = new int[] { -1, 1, -2, 2, -2, 2, -1, 1 };
                for (int i = 0; i < 8; i++) if (fr + ndr[i] == tr && ff + ndf[i] == tf) return true;
                return false;
            case PieceType.Bishop:
                if (Math.Abs(dr) == Math.Abs(df) && dr != 0)
                {
                    int stepR = dr > 0 ? 1 : -1;
                    int stepF = df > 0 ? 1 : -1;
                    int r = fr + stepR, f = ff + stepF;
                    while (r != tr && f != tf)
                    {
                        if (board.GetPieceIndexAt(r * 8 + f) != -1) return false;
                        r += stepR; f += stepF;
                    }
                    return true;
                }
                return false;
            case PieceType.Rook:
                if ((dr == 0) ^ (df == 0))
                {
                    int stepR = dr == 0 ? 0 : (dr > 0 ? 1 : -1);
                    int stepF = df == 0 ? 0 : (df > 0 ? 1 : -1);
                    int r = fr + stepR, f = ff + stepF;
                    while (r != tr || f != tf)
                    {
                        if (board.GetPieceIndexAt(r * 8 + f) != -1) return false;
                        r += stepR; f += stepF;
                    }
                    return true;
                }
                return false;
            case PieceType.Queen:
                // combines rook and bishop
                if (Math.Abs(dr) == Math.Abs(df) && dr != 0)
                {
                    int stepR = dr > 0 ? 1 : -1;
                    int stepF = df > 0 ? 1 : -1;
                    int r = fr + stepR, f = ff + stepF;
                    while (r != tr && f != tf)
                    {
                        if (board.GetPieceIndexAt(r * 8 + f) != -1) return false;
                        r += stepR; f += stepF;
                    }
                    return true;
                }
                if ((dr == 0) ^ (df == 0))
                {
                    int stepR = dr == 0 ? 0 : (dr > 0 ? 1 : -1);
                    int stepF = df == 0 ? 0 : (df > 0 ? 1 : -1);
                    int r = fr + stepR, f = ff + stepF;
                    while (r != tr || f != tf)
                    {
                        if (board.GetPieceIndexAt(r * 8 + f) != -1) return false;
                        r += stepR; f += stepF;
                    }
                    return true;
                }
                return false;
            case PieceType.King:
                return Math.Max(Math.Abs(dr), Math.Abs(df)) == 1;
        }
        return false;
    }
}
