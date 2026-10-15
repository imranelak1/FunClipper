# Performance validation

The aim is smooth gameplay on modest PCs. The current prototype has no established low-end compatibility or comparative FPS claim.

## Test profiles

| Profile | Video | Encoded bitrate | Replay |
|---|---|---|---|
| Minimum | 854×480, 30 FPS | 1.5 Mbps | 15 seconds |
| Low-end | 1280×720, 30 FPS | 3.5 Mbps | 30 seconds |
| Standard | 1920×1080, 60 FPS | 8 Mbps | 30 seconds |

Replay segments are written to disk and pruned. An in-progress segment and a few bookkeeping segments add small headroom. A clip-save snapshot temporarily duplicates the selected encoded files. Saved clips are retained until the user removes them; a saved-library quota is not yet implemented.

Hardware H.264 is preferred. The Minimum profile permits an explicitly labeled two-thread CPU encoder when hardware probing fails; higher profiles fail clearly. A machine without adequate recording headroom may need reduced quality or manual capture in a future mode.

## Current evidence

- Development PC: Ryzen 7500F, Radeon RX 6600 XT 8 GB, 32 GB DDR5.
- Synthetic hardware encoding and exact hardware trim passed.
- AMD GPU resizing passed every profile with 16:9, 4:3, and portrait synthetic inputs, preserving the display geometry and output audio. The route performs frame copies and aspect-preserving padding; it is not a zero-copy pipeline.
- Desktop Duplication plus AMD GPU resizing and encoding passed a 30-frame null-output probe after explicit constant-frame-rate normalization. No screen footage was saved. The earlier unnormalized VPP probe produced repeated DTS warnings; the normalized probe was quiet, and packet-order checks cover repeated synthetic input timestamps.
- API streaming and video playback passed without a browser framework.
- `artifacts/prototype-vitals.json` records the actual measurement context and samples for an offscreen UI with a synthetic clip, capture stopped. It includes startup/JIT and preview activity. It is neither an idle-tray measurement nor a game benchmark, and must not be presented as one.
- GPU load groups per-process Windows counters by adapter/engine and reports the busiest engine; parallel engine percentages are not added together. Dedicated and shared GPU memory remain separate.
- CPU/GPU usage does not by itself establish a bottleneck, and recorder output FPS is not game FPS.

## October 15 recorder repair

Live testing found that AMD ordinary I-frames did not close replay segments reliably. Forced IDRs now produce independently decodable one-second segments. Audio pipe/monitor work runs independently of the UI dispatcher; the raw PCM sample clock no longer skips elapsed time after pipe backpressure. Source timestamps are preserved and resampled to the requested frame rate rather than compressed when capture misses frames.

Below-normal Windows priority starved real media jobs on this heavily loaded host: a generated six-second export sat idle for minutes and finished immediately after its priority was raised. Capture, probes and exports therefore use normal scheduling, bounded queues and encoder/filter threads, and hardware encoding where available. Low priority is not treated as proof of low recording overhead.

The final controlled live test used an app-owned moving fullscreen pattern at 1280×720 / 30 FPS, AMD AMF with GPU resize and a generated 440 Hz audio input through the production PCM pipe. Repeated saves yielded 5.05-second and 18.06-second MP4s while the same capture PID continued. Both fully decoded without errors, had strictly increasing video DTS, non-silent AAC and audio/video durations within 250 ms. The replay timeline covered 0.95× elapsed wall time under the measured load. Stop removed the capture process, replay segments and temporary snapshots. Test media was deleted, retaining text diagnostics.

This establishes a short recorder-flow check on this PC. It does not measure a game's FPS, long-session drift, actual sound-device latency or low-end compatibility. The floating widget is a normal topmost desktop window; exclusive fullscreen can cover it. The native runtime check confirmed that this Windows version accepts capture exclusion and showing the widget does not activate it.

## Game detection cost

Game detection scans native process identities every 2.5 seconds on a worker task. It requests only limited image-path/creation-time information, caches process metadata and classifications, prunes exited processes, and bounds caches to 8,192 entries. Local Steam/Epic install manifests refresh at most every five minutes unless the user chooses Refresh. There is no per-frame scan, process-module inspection, injection, elevation or network catalog request. This design keeps work bounded; it is not a measured gameplay-overhead or low-end compatibility claim.

## Acceptance measurements

Use the same game scene, settings, resolution, FPS cap, driver, and recording profile for each run. Compare recording off, FunClipper on, and Medal on. Keep the performance telemetry collection identical across comparisons.

Measure average game FPS, 1% low FPS and frame-time distributions, encoder dropped frames, CPU usage, physical RAM, dedicated/shared GPU memory, disk write rate, and output audio synchronization. Include startup, 30-minute capture, repeated hotkey saves, trimming during gameplay, uploading, and closing/quitting.

Run on real older integrated-graphics hardware with 4–8 GB RAM as well as the development PC. A constrained VM on the development machine cannot prove support for an older GPU/driver or shared-memory bottleneck. Test long sessions and sleep/display changes before making reliability claims.

Measure task friction separately: saved-clip availability, time to make a trim, user interactions before sharing, and time until a uploaded link is playable. Network upload speed must be reported independently of editor latency.
