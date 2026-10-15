# FunClipper web companion

Run `dotnet run --project src/FunClipper.Web` from the repository root. It listens on `http://127.0.0.1:5180` by default. No third-party frontend or server packages are required.

Uploads and the library require `X-FunClipper-Key`. Set `FUNCLIPPER_SHARE_KEY` to a random secret, or read the generated `%LOCALAPPDATA%\FunClipper\shared\access-key.txt` through the desktop app. Desktop can open `http://127.0.0.1:5180/#key=<URL-encoded-key>`; the browser stores it only for its tab and removes it from the address bar. Each clip link is a separate random 128-bit capability: anyone with that link can view or download the clip until deletion or expiry.

`POST /api/clips` accepts multipart `file` (MP4) and optional `title`. It streams at most 100 MiB to disk and checks the container's `ftyp` signature. That check detects incorrect file types; it does not prove that the entire video is decodable. `GET /api/clips` returns the authenticated library. `DELETE /api/clips/{id}` revokes a clip. `/c/{id}` is the viewer and `/media/{id}` supports browser byte ranges. Clips expire after 14 days; `FUNCLIPPER_EXPIRY_DAYS` accepts 1–365. Set `FUNCLIPPER_SHARE_DIR` to change storage.

## Verify locally

Use disposable storage and a known test key in a separate PowerShell terminal:

```powershell
$env:FUNCLIPPER_SHARE_DIR = Join-Path $PWD 'src\FunClipper.Web\tests\scratch'
$env:FUNCLIPPER_SHARE_KEY = 'funclipper-isolated-smoke-key-2026'
dotnet run --project src/FunClipper.Web --urls http://127.0.0.1:5182
```

Then generate a real MP4 and run the standard-library integration checks:

```powershell
$env:FUNCLIPPER_SHARE_KEY = 'funclipper-isolated-smoke-key-2026'
ffmpeg -hide_banner -loglevel error -f lavfi -i testsrc2=size=640x360:rate=30 -t 2 -c:v libx264 -pix_fmt yuv420p -movflags +faststart -y src/FunClipper.Web/tests/smoke.mp4
node src/FunClipper.Web/tests/smoke.mjs http://127.0.0.1:5182 src/FunClipper.Web/tests/smoke.mp4 src/FunClipper.Web/tests/scratch
```

These checks exercise unauthorized access, streaming upload, file signatures, anonymous playback, byte ranges, HEAD, path traversal, the actual 100 MiB boundary, expiry, and link revocation. The storage argument enables expiry testing by editing only metadata for a clip the script created. Use only the disposable service for these tests.

For the browser UI checks, keep that isolated service running and generate the MP4 above, then run from the repository root:

```powershell
Push-Location src/FunClipper.Web/tests
npm install
npx playwright install chromium
Pop-Location
$env:FUNCLIPPER_SHARE_KEY = 'funclipper-isolated-smoke-key-2026'
node src/FunClipper.Web/tests/v2-ui.cjs http://127.0.0.1:5182
```

Playwright is a test-only dependency. The shipped web app requires no Node runtime or frontend packages. `PLAYWRIGHT_EXECUTABLE_PATH` can select an existing Chromium/Chrome executable; `PLAYWRIGHT_MODULE` can select an existing Playwright installation. The UI check verifies upload, cancel, playback, copy recovery, authentication, deletion, keyboard focus and mobile layouts, and writes local screenshots under `.impeccable/review/`.

## Hosting preparation

The Dockerfile uses this folder as its build context:

```powershell
docker build -t funclipper-web src/FunClipper.Web
```

Set a unique `FUNCLIPPER_SHARE_KEY`, a persistent `/data` volume, and `FUNCLIPPER_PUBLIC_URL` to the actual HTTPS origin when hosting. Place the service behind HTTPS. A configured URL is only link generation; it does not deploy or establish reachability. No upload rate limiting, storage quota, accounts, or cloud deployment is included in this local prototype. Keep the service bound to localhost until deployment is configured.

The companion plays already edited MP4s. Trimming and muting are performed by the native desktop editor before upload; there is no browser transcoder.
