using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

// Minimal UCI skeleton. Expand this into a full engine later.
class Program
{
    static readonly string EngineName = "GOATea";
    static readonly string EngineAuthor = "You";

    static bool isRunning = true;
    // Global board for the engine
    static Board board = new Board();

    static void Main(string[] args)
    {
        board.SetPositionFromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        // Read lines from stdin and respond according to the UCI protocol.
        // This skeleton implements the basic handshake and dummy handlers.
        while (isRunning && Console.In.Peek() >= 0)
        {
            var line = Console.ReadLine();
            if (line == null)
                break;
            line = line.Trim();
            if (line.Length == 0)
                continue;

            // Split command and args
            var parts = SplitOnce(line);
            var cmd = parts.Item1;
            var rest = parts.Item2;

            switch (cmd)
            {
                case "uci":
                    HandleUci();
                    break;
                case "isready":
                    HandleIsReady();
                    break;
                case "setoption":
                    HandleSetOption(rest);
                    break;
                case "ucinewgame":
                    HandleUciNewGame();
                    break;
                case "position":
                    HandlePosition(rest);
                    break;
                case "go":
                    HandleGo(rest);
                    break;
                case "stop":
                    HandleStop();
                    break;
                case "debug":
                    HandleDebug(rest);
                    break;
                case "perft":
                    HandlePerft(rest);
                    break;

                case "quit":
                    HandleQuit();
                    break;
                case "protocol":
                case "uai":
                    // unsupported legacy commands
                    Console.WriteLine("unknown command");
                    break;
                default:
                    // For other commands, just echo unknown to remain robust for GUIs that probe.
                    Console.WriteLine($"unknown command: {cmd}");
                    break;
            }
        }
    }

    static Tuple<string, string> SplitOnce(string input)
    {
        int idx = input.IndexOf(' ');
        if (idx < 0)
            return Tuple.Create(input, string.Empty);
        return Tuple.Create(input.Substring(0, idx), input.Substring(idx + 1));
    }


    static void HandleUciNewGame()
    {
        // Reset internal state for a new game. Currently dummy.
        board.SetPositionFromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        Console.WriteLine("info string new game initialized (dummy)");
    }

    static void HandleSetOption(string rest)
    {
        // Minimal parsing: support "name X value Y" but keep it dummy.
        Console.WriteLine("info string setoption received: " + rest);
    }

    static void HandleIsReady()
    {
        // If initialization is asynchronous, wait until ready. For now immediate.
        Console.WriteLine("readyok");
    }

    static void HandleUci()
    {
        Console.WriteLine($"id name {EngineName}");
        Console.WriteLine($"id author {EngineAuthor}");
        // engine options would go here, e.g. Hash, Threads, etc.
        Console.WriteLine("option name Hash type spin default 16 min 1 max 1024");
        Console.WriteLine("option name Threads type spin default 1 min 1 max 64");
        Console.WriteLine("option name Ponder type check default false");
        Console.WriteLine("uciok");
    }


    static void HandlePosition(string rest)
    {
        // Accepts: position startpos | fen <fen> [moves ...]
        // examples:
        // position startpos
        // position startpos moves e2e4 e7e5
        // position fen <fen>  or position fen <fen> moves ...
        Console.WriteLine("info string position received: " + rest);

        if (string.IsNullOrWhiteSpace(rest)) return;
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int idx = 0;
        if (parts[idx] == "startpos")
        {
            // standard start position FEN
            board.SetPositionFromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            idx++;
        }
        else if (parts[idx] == "fen")
        {
            // the FEN may contain spaces: we need 6 fields
            // collect next 6 tokens (or fewer if malformed)
            var fenParts = new List<string>();
            idx++;
            for (int f = 0; f < 6 && idx < parts.Length; f++, idx++) fenParts.Add(parts[idx]);
            var fen = string.Join(' ', fenParts);
            try
            {
                board.SetPositionFromFen(fen);
            }
            catch (Exception ex)
            {
                Console.WriteLine("info string invalid FEN: " + ex.Message);
                return;
            }
        }

        // optional moves
        if (idx < parts.Length && parts[idx] == "moves")
        {
            idx++;
            for (; idx < parts.Length; idx++)
            {
                var mv = parts[idx];
                // basic algebraic long move parser: e2e4, e7e8q, e5d6 (en-passant), e1g1 (castle)
                try
                {
                    var move = ParseUciMove(mv);
                    board.MakeMove(move);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("info string invalid move in moves list: " + mv + " (" + ex.Message + ")");
                }
            }
        }
    }

