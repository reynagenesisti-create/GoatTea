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

    static void Main(string[] args)
    {
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

    static void HandleIsReady()
    {
        // If initialization is asynchronous, wait until ready. For now immediate.
        Console.WriteLine("readyok");
    }

    static void HandleSetOption(string rest)
    {
        // Minimal parsing: support "name X value Y" but keep it dummy.
        Console.WriteLine("info string setoption received: " + rest);
    }

    static void HandleUciNewGame()
    {
        // Reset internal state for a new game. Currently dummy.
        Console.WriteLine("info string new game initialized (dummy)");
    }

    static void HandlePosition(string rest)
    {
        // Accepts: position startpos | fen <fen> [moves ...]
        Console.WriteLine("info string position received: " + rest);
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
