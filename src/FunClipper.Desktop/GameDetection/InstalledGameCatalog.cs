using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace FunClipper.Desktop.GameDetection;

public static class InstalledGameCatalog
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumManifests = 3000;

    public static IReadOnlyList<GameInstall> ReadInstalledGames()
    {
        var games = new List<GameInstall>();
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRoot(steamRoots, ReadRegistry(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"));
        AddRoot(steamRoots, ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Valve\Steam", "InstallPath"));
        AddRoot(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (string root in steamRoots.ToArray())
        {
            string? libraries = ReadSmallFile(Path.Combine(root, "steamapps", "libraryfolders.vdf"))
                ?? ReadSmallFile(Path.Combine(root, "config", "libraryfolders.vdf"));
            if (libraries != null)
                foreach (string library in ParseSteamLibraryPaths(libraries).Take(64)) AddRoot(steamRoots, library);
        }
        foreach (string root in steamRoots.Take(64))
        {
            string steamApps = Path.Combine(root, "steamapps");
            foreach (string file in EnumerateSafe(steamApps, "appmanifest_*.acf").Take(MaximumManifests))
            {
                string? text = ReadSmallFile(file);
                GameInstall? game = text == null ? null : ParseSteamAppManifest(text, steamApps);
                if (game != null && Directory.Exists(game.RootDirectory)) games.Add(game);
            }
        }
        string epicManifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        foreach (string file in EnumerateSafe(epicManifests, "*.item").Take(MaximumManifests))
        {
            string? text = ReadSmallFile(file);
            GameInstall? game = text == null ? null : ParseEpicManifest(text);
            if (game != null && Directory.Exists(game.RootDirectory)) games.Add(game);
        }
        return games.DistinctBy(game => game.RootDirectory, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> ParseSteamLibraryPaths(string text)
    {
        VdfNode root = VdfNode.Parse(text);
        VdfNode libraries = root.GetChild("libraryfolders") ?? root;
        var paths = new List<string>();
        foreach (var child in libraries.Children)
            if (int.TryParse(child.Key, out _) && child.Value.GetValue("path") is string path && GameClassifier.TryNormalizePath(path, out string normalized)) paths.Add(normalized);
        // Older Steam installations used numeric key/value entries.
        foreach (var value in libraries.Values)
            if (int.TryParse(value.Key, out _) && GameClassifier.TryNormalizePath(value.Value, out string normalized)) paths.Add(normalized);
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static GameInstall? ParseSteamAppManifest(string text, string steamAppsDirectory)
    {
        VdfNode? state = VdfNode.Parse(text).GetChild("AppState");
        string? name = state?.GetValue("name"), directory = state?.GetValue("installdir");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(directory) || IsNonGameTitle(name)) return null;
        string common = Path.Combine(steamAppsDirectory, "common");
        if (!GameClassifier.TryNormalizePath(Path.Combine(common, directory), out string normalized) || !GameClassifier.IsInside(normalized, common)) return null;
        return new(CleanName(name), normalized, GameDetectionSource.Steam);
    }

    public static GameInstall? ParseEpicManifest(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 24 });
            JsonElement root = document.RootElement;
            string? name = JsonString(root, "DisplayName"), path = JsonString(root, "InstallLocation"), launch = JsonString(root, "LaunchExecutable");
            if (string.IsNullOrWhiteSpace(name) || IsNonGameTitle(name) || !GameClassifier.TryNormalizePath(path ?? "", out string normalized)) return null;
            // Library entries without an executable are add-ons, assets, or incomplete installs.
            if (string.IsNullOrWhiteSpace(launch) || !launch.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            return new(CleanName(name), normalized, GameDetectionSource.Epic);
        }
        catch (JsonException) { return null; }
    }

    private static string? JsonString(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsNonGameTitle(string name)
    {
        string[] software = { "Steamworks Common Redistributables", "Steam Linux Runtime", "Proton", "SteamVR", "Wallpaper Engine", "Blender", "OBS Studio", "Aseprite", "Krita", "Source Filmmaker", "Source SDK", "RPG Maker", "Unreal Engine", "Godot Engine", "Dedicated Server", "Soundtrack" };
        return software.Any(value => name.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    internal static string CleanName(string name) => new(name.Where(character => !char.IsControl(character)).Take(100).ToArray());

    private static void AddRoot(HashSet<string> roots, string? path)
    {
        if (path != null && GameClassifier.TryNormalizePath(path, out string normalized) && Directory.Exists(normalized)) roots.Add(normalized);
    }

    private static string? ReadRegistry(RegistryHive hive, RegistryView view, string path, string name)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? key = baseKey.OpenSubKey(path, false);
            return key?.GetValue(name) as string;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return null; }
    }

    private static IEnumerable<string> EnumerateSafe(string path, string pattern)
    {
        try { return Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return Array.Empty<string>(); }
    }

    private static string? ReadSmallFile(string path)
    {
        try { return new FileInfo(path).Length <= MaximumManifestBytes ? File.ReadAllText(path) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    // Valve KeyValues parser: quoted/unquoted keys, escaped quotes/backslashes, nested objects, and // comments.
    private sealed class VdfNode
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public VdfNode? GetChild(string key) => Children.GetValueOrDefault(key);
        public string? GetValue(string key) => Values.GetValueOrDefault(key);

        public static VdfNode Parse(string text)
        {
            var tokens = Tokenize(text).Take(100000).ToArray();
            int position = 0;
            return ReadNode(tokens, ref position, 0);
        }

        private static VdfNode ReadNode(string[] tokens, ref int position, int depth)
        {
            var result = new VdfNode();
            if (depth > 24) return result;
            while (position < tokens.Length)
            {
                string key = tokens[position++];
                if (key == "}") break;
                if (key == "{" || position >= tokens.Length) break;
                string value = tokens[position++];
                if (value == "{") result.Children[key] = ReadNode(tokens, ref position, depth + 1);
                else if (value != "}") result.Values[key] = value;
                else break;
            }
            return result;
        }

        private static IEnumerable<string> Tokenize(string text)
        {
            int position = 0;
            while (position < text.Length)
            {
                char character = text[position++];
                if (char.IsWhiteSpace(character)) continue;
                if (character == '/' && position < text.Length && text[position] == '/')
                {
                    while (position < text.Length && text[position] != '\n') position++;
                    continue;
                }
                if (character is '{' or '}') { yield return character.ToString(); continue; }
                var token = new StringBuilder();
                if (character == '"')
                {
                    while (position < text.Length)
                    {
                        character = text[position++];
                        if (character == '"') break;
                        if (character == '\\' && position < text.Length && text[position] is '\\' or '"') character = text[position++];
                        token.Append(character);
                    }
                }
                else
                {
                    token.Append(character);
                    while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not '{' and not '}') token.Append(text[position++]);
                }
                yield return token.ToString();
            }
        }
    }
}
