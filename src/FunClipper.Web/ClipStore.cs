using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FunClipper.Web;

public sealed record ClipMetadata(string Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, long SizeBytes);
public sealed record UploadDraft(string Id, string TemporaryPath, long SizeBytes);
public sealed class UploadTooLargeException : IOException;

public sealed class ClipStore
{
    public const long MaxUploadBytes = 100L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string directory;
    private readonly Uri? publicUrl;
    private readonly ILogger<ClipStore> logger;
    public string AccessKey { get; }
    public int ExpiryDays { get; }
    public bool HasPublicUrl => publicUrl is not null;

    public ClipStore(ILogger<ClipStore> logger)
    {
        this.logger = logger;
        directory = Environment.GetEnvironmentVariable("FUNCLIPPER_SHARE_DIR") is { Length: > 0 } configuredDirectory
            ? Path.GetFullPath(configuredDirectory)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunClipper", "shared");
        Directory.CreateDirectory(directory);
        AccessKey = LoadAccessKey();
        ExpiryDays = int.TryParse(Environment.GetEnvironmentVariable("FUNCLIPPER_EXPIRY_DAYS"), out var days) && days is >= 1 and <= 365 ? days : 14;
        var configuredPublicUrl = Environment.GetEnvironmentVariable("FUNCLIPPER_PUBLIC_URL");
        if (!string.IsNullOrWhiteSpace(configuredPublicUrl))
        {
            if (!Uri.TryCreate(configuredPublicUrl, UriKind.Absolute, out publicUrl)
                || publicUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(publicUrl.Query)
                || !string.IsNullOrEmpty(publicUrl.Fragment) || !string.IsNullOrEmpty(publicUrl.UserInfo)
                || publicUrl.AbsolutePath != "/")
                throw new InvalidOperationException("FUNCLIPPER_PUBLIC_URL must be an HTTP(S) origin, such as https://clips.example.com, with no path, query, or fragment.");
        }
        logger.LogInformation("Clip storage ready. Upload/library access requires the local access key. Clips expire after {Days} days.", ExpiryDays);
    }

    private string LoadAccessKey()
    {
        var configuredKey = Environment.GetEnvironmentVariable("FUNCLIPPER_SHARE_KEY");
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            if (configuredKey.Length is < 16 or > 512) throw new InvalidOperationException("FUNCLIPPER_SHARE_KEY must contain between 16 and 512 characters.");
            return configuredKey;
        }
        var keyPath = Path.Combine(directory, "access-key.txt");
        if (!File.Exists(keyPath))
        {
            try
            {
                using var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var keyBytes = Encoding.UTF8.GetBytes(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
                stream.Write(keyBytes);
            }
            catch (IOException) when (File.Exists(keyPath)) { }
        }
        var key = File.ReadAllText(keyPath).Trim();
        if (key.Length is < 16 or > 512) throw new InvalidOperationException("The local access-key.txt is invalid. Set FUNCLIPPER_SHARE_KEY or replace the access key file.");
        return key;
    }

    public string LinkBase(HttpRequest request) => publicUrl?.GetLeftPart(UriPartial.Authority)
        ?? $"http://127.0.0.1:{request.Host.Port ?? 5180}";

    public static bool IsValidId(string? id) => id is { Length: 32 } && id.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    public string MediaPath(string id) => IsValidId(id) ? Path.Combine(directory, id + ".mp4") : throw new ArgumentException("Invalid clip ID.", nameof(id));
    private string MetadataPath(string id) => Path.Combine(directory, id + ".json");