    static Move ParseUciMove(string uci)
    {
        // uci like e2e4, e7e8q (promotion), e1g1 (castle), e5d6 (en-passant candidate)
        if (uci.Length < 4) throw new ArgumentException("move string too short");
        int from = Board.AlgebraicToSquare(uci.Substring(0, 2));
        int to = Board.AlgebraicToSquare(uci.Substring(2, 2));

        // determine moving piece by checking which piece exists on 'from'
        int idx = board.GetPieceIndexAt(from);
        if (idx < 0) throw new ArgumentException("no piece on from-square");
        Color side = (Color)(idx / 6);
        PieceType piece = (PieceType)(idx % 6);

        bool isCapture = board.GetPieceIndexAt(to) >= 0;
        bool isEnPassant = false;
        PieceType? promo = null;
        bool isPromotion = false;
        bool isCastle = false;

        // promotion
        if (uci.Length == 5)
        {
            isPromotion = true;
            promo = uci[4] switch
            {
                'q' => PieceType.Queen,
                'r' => PieceType.Rook,
                'b' => PieceType.Bishop,
                'n' => PieceType.Knight,
                _ => throw new ArgumentException("invalid promotion piece")
            };
        }

        // detect en-passant: if moving pawn and to == en-passant square
        if (piece == PieceType.Pawn && to == board.EnPassantSquare)
        {
            isEnPassant = true;
            isCapture = true;
        }

        // detect castle by king move  e1g1, e1c1, e8g8, e8c8
        if (piece == PieceType.King && Math.Abs(from - to) == 2) isCastle = true;

        return new Move(from, to, piece, isCapture, isEnPassant, isPromotion, promo, isCastle);
    }

    static void HandleDebug(string rest)
    {
        // support "debug board"
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        if (parts[0] == "board")
        {
            // print ASCII representation
            Console.WriteLine("info string Current board (bitboards representation):");
            // print each piece bitboard
            for (int color = 0; color < 2; color++)
            {
                for (int p = 0; p < 6; p++)
                {
                    var bb = board.GetBitboard((Color)color, (PieceType)p);
                    Console.WriteLine($"info string {(color == 0 ? "White" : "Black")} {((PieceType)p).ToString()}:\n" + Board.BitboardToString(bb));
                }
            }
            // Print combined board in ASCII with piece letters
            Console.WriteLine("info string Board ASCII:");
            Console.WriteLine("info string ----------------");
            // build ascii rows
            for (int r = 7; r >= 0; r--)
            {
                var row = new char[8];
                for (int f = 0; f < 8; f++)
                {
                    int sq = r * 8 + f;
                    int pi = board.GetPieceIndexAt(sq);
                    if (pi == -1) row[f] = '.';
                    else
                    {
                        Color c = (Color)(pi / 6);
                        PieceType pt = (PieceType)(pi % 6);
                        char ch = pt switch
                        {
                            PieceType.Pawn => 'p',
                            PieceType.Knight => 'n',
                            PieceType.Bishop => 'b',
                            PieceType.Rook => 'r',
                            PieceType.Queen => 'q',
                            PieceType.King => 'k',
                            _ => '?'
                        };
                        if (c == Color.White) ch = char.ToUpper(ch);
                        row[f] = ch;
                    }
                }
                Console.WriteLine("info string " + new string(row));
            }
            Console.WriteLine("info string ----------------");
            // print FEN like string: we can reconstruct basic FEN (placement + side + castling + ep + half/full)
            string fen = BuildFenFromBoard();
            Console.WriteLine("info string FEN: " + fen);
            Console.WriteLine("info string Castling rights: " + board.CastlingRights);
        }
    }

