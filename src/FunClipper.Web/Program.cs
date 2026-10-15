using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using FunClipper.Web;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5180");

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ClipStore.MaxUploadBytes + 512 * 1024);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = ClipStore.MaxUploadBytes);
builder.Services.AddSingleton<ClipStore>();
builder.Services.AddHostedService<ExpiredClipCleanup>();
var app = builder.Build();
var store = app.Services.GetRequiredService<ClipStore>();
var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(store.AccessKey));

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new { error = "The clip service could not complete this request. Check available storage and try again." });
}));

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; media-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/c"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});

app.UseDefaultFiles();
app.UseStaticFiles();

bool IsAuthorized(HttpRequest request)
{
    var value = request.Headers["X-FunClipper-Key"].ToString();
    return value.Length is > 0 and <= 512
        && CryptographicOperations.FixedTimeEquals(keyHash, SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

object View(ClipMetadata clip, HttpRequest request) => new
{
    clip.Id, clip.Title, clip.CreatedAt, clip.ExpiresAt, clip.SizeBytes,
    url = $"{store.LinkBase(request)}/c/{clip.Id}",
    mediaUrl = $"{store.LinkBase(request)}/media/{clip.Id}"
};

app.MapGet("/api/health", () => Results.Ok(new
{
    product = "FunClipper", status = "ok", scope = "local", maxUploadBytes = ClipStore.MaxUploadBytes,
    expiresAfterDays = store.ExpiryDays, publicUrlConfigured = store.HasPublicUrl
}));

app.MapGet("/api/clips", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request)) return Results.Json(new { error = "Enter the local access key to open your library." }, statusCode: 401);
    var clips = await store.ListAsync(cancellationToken);
    return Results.Ok(clips.Select(clip => View(clip, request)));
});

app.MapPost("/api/clips", async (HttpContext context, CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(context.Request)) return Results.Json(new { error = "A valid local access key is required to upload a clip." }, statusCode: 401);
    if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
        || !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Upload an MP4 using a multipart form with a file field." });

    var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
    if (string.IsNullOrEmpty(boundary) || boundary.Length > 128)
        return Results.BadRequest(new { error = "The upload form is invalid." });

    UploadDraft? draft = null;
    try
    {
        var reader = new MultipartReader(boundary, context.Request.Body)
        {
            BodyLengthLimit = ClipStore.MaxUploadBytes,
            HeadersLengthLimit = 16 * 1024,
            HeadersCountLimit = 16
        };
        var title = "";
        var sectionCount = 0;
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (++sectionCount > 4) throw new InvalidDataException("The upload form has too many fields.");
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The upload form is invalid.");
            var field = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
            if (field == "file")
            {
                if (draft is not null) throw new InvalidDataException("Upload one clip at a time.");
                draft = await store.StageAsync(section.Body, cancellationToken);
            }
            else if (field == "title")
            {
                using var memory = new MemoryStream();
                await ClipStore.CopyBoundedAsync(section.Body, memory, 1024, cancellationToken);
                title = Encoding.UTF8.GetString(memory.ToArray());
            }
            else throw new InvalidDataException("The upload form contains an unsupported field.");
        }
        if (draft is null) return Results.BadRequest(new { error = "Choose an MP4 file to upload." });
        var clip = await store.CommitAsync(draft, title, cancellationToken);
        draft = null;
        return Results.Created($"/c/{clip.Id}", View(clip, context.Request));
    }
    catch (UploadTooLargeException)
    {
        return Results.Json(new { error = "This clip exceeds the 100 MB upload limit. Trim it before uploading." }, statusCode: 413);
    }
    catch (InvalidDataException exception)
    {
        return exception.Message.StartsWith("Multipart body length limit", StringComparison.Ordinal)
            ? Results.Json(new { error = "This clip exceeds the 100 MB upload limit. Trim it before uploading." }, statusCode: 413)
            : Results.BadRequest(new { error = exception.Message });
    }
    catch (BadHttpRequestException exception) when (exception.StatusCode == 413)
    {
        return Results.Json(new { error = "This clip exceeds the 100 MB upload limit. Trim it before uploading." }, statusCode: 413);
    }
    finally
    {
        if (draft is not null) store.Discard(draft);
    }
}).DisableAntiforgery();

app.MapDelete("/api/clips/{id}", async (string id, HttpRequest request, CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request)) return Results.Json(new { error = "A valid local access key is required to delete a clip." }, statusCode: 401);
    var deleted = await store.DeleteAsync(id, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound(new { error = "This clip is unavailable or has expired." });
});

app.MapGet("/api/view/{id}", async (string id, HttpRequest request, CancellationToken cancellationToken) =>
{
    var clip = await store.FindAsync(id, cancellationToken);
    return clip is null ? Results.NotFound(new { error = "This clip is unavailable or has expired." }) : Results.Ok(View(clip, request));
});

app.MapMethods("/media/{id}", ["GET", "HEAD"], async (string id, CancellationToken cancellationToken) =>
{
    var clip = await store.FindAsync(id, cancellationToken);
    return clip is null ? Results.NotFound() : Results.File(store.MediaPath(clip.Id), "video/mp4", enableRangeProcessing: true,
        lastModified: clip.CreatedAt, entityTag: new EntityTagHeaderValue($"\"{clip.Id}\""));
});

app.MapGet("/c/{id}", (string id) => ClipStore.IsValidId(id)
    ? Results.File(Path.Combine(app.Environment.WebRootPath, "viewer.html"), "text/html")
    : Results.NotFound());

app.Run();
