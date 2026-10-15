using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FunClipper.Desktop;

public sealed class ShareClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _settingsPath;
    public string BaseUrl { get; private set; } = Environment.GetEnvironmentVariable("FUNCLIPPER_SERVER") ?? "http://127.0.0.1:5180";
    public string Key { get; private set; } = Environment.GetEnvironmentVariable("FUNCLIPPER_SHARE_KEY") ?? "";
    public bool IsLocal => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;

    public ShareClient(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _settingsPath = Path.Combine(dataDirectory, "share-settings.json");
        try
        {
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath));
                if (settings != null) { BaseUrl = settings.BaseUrl; Key = settings.Key; }
            }
        }
        catch { /* Invalid settings can be repaired in the connection dialog. */ }
        RefreshLocalKey();
    }

    public void Configure(string baseUrl, string key)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new ArgumentException("Use HTTPS for a hosted server, or HTTP on localhost.");
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Enter the server's access key.");
        BaseUrl = baseUrl.TrimEnd('/'); Key = key.Trim();
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings(BaseUrl, Key)));
    }

    public void RefreshLocalKey()
    {
        if (!IsLocal || Key.Length > 0) return;
        string folder = Environment.GetEnvironmentVariable("FUNCLIPPER_SHARE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunClipper", "shared");
        string keyFile = Path.Combine(folder, "access-key.txt");
        if (File.Exists(keyFile)) Key = File.ReadAllText(keyFile).Trim();
    }

    public string LibraryUrl() { RefreshLocalKey(); return BaseUrl.TrimEnd('/') + "/#key=" + Uri.EscapeDataString(Key); }

    public async Task<string> UploadAsync(string filePath, CancellationToken ct = default)
    {
        RefreshLocalKey();
        if (string.IsNullOrWhiteSpace(Key)) throw new InvalidOperationException("Start the web companion with scripts/Start-Web.ps1, then share again. You can configure a hosted server in Connection.");
        using var content = new MultipartFormDataContent();
        using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        content.Add(fileContent, "file", Path.GetFileName(filePath));
        content.Add(new StringContent(Path.GetFileNameWithoutExtension(filePath)), "title");
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl.TrimEnd('/') + "/api/clips") { Content = content };
        request.Headers.Add("X-FunClipper-Key", Key);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            string detail = "";
            try { using var error = JsonDocument.Parse(body); if (error.RootElement.TryGetProperty("error", out var message)) detail = message.GetString() ?? ""; } catch { }
            throw new InvalidOperationException($"Upload failed ({(int)response.StatusCode}). {detail}".Trim());
        }
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("url").GetString() ?? throw new InvalidOperationException("The server returned no sharing link.");
    }

    public void Dispose() => _http.Dispose();
    private sealed record Settings(string BaseUrl, string Key);
}
