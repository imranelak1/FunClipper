using System.Diagnostics;
using FunClipper.Desktop.GameDetection;

if (args.Contains("--fixture-child"))
{
    Console.WriteLine("FunClipper owned game-detection fixture");
    await Task.Delay(TimeSpan.FromSeconds(45));
    return;
}

int passed = 0;
Check("modern and legacy Steam libraries parse escaped paths", () =>
{
    const string vdf = "\"libraryfolders\" { // comment\n\"0\" { \"path\" \"C:\\\\Steam\" \"apps\" { \"570\" \"100\" } } \"1\" \"D:\\\\Games\" }";
    var libraries = InstalledGameCatalog.ParseSteamLibraryPaths(vdf);
    Require(libraries.Count == 2 && libraries.Contains(@"C:\Steam") && libraries.Contains(@"D:\Games"));
});
Check("Steam manifest preserves game title and rejects path escape", () =>
{
    var game = InstalledGameCatalog.ParseSteamAppManifest("\"AppState\" { \"appid\" \"42\" \"name\" \"A Game\" \"installdir\" \"A Game\" }", @"C:\Steam\steamapps");
    Require(game?.Name == "A Game" && game.RootDirectory == @"C:\Steam\steamapps\common\A Game");
    Require(InstalledGameCatalog.ParseSteamAppManifest("AppState { name Unsafe installdir ..\\..\\Windows }", @"C:\Steam\steamapps") == null);
});
Check("Steam software and runtime entries stay out of game catalog", () =>
{
    foreach (string name in new[] { "Wallpaper Engine", "SteamVR", "OBS Studio", "Steamworks Common Redistributables", "Blender", "Aseprite", "Krita", "Source Filmmaker", "Game Dedicated Server" })
        Require(InstalledGameCatalog.ParseSteamAppManifest($"AppState {{ name \"{name}\" installdir Tool }}", @"C:\Steam\steamapps") == null);
});
Check("Epic manifest finds games and rejects non-executable assets", () =>
{
    var game = InstalledGameCatalog.ParseEpicManifest("{\"DisplayName\":\"A Game\",\"InstallLocation\":\"D:\\\\Epic\\\\Game\",\"LaunchExecutable\":\"Game/Binaries/Game.exe\"}");
    Require(game?.Name == "A Game" && game.Source == GameDetectionSource.Epic);
    Require(InstalledGameCatalog.ParseEpicManifest("{\"DisplayName\":\"Asset\",\"InstallLocation\":\"D:\\\\Epic\\\\Asset\"}") == null);
    Require(InstalledGameCatalog.ParseEpicManifest("broken") == null);
});

var installs = new[] { new GameInstall("Installed Game", @"C:\Games\Installed", GameDetectionSource.Steam), new GameInstall("Nested Game", @"C:\Games\Installed\Nested", GameDetectionSource.Epic) };
Check("install matching respects folder boundaries and longest root", () =>
{
    Require(GameClassifier.Classify(@"C:\Games\Installed\Binaries\A.exe", installs, [])?.Name == "Installed Game");
    Require(GameClassifier.Classify(@"C:\Games\InstalledOther\A.exe", installs, []) == null);
    Require(GameClassifier.Classify(@"C:\Games\Installed\Nested\A.exe", installs, [])?.Name == "Nested Game");
});
Check("launchers helpers software and Java stay excluded inside game roots", () =>
{
    foreach (string name in new[] { "Launcher.exe", "CrashReportClient.exe", "EasyAntiCheat_EOS.exe", "unins000.exe", "setup.exe", "steam.exe", "wallpaper64.exe", "obs64.exe", "aseprite.exe", "krita.exe", "sfm.exe", "vrmonitor.exe", "javaw.exe", "LeagueClient.exe", "Discord.exe", "EOSOverlayRenderer-Win64-Shipping.exe" })
        Require(GameClassifier.Classify(Path.Combine(installs[0].RootDirectory, name), installs, []) == null);
});
Check("exact known executable works outside installed libraries", () =>
{
    var game = GameClassifier.Classify(@"D:\Standalone\cs2.exe", [], []);
    Require(game?.Name == "Counter-Strike 2" && game.Source == GameDetectionSource.KnownExecutable);
    Require(GameClassifier.Classify(@"D:\Standalone\not-cs2.exe", [], []) == null);
});
Check("explicit user registration overrides generic executable exclusions", () =>
{
    var registered = new[] { new RegisteredGame("My Java Game", @"C:\Runtime\javaw.exe") };
    Require(GameClassifier.Classify(@"C:\Runtime\javaw.exe", [], registered)?.Source == GameDetectionSource.Custom);
    Require(GameClassifier.Classify(@"D:\Runtime\javaw.exe", [], registered) == null);
});
Check("foreground game wins then persists across HUD browser and unknown app focus", () =>
{
    var first = new DetectedGame("First", @"C:\GameA.exe", 10, false, GameDetectionSource.Steam);
    var second = new DetectedGame("Second", @"C:\GameB.exe", 20, true, GameDetectionSource.Epic);
    Require(GameSelection.SelectActive([first, second], 10)?.ProcessId == 20);
    Require(GameSelection.SelectActive([first, second with { IsForeground = false }], 20)?.ProcessId == 20);
    Require(GameSelection.SelectActive([first], 20)?.ProcessId == 10);
    Require(GameSelection.SelectActive([], 10) == null);
});

