using System;
using System.Collections.Generic;
using System.Linq;

namespace FunClipper.Desktop.GameDetection;

public enum GameDetectionSource { Steam, Epic, KnownExecutable, Custom }

public sealed record DetectedGame(string Name, string ExecutablePath, int ProcessId, bool IsForeground, GameDetectionSource Source);
public sealed record RegisteredGame(string Name, string ExecutablePath);
public sealed record GameInstall(string Name, string RootDirectory, GameDetectionSource Source);
public sealed record GameMatch(string Name, GameDetectionSource Source);

public sealed record GameDetectionSnapshot(
    DetectedGame? ActiveGame,
    IReadOnlyList<DetectedGame> RunningGames,
    IReadOnlyList<RegisteredGame> RegisteredGames,
    DateTimeOffset LastScanUtc,
    string Status)
{
    public static GameDetectionSnapshot Empty { get; } = new(null, Array.Empty<DetectedGame>(), Array.Empty<RegisteredGame>(), DateTimeOffset.MinValue, "Looking for a running game…");
}

public static class GameSelection
{
    // Keep the game selected when the user opens the clipper, its HUD, or another app.
    public static DetectedGame? SelectActive(IReadOnlyList<DetectedGame> games, int? previousProcessId) =>
        games.FirstOrDefault(game => game.IsForeground)
        ?? games.FirstOrDefault(game => game.ProcessId == previousProcessId)
        ?? games.OrderByDescending(game => game.Source == GameDetectionSource.Custom)
                .ThenByDescending(game => game.Source == GameDetectionSource.KnownExecutable)
                .ThenBy(game => game.ProcessId).FirstOrDefault();
}
