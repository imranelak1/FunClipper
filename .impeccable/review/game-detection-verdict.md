# Game detection finish review

Reviewed October 15, 2026. Verdict: **pass for the scoped local game-detection feature**. No blocking source or visual findings remain in this review.

## Evidence reviewed

- Source: `GameDetection/`, `GamesWindow.xaml/.cs`, main-window and widget integration, and the core game-label save/trim changes.
- Fresh native smoke renders: `desktop.png`, `desktop-minimum.png`, `games.png`, `games-minimum.png`, `overlay.png`, and `overlay-collapsed.png` in this directory.
- The rendered detected title comes from discovery of an owned `FunClipper.GameChecks.exe --fixture-child` process registered in isolated temporary settings. It is a test fixture, not evidence of commercial-game compatibility. The smoke did not start recording or system-audio capture.
- The GameChecks source covers manifest parsing, exclusions, folder boundaries, foreground selection, owned-process detection and exit, registration persistence, and malformed settings. Actual test-run outcomes are reported separately by the implementation checks.

## Findings

The Games entry fits the existing main-window header at the declared minimum width. Game status sits with the capture controls and explicitly retains the screen-1 capture explanation. The Games manager keeps the executable picker direct, distinguishes running titles from custom registrations, and fits at its 540 by 480 minimum. Both lists retain scrolling. The widget adds one truncated title line with a full tooltip; capture controls and metrics fit at 350 by 200, and the collapsed widget remains compact.

Process scans run on a worker every 2.5 seconds. Installed metadata refreshes separately every five minutes. Classification caches are bounded and pruned; cached process paths verify creation times against PID reuse. Native handles close in `finally` blocks. There is no process injection, module enumeration, elevation, network lookup, or recording triggered by detection. UI handlers dispatch worker events and detach at shutdown. Custom registrations use an atomic settings replacement and do not alter game files.

The review identified a common Steam-software false-positive risk. The implementation added manifest and executable exclusions for Aseprite, Krita and Source Filmmaker, with corresponding checks. No further change was requested in this bounded visual pass.

## Limits retained

Detection remains best-effort: installed-root classification can miss protected processes or mistake an unlisted auxiliary executable for a game. The known-name fallback is intentionally small. Generic Java processes are excluded automatically; a user may register the actual Java executable, which consequently identifies that executable rather than its command line. Registration updates can take one scan interval to appear.

Game labels describe the detected running title. They do not establish that every frame in a replay belongs to that title. Capture remains opt-in and records the first display, including other visible windows. This review makes no game-only capture, game-FPS, exclusive-fullscreen overlay, low-end performance, or commercial-game compatibility claim. Published-app UI automation and package verification remain separate release checks.
