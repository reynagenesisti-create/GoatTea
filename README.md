GOATea - UCI chess engine skeleton

This project is a minimal .NET console application that implements the UCI protocol skeleton with dummy responses. It's intended as a starting point for building a full chess engine.

Quick start

1. Build

```powershell
dotnet build
```

2. Run interactively

```powershell
dotnet run --project "D:\Chess Engines\GOATea\GOATea.csproj"
```

3. Use with a UCI GUI

Point your GUI's engine path to the built executable (bin/Debug/net8.0/GOATea.exe). The engine supports basic UCI commands including `uci`, `isready`, `setoption`, `ucinewgame`, `position`, `go`, `stop`, and `quit`.

Notes

- Currently the engine returns dummy responses; "go" returns a hard-coded move `e2e4` with ponder `e7e5`.
- Next steps: add an internal board representation, move generator, search (alpha-beta / PVS), evaluation, hash tables, multi-threading, time management, and tuning options.
