# Screenshot provenance

Captured from FunClipper on October 15, 2026. The PNGs are unchanged application captures, copied from the native smoke and isolated browser checks. They contain generated test footage rather than gameplay or user recordings. Vitals reflect the capture machine at that moment and do not establish recording overhead.

| File | Source and context |
| --- | --- |
| `desktop.png` | Native WPF smoke: capture stopped, generated clips in the library. |
| `editor.png` | Native WPF smoke: decoded frame from an FFmpeg-generated color-bar test clip. |
| `recording-widget.png` | Native WPF smoke: expanded widget with sampled vitals; capture stopped. |
| `recording-widget-collapsed.png` | Native WPF smoke: collapsed widget; capture stopped. |
| `web-library.png` | Isolated browser check at 1440px: uploaded generated MP4, local-preview scope. |
| `web-viewer.png` | Isolated browser check: decoded generated video with native playback controls. |
| `web-mobile.png` | Isolated browser check at 390px: responsive clip library. |

To reproduce native review captures, run `dotnet run --project src/FunClipper.Desktop -- --smoke` from the repository root. Browser capture setup is documented in [the web README](../../src/FunClipper.Web/README.md); current checks are in `src/FunClipper.Web/tests/v2-ui.cjs`. Generated review evidence stays local under `.impeccable/review/`.
