using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FunClipper.Desktop.GameDetection;

public static class GameClassifier
{
    // A deliberately small, original fallback list. Installed libraries cover other games.
    private static readonly IReadOnlyDictionary<string, string> KnownExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cs2.exe"] = "Counter-Strike 2", ["csgo.exe"] = "Counter-Strike: Global Offensive",
        ["dota2.exe"] = "Dota 2", ["VALORANT-Win64-Shipping.exe"] = "VALORANT",
        ["FortniteClient-Win64-Shipping.exe"] = "Fortnite", ["RocketLeague.exe"] = "Rocket League",
        ["r5apex.exe"] = "Apex Legends", ["League of Legends.exe"] = "League of Legends",
        ["Overwatch.exe"] = "Overwatch", ["Minecraft.Windows.exe"] = "Minecraft",
        ["RobloxPlayerBeta.exe"] = "Roblox", ["GenshinImpact.exe"] = "Genshin Impact",
        ["StarRail.exe"] = "Honkai: Star Rail", ["ZenlessZoneZero.exe"] = "Zenless Zone Zero",
        ["eldenring.exe"] = "Elden Ring", ["Stardew Valley.exe"] = "Stardew Valley",
        ["Terraria.exe"] = "Terraria", ["Hades.exe"] = "Hades", ["Hades2.exe"] = "Hades II",
        ["Warframe.x64.exe"] = "Warframe", ["osu!.exe"] = "osu!",
        ["ffxiv_dx11.exe"] = "Final Fantasy XIV", ["GTA5.exe"] = "Grand Theft Auto V",
        ["GTA5_Enhanced.exe"] = "Grand Theft Auto V Enhanced", ["RainbowSix.exe"] = "Rainbow Six Siege",
        ["RainbowSix_Vulkan.exe"] = "Rainbow Six Siege", ["Cyberpunk2077.exe"] = "Cyberpunk 2077",
        ["witcher3.exe"] = "The Witcher 3", ["bg3.exe"] = "Baldur’s Gate 3", ["bg3_dx11.exe"] = "Baldur’s Gate 3",
        ["destiny2.exe"] = "Destiny 2", ["DeadByDaylight-Win64-Shipping.exe"] = "Dead by Daylight"
    };

    private static readonly HashSet<string> ExcludedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamservice.exe", "steamwebhelper.exe", "GameOverlayUI.exe", "EpicGamesLauncher.exe", "EpicWebHelper.exe",
        "RiotClientServices.exe", "RiotClientUx.exe", "RiotClientUxRender.exe", "LeagueClient.exe", "LeagueClientUx.exe", "LeagueClientUxRender.exe",
        "Battle.net.exe", "Agent.exe", "UbisoftConnect.exe", "upc.exe", "GalaxyClient.exe", "EADesktop.exe", "EALauncher.exe", "Origin.exe",
        "java.exe", "javaw.exe", "python.exe", "pythonw.exe", "node.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "FunClipper.exe", "ffmpeg.exe", "ffprobe.exe", "UnityCrashHandler64.exe", "UnityCrashHandler32.exe",
        "EOSOverlayRenderer-Win64-Shipping.exe", "EOSOverlayRenderer-Win32-Shipping.exe", "EOSBootstrapper.exe",
        "wallpaper32.exe", "wallpaper64.exe", "blender.exe", "obs64.exe", "aseprite.exe", "krita.exe", "sfm.exe", "vrmonitor.exe", "vrserver.exe", "vrcompositor.exe",
        "chrome.exe", "msedge.exe", "firefox.exe", "Discord.exe", "UnrealEditor.exe", "Unity.exe", "godot.exe"
    };

    public static GameMatch? Classify(string executablePath, IReadOnlyList<GameInstall> installs, IReadOnlyList<RegisteredGame> registered)
    {
        if (!TryNormalizePath(executablePath, out string path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        RegisteredGame? custom = registered.FirstOrDefault(game => string.Equals(game.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
        if (custom != null) return new(custom.Name, GameDetectionSource.Custom);
        if (IsHelper(path)) return null;
        string fileName = Path.GetFileName(path);
        if (KnownExecutables.TryGetValue(fileName, out string? name)) return new(name, GameDetectionSource.KnownExecutable);
        GameInstall? install = installs.Where(game => IsInside(path, game.RootDirectory))
            .OrderByDescending(game => game.RootDirectory.Length).FirstOrDefault();
        return install == null ? null : new(install.Name, install.Source);
    }

    public static bool IsHelper(string executablePath)
    {
        string fileName = Path.GetFileName(executablePath);
        if (ExcludedExecutables.Contains(fileName)) return true;
        string lower = fileName.ToLowerInvariant();
        string[] helperFragments = { "crashreport", "crashpad", "crashhandler", "crashsender", "unins", "uninstall", "setup", "installer", "launcher", "updater", "anticheat", "easyanticheat", "battleye", "cefsubprocess", "cef_helper", "redist", "dedicatedserver" };
        if (helperFragments.Any(lower.Contains)) return true;
        string[] parts = executablePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part.Equals("_CommonRedist", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Engine", StringComparison.OrdinalIgnoreCase)
            || part.Equals("__Installer", StringComparison.OrdinalIgnoreCase)
            || part.Equals("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)
            || part.Equals("BattlEye", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsInside(string path, string root) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizePath(string value, out string normalized)
    {
        normalized = "";
        try
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) return false;
            normalized = Path.GetFullPath(value);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
