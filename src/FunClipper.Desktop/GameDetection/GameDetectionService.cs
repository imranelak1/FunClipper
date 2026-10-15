using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FunClipper.Desktop.GameDetection;

/// <summary>Local-only game discovery. Events arrive on a worker thread; callers dispatch to their UI.</summary>
public sealed class GameDetectionService : IAsyncDisposable
{
    private readonly string _settingsPath;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly WindowsGameProcessReader _processes = new();
    private readonly Dictionary<string, GameMatch?> _classifications = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<GameInstall>? _classifiedCatalog;
    private RegisteredGame[]? _classifiedRegistered;
    private RegisteredGame[] _registered;
    private IReadOnlyList<GameInstall> _catalog = Array.Empty<GameInstall>();
    private GameDetectionSnapshot _current = GameDetectionSnapshot.Empty;
    private Task? _loop;
    private DateTimeOffset _nextCatalogRefresh;
    private int _disposed;

    public event Action<GameDetectionSnapshot>? Changed;
    public GameDetectionSnapshot Current => Volatile.Read(ref _current);
    public static TimeSpan ScanInterval { get; } = TimeSpan.FromSeconds(2.5);
    internal int CachedClassificationCount => _classifications.Count;
    internal int CachedProcessCount => _processes.CachedProcessCount;

    public GameDetectionService(string dataDirectory)
    {
        _settingsPath = Path.Combine(Path.GetFullPath(dataDirectory), "games.json");
        _registered = LoadSettings(_settingsPath);
        _current = GameDetectionSnapshot.Empty with { RegisteredGames = _registered };
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_gate) _loop ??= Task.Run(() => RunAsync(_shutdown.Token));
    }

    public async Task<RegisteredGame> RegisterGameAsync(string executablePath, string? displayName = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!GameClassifier.TryNormalizePath(executablePath, out string path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("Choose an existing game .exe file.", nameof(executablePath));
        string name = InstalledGameCatalog.CleanName(string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(path) : displayName.Trim());
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a game name.", nameof(displayName));
        var game = new RegisteredGame(name, path);
        await _settingsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            RegisteredGame[] games;
            lock (_gate) games = _registered.Where(item => !item.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase)).Append(game).TakeLast(256).ToArray();
            await SaveSettingsAsync(games).ConfigureAwait(false);
            lock (_gate) _registered = games;
        }
        finally { _settingsGate.Release(); }
        return game;
    }

    public async Task RemoveRegisteredGameAsync(string executablePath)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!GameClassifier.TryNormalizePath(executablePath, out string path)) return;
        await _settingsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            RegisteredGame[] games;
            lock (_gate) games = _registered.Where(item => !item.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
            await SaveSettingsAsync(games).ConfigureAwait(false);
            lock (_gate) _registered = games;
        }
        finally { _settingsGate.Release(); }
    }

    public async Task RefreshCatalogAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        IReadOnlyList<GameInstall> catalog = await Task.Run(InstalledGameCatalog.ReadInstalledGames).ConfigureAwait(false);
        lock (_gate)
        {
            _catalog = catalog;
            _nextCatalogRefresh = DateTimeOffset.UtcNow.AddMinutes(5);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset refresh;
                lock (_gate) refresh = _nextCatalogRefresh;
                if (DateTimeOffset.UtcNow >= refresh) await RefreshCatalogAsync().ConfigureAwait(false);
                Scan();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
            {
                Publish(Current with { LastScanUtc = DateTimeOffset.UtcNow, Status = "Game detection will retry automatically." });
            }
            try { await Task.Delay(ScanInterval, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    private void Scan()
    {
        IReadOnlyList<GameInstall> catalog;
        RegisteredGame[] registered;
        lock (_gate) { catalog = _catalog; registered = _registered; }
        if (!ReferenceEquals(catalog, _classifiedCatalog) || !ReferenceEquals(registered, _classifiedRegistered))
        {
            _classifications.Clear();
            _classifiedCatalog = catalog;
            _classifiedRegistered = registered;
        }
        int foreground = WindowsGameProcessReader.ForegroundProcessId();
        IReadOnlyList<WindowsGameProcessReader.ProcessImage> processes = _processes.Read();
        var alivePaths = new HashSet<string>(processes.Select(process => process.ExecutablePath), StringComparer.OrdinalIgnoreCase);
        foreach (string path in _classifications.Keys.Where(path => !alivePaths.Contains(path)).ToArray()) _classifications.Remove(path);
        DetectedGame[] running = processes.Select(process =>
        {
            if (!_classifications.TryGetValue(process.ExecutablePath, out GameMatch? match))
            {
                match = GameClassifier.Classify(process.ExecutablePath, catalog, registered);
                if (_classifications.Count < 8192) _classifications[process.ExecutablePath] = match;
            }
            return match == null ? null : new DetectedGame(match.Name, process.ExecutablePath, process.ProcessId, process.ProcessId == foreground, match.Source);
        }).OfType<DetectedGame>().OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase).ThenBy(game => game.ProcessId).ToArray();
        DetectedGame? active = GameSelection.SelectActive(running, Current.ActiveGame?.ProcessId);
        Publish(new(active, running, registered, DateTimeOffset.UtcNow, active == null ? "No game detected · desktop capture is available" : $"{active.Name} detected"));
    }

    private void Publish(GameDetectionSnapshot snapshot)
    {
        GameDetectionSnapshot previous = Current;
        Volatile.Write(ref _current, snapshot);
        if (previous.ActiveGame == snapshot.ActiveGame && previous.Status == snapshot.Status
            && previous.RunningGames.SequenceEqual(snapshot.RunningGames) && previous.RegisteredGames.SequenceEqual(snapshot.RegisteredGames)) return;
        Action<GameDetectionSnapshot>? listeners = Changed;
        if (listeners == null) return;
        foreach (Action<GameDetectionSnapshot> callback in listeners.GetInvocationList())
        {
            try { callback(snapshot); }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Game detection listener failed: {error.GetType().Name}"); }
        }
    }

    private async Task SaveSettingsAsync(RegisteredGame[] games)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        string temporary = _settingsPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            File.Move(temporary, _settingsPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static RegisteredGame[] LoadSettings(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return Array.Empty<RegisteredGame>();
            return (JsonSerializer.Deserialize<RegisteredGame[]>(File.ReadAllText(path)) ?? Array.Empty<RegisteredGame>())
                .Where(game => game != null && !string.IsNullOrWhiteSpace(game.Name) && GameClassifier.TryNormalizePath(game.ExecutablePath, out _))
                .Select(game => new RegisteredGame(InstalledGameCatalog.CleanName(game.Name), Path.GetFullPath(game.ExecutablePath)))
                .DistinctBy(game => game.ExecutablePath, StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException) { return Array.Empty<RegisteredGame>(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        Task? loop;
        lock (_gate) loop = _loop;
        if (loop != null) await loop.ConfigureAwait(false);
        _shutdown.Dispose();
        // Settings writes may still be completing in a closing manager window.
        await _settingsGate.WaitAsync().ConfigureAwait(false);
        _settingsGate.Release();
    }
}
