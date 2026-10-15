# Native recorder core

`FunClipper.Core` is a .NET 10 class library. It opens no window and starts no capture
when constructed. `StartAsync` must be called following the user's recording action.
It requires Windows 10 or newer plus FFmpeg/FFprobe. FFmpeg must include `ddagrab`,
H.264 encoders, AAC, and the segment muxer. Set `FUNCLIPPER_FFMPEG` to the full
FFmpeg executable path, place both executables in the application `tools` folder,
or provide them on PATH. The executables are not bundled by this library.

## Capture and cost

| Profile | Output | Replay target | Video bitrate |
| --- | --- | --- | --- |
| Minimum | 854×480, 30 FPS | 15 seconds | 1.5 Mbps |
| LowEnd | 1280×720, 30 FPS | 30 seconds | 3.5 Mbps |
| Standard | 1920×1080, 60 FPS | 30 seconds | 8 Mbps |

The first display is captured with Desktop Duplication. This captures the entire
display, including the desktop and notifications; it is not a game hook or
game-only capture. Borderless window mode is recommended for this prototype.
The CPU and copied AMD GPU resizing routes preserve the display aspect ratio and
add black bars where needed. The optional direct D3D11 scaling route for other
encoders scales directly to the profile size.

Encoder selection first runs eight **synthetic** frames through each candidate,
starting with AMD AMF, followed by NVIDIA NVENC and Intel Quick Sync. AMD then
probes D3D11 synthetic frames downloaded as BGRA, resized and converted by
`vpp_amf`, and downloaded at the reduced resolution for padding before AMF encoding.
The status explicitly says `AMD AMF · GPU resize · frame copy`. This route copies
frames between RAM and the GPU; it is not zero-copy. Other hardware encoders probe
the direct D3D11 scaling path. If GPU scaling fails, the status explicitly says
`CPU resize`; encoding remains on the GPU. A compatible
hardware encoder is required for LowEnd and Standard. Minimum alone permits
480p software x264 with two threads and a visible `CPU · 2 threads` status.
There is no automatic full-resolution software recording retry.

This machine's synthetic copied AMD VPP pipeline succeeds. One-second outputs
at all three profiles retain their dimensions and synthetic AAC audio; decoded
16:9, 4:3, and portrait test patterns retain their aspect ratio with padding.
Its direct D3D11 scaling probe fails when allocating a texture. Game performance
and the cost of these frame copies have not been benchmarked.
These profiles are quality targets, not guarantees for every low-end PC.
Capture explicitly normalizes output to the profile frame rate with FFmpeg's
constant-frame-rate mode, preventing repeated capture timestamps from producing
duplicate encoded DTS. Synthetic repeated-PTS inputs pass packet-order checks.

The buffer contains encoded one-second MPEG-TS segments on disk, not raw frames
in RAM. Retention includes a two-second margin; the segment CSV is also bounded.
At 8 Mbps, a 30-second replay contains about 30 MB of encoded video plus overhead.
The monitor prunes completed files every 200 ms and removes missed older files
after an application pause. Buffer writes consume disk bandwidth while recording.

## Audio

`NAudio.Wasapi` 2.3.0 provides WASAPI loopback of the default system playback
device at stereo 48 kHz float. A bounded half-second PCM queue feeds a unique
Windows named pipe into FFmpeg for AAC encoding. The clocked writer inserts
silence when Windows delivers no packets during silence. Audio has its own pipe,
so FFmpeg stdin remains available for clean `q` shutdown.

This records all audio on the chosen system playback device, including other
apps. It does not record the microphone. Changing playback devices mid-recording
requires restarting recording. If WASAPI initialization fails, recording proceeds
with a clearly labeled video-only status. Later audio errors appear in
`AudioStatus`. The synthetic named-pipe test confirms valid AAC output without
capturing real audio; live WASAPI synchronization still needs user testing.

## Save, edit, and lifecycle

`SaveReplayAsync` waits for the next completed segment (normally about one second;
at most two seconds), copies completed segments while holding the same lock used
by pruning, then releases the ring and remuxes the copies to MP4. Capture continues.
Complete intervals inside the requested replay window are selected, so the
saved duration can be about one second shorter than the profile's replay target.
The segment closing after the hotkey can include up to one second after the press.
The operation never reads a segment still being written. Cancellation removes
snapshot and partial-output files. Saves can overlap safely after their snapshots
have been made.

`TrimAsync` makes a new MP4 using exact selected bounds, optional audio removal,
H.264, and `faststart` metadata. It preserves the source and validates actual media
duration. A known capture hardware encoder is reused; other imports and demos use
two-thread ultrafast x264. Hardware export failures are reported directly instead
of silently switching to software. Trim exports re-encode and consume resources;
they are user-initiated. File paths are passed through `ProcessStartInfo.ArgumentList`
and never through a command shell.

`StopAsync` stops the audio writer, requests graceful FFmpeg shutdown, waits up
to three seconds, then terminates a stuck process tree. `DisposeAsync` calls Stop.
On Windows, each FFmpeg process is attached to a kill-on-close Job Object where
Windows permits it, so application termination also stops the child process.
Completed clips remain in `ClipsDirectory`; transient session files are removed.
The clip catalog uses JSON sidecars and ignores `.vitals.json` sidecars.
An unexpected FFmpeg exit or monitor failure also stops and cleans up the session,
including loopback audio. Cleanup continues if the audio device reports a shutdown
error. A failing status subscriber cannot interrupt capture ownership or cleanup.

`StatusChanged` can run on a worker thread; UI callers must marshal it.
`CaptureProcessId` identifies the FFmpeg process for real resource accounting.
`DroppedFrames` is FFmpeg's reported output drop counter, initially null; it is
not game FPS, game frametime, or a measure of GPU presentation stutter.

## Verification

Run from the repository root:

```powershell
dotnet run --project tests/FunClipper.CoreChecks/FunClipper.CoreChecks.csproj
```

The checks create synthetic media only. They verify demo metadata/catalog,
accurate trim duration/audio removal, invalid bounds, original preservation,
media process cancellation, bounded rolling retention, independent concurrent
snapshots, replay remux with audio, Windows named-pipe audio ingestion, and
synthetic hardware encoder detection, AMD copied GPU resizing and letterbox
geometry when available, and automatic session cleanup after unexpected process
exit or reader failure. No screen or real system audio is recorded.

References: [FFmpeg documentation](https://ffmpeg.org/ffmpeg-all.html),
[NAudio 2.3 release](https://github.com/naudio/NAudio/releases/tag/v2.3.0),
[WASAPI loopback](https://github.com/naudio/NAudio/blob/v2.3.0/Docs/WasapiLoopbackCapture.md),
[Windows Job Objects](https://learn.microsoft.com/windows/win32/procthread/job-objects).
