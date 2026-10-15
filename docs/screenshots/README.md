# Screenshot provenance

Captured from FunClipper on October 15, 2026. The PNGs are unchanged application captures, copied from the native smoke and isolated browser checks. They contain generated test footage rather than gameplay or user recordings. Vitals reflect the capture machine at that moment and do not establish recording overhead.

| File | Source and context |
| --- | --- |
| `desktop.png` | Native WPF game smoke: capture stopped, generated clips in the library and an actually detected controlled test process. |
| `editor.png` | Native WPF smoke: decoded frame from an FFmpeg-generated color-bar test clip. |
| `recording-widget.png` | Native WPF game smoke: expanded widget with sampled vitals and the detected controlled process; capture stopped. |
| `recording-widget-collapsed.png` | Native WPF smoke: collapsed widget; capture stopped. |
| `web-library.png` | Isolated browser check at 1440px: uploaded generated MP4, local-preview scope. |
| `web-viewer.png` | Isolated browser check: decoded generated video with native playback controls. |
| `web-mobile.png` | Isolated browser check at 390px: responsive clip library. |
| `games.png` | Native WPF game smoke: running owned test executable registered in isolated settings. This is not a commercial game or a gameplay benchmark. |

To reproduce native review captures, run `dotnet run --project src/FunClipper.Desktop -- --smoke` from the repository root. For detected-game captures, first build `tests/FunClipper.GameChecks` and append `--game-smoke` to that smoke command. The owned fixture is stopped and its temporary registration is removed afterward. Browser capture setup is documented in [the web README](../../src/FunClipper.Web/README.md); current checks are in `src/FunClipper.Web/tests/v2-ui.cjs`. Generated review evidence stays local under `.impeccable/review/`.