string directory = Path.Combine(Path.GetTempPath(), "FunClipper-GameChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
Process? child = null;
try
{
    string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test apphost is unavailable.");
    if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Run the generated FunClipper.GameChecks.exe apphost for the owned subprocess test.");
    await using var service = new GameDetectionService(directory);
    bool rejected = false;
    try { await service.RegisterGameAsync(Path.Combine(directory, "missing.exe")); }
    catch (ArgumentException) { rejected = true; }
    Require(rejected);
    await service.RegisterGameAsync(executable, "Owned test game");
    child = Process.Start(new ProcessStartInfo(executable, "--fixture-child") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })
        ?? throw new InvalidOperationException("Could not start the owned test fixture.");
    int childId = child.Id;
    service.Start();
    await UntilAsync(() => service.Current.RunningGames.Any(game => game.ProcessId == childId), TimeSpan.FromSeconds(15));
    Require(service.Current.ActiveGame?.ProcessId == childId && service.Current.ActiveGame.Source == GameDetectionSource.Custom);
    Require(service.CachedProcessCount <= 8192 && service.CachedClassificationCount <= 8192);
    Console.WriteLine("PASS real Windows process scan detects the owned custom executable and bounds caches"); passed++;
    long handleCount = Process.GetCurrentProcess().HandleCount;
    await Task.Delay(GameDetectionService.ScanInterval + TimeSpan.FromMilliseconds(250));
    Require(service.Current.RunningGames.Any(game => game.ProcessId == childId));
    Require(Process.GetCurrentProcess().HandleCount - handleCount < 40);
    Console.WriteLine("PASS cached rescan retains the running game and closes native handles"); passed++;
    child.Kill();
    await child.WaitForExitAsync();
    await UntilAsync(() => !service.Current.RunningGames.Any(game => game.ProcessId == childId), TimeSpan.FromSeconds(10));
    Require(service.Current.ActiveGame?.ProcessId != childId);
    Console.WriteLine("PASS real process exit clears detection without a stale selected game"); passed++;
    await using (var reloaded = new GameDetectionService(directory)) Require(reloaded.Current.RegisteredGames.Single().Name == "Owned test game");
    await service.RemoveRegisteredGameAsync(executable);
    await using (var reloaded = new GameDetectionService(directory)) Require(reloaded.Current.RegisteredGames.Count == 0);
    Console.WriteLine("PASS custom game registration and removal persist across service restarts"); passed++;
    // Corrupted user settings must not prevent the clipper from starting.
    await File.WriteAllTextAsync(Path.Combine(directory, "games.json"), "not json");
    await using (var reloaded = new GameDetectionService(directory)) Require(reloaded.Current.RegisteredGames.Count == 0);
    Console.WriteLine("PASS malformed settings recover without blocking startup"); passed++;
}
finally
{
    if (child != null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
    string resolved = Path.GetFullPath(directory);
    string temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test cleanup escaped the temporary directory.");
    Directory.Delete(resolved, true);
}
Console.WriteLine($"All {passed} game-detection checks passed.");

void Check(string name, Action check) { check(); Console.WriteLine("PASS " + name); passed++; }
static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Game detection assertion failed."); }
static async Task UntilAsync(Func<bool> predicate, TimeSpan timeout)
{
    var clock = Stopwatch.StartNew();
    while (!predicate())
    {
        if (clock.Elapsed > timeout) throw new TimeoutException("Owned game fixture detection did not update in time.");
        await Task.Delay(100);
    }
}