    public async Task<UploadDraft> StageAsync(Stream source, CancellationToken cancellationToken)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var path = Path.Combine(directory, id + ".upload");
        try
        {
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var prefix = new byte[32];
            var prefixLength = 0;
            while (prefixLength < prefix.Length)
            {
                var count = await source.ReadAsync(prefix.AsMemory(prefixLength), cancellationToken);
                if (count == 0) break;
                prefixLength += count;
            }
            if (!HasMp4Signature(prefix.AsSpan(0, prefixLength)))
                throw new InvalidDataException("This file is not an MP4 video. Export your clip as MP4 first.");
            await destination.WriteAsync(prefix.AsMemory(0, prefixLength), cancellationToken);
            var remainingLength = await CopyBoundedAsync(source, destination, MaxUploadBytes - prefixLength, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            return new UploadDraft(id, path, prefixLength + remainingLength);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static bool HasMp4Signature(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 16 || !prefix.Slice(4, 4).SequenceEqual("ftyp"u8)) return false;
        var boxSize = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (boxSize < 16 || boxSize > 4096) return false;
        for (var offset = 8; offset + 4 <= Math.Min(boxSize, prefix.Length); offset += 4)
        {
            if (offset == 12) continue; // Minor version, not a brand.
            var brand = prefix.Slice(offset, 4);
            if (brand.SequenceEqual("isom"u8) || brand.SequenceEqual("mp41"u8) || brand.SequenceEqual("mp42"u8)
                || brand.SequenceEqual("avc1"u8) || brand.SequenceEqual("M4V "u8)
                || (brand[..3].SequenceEqual("iso"u8) && brand[3] is >= (byte)'2' and <= (byte)'9')) return true;
        }
        return false;
    }

    public static async Task<long> CopyBoundedAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes) throw new UploadTooLargeException();
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return total;
    }

    public async Task<ClipMetadata> CommitAsync(UploadDraft draft, string title, CancellationToken cancellationToken)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var cleanTitle = string.Join(" ", title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        cleanTitle = new string(cleanTitle.Where(character => !char.IsControl(character)).ToArray());
        if (cleanTitle.Length > 140) cleanTitle = cleanTitle[..140];
        if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = "Gameplay clip";
        var clip = new ClipMetadata(draft.Id, cleanTitle, createdAt, createdAt.AddDays(ExpiryDays), draft.SizeBytes);
        var metadataTemporaryPath = Path.Combine(directory, draft.Id + ".metadata-upload");
        try
        {
            await using (var stream = new FileStream(metadataTemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await JsonSerializer.SerializeAsync(stream, clip, JsonOptions, cancellationToken);
            File.Move(draft.TemporaryPath, MediaPath(draft.Id));
            File.Move(metadataTemporaryPath, MetadataPath(draft.Id));
            return clip;
        }
        catch
        {
            TryDelete(metadataTemporaryPath);
            TryDelete(MediaPath(draft.Id));
            throw;
        }
    }

    public void Discard(UploadDraft draft) => TryDelete(draft.TemporaryPath);

    public async Task<ClipMetadata?> FindAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsValidId(id)) return null;
        try
        {
            await using var stream = new FileStream(MetadataPath(id), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            var clip = await JsonSerializer.DeserializeAsync<ClipMetadata>(stream, JsonOptions, cancellationToken);
            return clip is not null && clip.Id == id && clip.ExpiresAt > DateTimeOffset.UtcNow && File.Exists(MediaPath(id)) ? clip : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (JsonException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async Task<List<ClipMetadata>> ListAsync(CancellationToken cancellationToken)
    {
        var clips = new List<ClipMetadata>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var clip = await FindAsync(Path.GetFileNameWithoutExtension(path), cancellationToken);
            if (clip is not null) clips.Add(clip);
        }
        return clips.OrderByDescending(clip => clip.CreatedAt).ToList();
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsValidId(id)) return false;
        var exists = await FindAsync(id, cancellationToken) is not null;
        TryDelete(MetadataPath(id));
        TryDelete(MediaPath(id));
        return exists;
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (IsValidId(id) && await FindAsync(id, cancellationToken) is null)
            {
                TryDelete(path);
                TryDelete(MediaPath(id));
            }
        }
        foreach (var pattern in new[] { "*.upload", "*.metadata-upload" })
            foreach (var path in Directory.EnumerateFiles(directory, pattern))
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-1)) TryDelete(path);
    }

    private void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { logger.LogWarning("A stored clip could not be removed. Cleanup will retry later."); }
        catch (UnauthorizedAccessException) { logger.LogWarning("A stored clip could not be removed. Check storage directory permissions."); }
    }
}

public sealed class ExpiredClipCleanup(ClipStore store, ILogger<ExpiredClipCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await store.CleanupAsync(stoppingToken); }
            catch (IOException) { logger.LogWarning("Clip cleanup could not read storage. It will retry in one hour."); }
            catch (UnauthorizedAccessException) { logger.LogWarning("Clip cleanup needs access to its storage directory."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
