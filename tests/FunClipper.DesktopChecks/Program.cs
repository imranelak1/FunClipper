using System.Net;
using System.Net.Http.Headers;
using FunClipper.Desktop;

string directory = Path.Combine(Path.GetTempPath(), "FunClipper-DesktopChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    using var vitals = new VitalsSampler();
    vitals.Sample();
    await Task.Delay(1000);
    var snapshot = vitals.Sample();
    Require(snapshot.TotalRamGb > 0 && snapshot.UsedRamGb is >= 0 && snapshot.UsedRamGb <= snapshot.TotalRamGb, "Physical RAM metrics are valid");
    Require(snapshot.CpuPercent is >= 0 and <= 100 && snapshot.AppCpuPercent is >= 0 and <= 100 && snapshot.AppRamMb > 0, "Actual CPU and process metrics are valid");
    Require(snapshot.GpuPercent is null or >= 0 and <= 100, "GPU measurement is bounded or unavailable");
    using var share = new ShareClient(directory);
    bool rejected = false;
    try { share.Configure("http://example.com", "a-test-access-key-with-32-characters"); }
    catch (ArgumentException) { rejected = true; }
    Require(rejected, "Hosted connection rejects unencrypted HTTP");
    if (args.Length >= 2)
    {
        string key = Environment.GetEnvironmentVariable("FUNCLIPPER_SHARE_KEY") ?? throw new InvalidOperationException("Set the isolated test server key.");
        share.Configure(args[0], key);
        string url = await share.UploadAsync(args[1]);
        Require(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback && uri.AbsolutePath.StartsWith("/c/"), "Native upload returns a local capability link");
        using var client = new HttpClient();
        using var viewer = await client.GetAsync(url);
        Require(viewer.IsSuccessStatusCode, "Native-uploaded clip viewer is reachable");
        string id = new Uri(url).Segments.Last();
        using var mediaRequest = new HttpRequestMessage(HttpMethod.Get, args[0].TrimEnd('/') + "/media/" + id);
        mediaRequest.Headers.Range = new RangeHeaderValue(0, 31);
        using var media = await client.SendAsync(mediaRequest);
        byte[] prefix = await media.Content.ReadAsByteArrayAsync();
        Require(media.StatusCode == HttpStatusCode.PartialContent && prefix.Length == 32 && System.Text.Encoding.ASCII.GetString(prefix, 4, 4) == "ftyp", "Native-uploaded MP4 supports real range playback");
        using var unauthorized = await client.GetAsync(args[0].TrimEnd('/') + "/api/clips");
        Require(unauthorized.StatusCode == HttpStatusCode.Unauthorized, "Upload does not expose the private library");
    }
    Console.WriteLine("Desktop integration checks passed.");
}
finally
{
    string resolved = Path.GetFullPath(directory);
    string temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test cleanup escaped the temporary directory.");
    Directory.Delete(resolved, true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}