    static string BuildFenFromBoard()
    {
        // build placement
        var ranks = new List<string>();
        for (int r = 7; r >= 0; r--)
        {
            int empty = 0;
            var row = new System.Text.StringBuilder();
            for (int f = 0; f < 8; f++)
            {
                int sq = r * 8 + f;
                int pi = board.GetPieceIndexAt(sq);
                if (pi == -1) { empty++; }
                else
                {
                    if (empty > 0) { row.Append(empty); empty = 0; }
                    Color c = (Color)(pi / 6);
                    PieceType pt = (PieceType)(pi % 6);
                    char ch = pt switch
                    {
                        PieceType.Pawn => 'p',
                        PieceType.Knight => 'n',
                        PieceType.Bishop => 'b',
                        PieceType.Rook => 'r',
                        PieceType.Queen => 'q',
                        PieceType.King => 'k',
                        _ => '?'
                    };
                    if (c == Color.White) ch = char.ToUpper(ch);
                    row.Append(ch);
                }
            }
            if (empty > 0) row.Append(empty);
            ranks.Add(row.ToString());
        }
        string placement = string.Join('/', ranks);
        string stm = board.SideToMove == Color.White ? "w" : "b";
        string cr = "-";
        if (board.CastlingRights != 0)
        {
            var sb = new System.Text.StringBuilder();
            if ((board.CastlingRights & 1) != 0) sb.Append('K');
            if ((board.CastlingRights & 2) != 0) sb.Append('Q');
            if ((board.CastlingRights & 4) != 0) sb.Append('k');
            if ((board.CastlingRights & 8) != 0) sb.Append('q');
            cr = sb.ToString();
        }
        string ep = board.EnPassantSquare == -1 ? "-" : Board.SquareToAlgebraic(board.EnPassantSquare);
        return $"{placement} {stm} {cr} {ep} {board.HalfmoveClock} {board.FullmoveNumber}";
    }

    static void HandleGo(string rest)
    {
        // In a full engine we'd parse search limits and start a search thread.
        // For now return a dummy bestmove instantly.
        Console.WriteLine("info string go command received: " + rest);
        // Dummy delay to simulate thinking briefly
        Thread.Sleep(50);
        // Respond with a legal-looking move (e2e4) and a ponder move (e7e5)
        Console.WriteLine("bestmove e2e4 ponder e7e5");
    }

    static void HandlePerft(string rest)
    {
        // Usage: perft <depth> [fen <fen>]
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine("info string perft requires a depth");
            return;
        }
        if (!int.TryParse(parts[0], out int depth) || depth < 1)
        {
            Console.WriteLine("info string invalid perft depth");
            return;
        }

        string saveFen = BuildFenFromBoard();
        // optional: fen <fen...>
        if (parts.Length >= 2 && parts[1] == "fen")
        {
            var fenParts = new List<string>();
            for (int i = 2; i < parts.Length; i++) fenParts.Add(parts[i]);
            var fen = string.Join(' ', fenParts);
            try
            {
                // save current position as FEN, then set new FEN
                board.SetPositionFromFen(fen);
            }
            catch (Exception ex)
            {
                Console.WriteLine("info string invalid FEN for perft: " + ex.Message);
                return;
            }
        }

        // run perft
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long nodes = Perft(board, depth);
        sw.Stop();
        double seconds = Math.Max(0.001, sw.Elapsed.TotalSeconds);
        long nps = (long)(nodes / seconds);
        Console.WriteLine($"info string perft depth={depth} nodes={nodes} time_ms={sw.ElapsedMilliseconds} nps={nps}");

        // restore saved board if we changed it
        // restore original position
        try { board.SetPositionFromFen(saveFen); } catch { }
    }

    static long Perft(Board b, int depth)
    {
        if (depth == 0) return 1;
        var moves = MoveGenerator.GenerateLegalMoves(b);
        if (depth == 1) return moves.Count;
        long nodes = 0;
        foreach (var mv in moves)
        {
            b.MakeMove(mv);
            nodes += Perft(b, depth - 1);
            b.UnmakeMove();
        }
        return nodes;
    }

    static void HandleStop()
    {
        // Would stop search. For dummy we just log.
        Console.WriteLine("info string stop received (no active search)");
    }

    static void HandleQuit()
    {
        Console.WriteLine("info string quitting");
        isRunning = false;
    }
}
