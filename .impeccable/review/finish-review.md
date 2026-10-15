# FunClipper finish review

## 1. Disposition

**Fix before local prototype handoff.** Three narrow source defects affect trimming, clipboard recovery, and native connection accessibility. No visual rebuild is warranted. Recapture affected native screens only if the fixes change visible layout; the current captures otherwise represent the reviewed utility direction.

## 2. Evidence and scope

Reviewed `PRODUCT.md`, `.impeccable/surface-brief.md`, Impeccable's `SKILL.md`, `reference/craft-floor.md` and `reference/polish.md`, and `uncodixfy/SKILL.md`. This is an Operate utility with an explicit save-hotkey -> direct edit/share -> copied-link flow. It has no approved visual comp; the review judges its stated compact, neutral, restrained-purple direction.

Opened **each** required image with `view_image`: `desktop.png`, `editor.png`, `web-desktop.png`, `web-mobile.png`, `web-viewer-desktop.png`, `web-viewer-mobile.png`, `web-upload-mobile.png`, `web-connect-desktop.png`, and `web-empty-desktop.png`. All are under `.impeccable/review/`. The native images show WPF client contents rather than operating-system chrome. Web evidence covers desktop and 390px mobile widths. The moving testsrc bars are intentional, playable synthetic media used for testing; they are not product hero art or shipping decorative imagery.

Inspected the desktop XAML, main/editor handlers, connection dialog, share client and vitals sampler, plus the web HTML, CSS and JavaScript and core trim implementation. The default captures show clear hierarchy, readable controls, stable spacing, responsive rows/player, and truthful local-link notices. Source includes opt-in capture, a disabled save control while stopped, a hotkey-conflict fallback, row/tray edit and share actions, native save notifications, bounds checks, original-preserving exports, cached edit invalidation, muted preview, busy states, browser upload progress/cancellation, labeled web controls, visible focus styling, skip links, status regions, empty states, and clipboard fallbacks.

The parent supplied prior functional API/browser evidence using real video, including mobile playback. This reviewer did not repeat those tests or launch a browser. A direct PowerShell arithmetic check reproduced finding F1. No new detector was run: the prior web result was `[]` in degraded regex mode because its parser was not installed, and is not full contrast/accessibility proof. WPF has no detector result. Keyboard and accessibility judgments here are source-based, not a completed assistive-technology session. No public deployment, FPS/temperature integration, or game-overhead claim is certified by this review.

## 3. Material findings

### F1 — Minimum valid trim can fail export (P2)

**Location:** `src/FunClipper.Desktop/EditorWindow.xaml.cs:88` and `src/FunClipper.Core/MediaTools.cs:66`.

The editor permits a 0.1-second range, but the core rejects `end - start < 0.1` without tolerance. On the captured six-second clip, `start = 5.9`, `end = 6` produces `0.099999999999999645`, so the UI-valid minimum range is rejected. This is a quick-edit correctness defect, not a capability limit.

**Precise remedy:** Normalize bounds consistently or apply a small numeric tolerance to the minimum-duration comparison while preserving rejection of genuinely shorter selections. Add the six-second/5.9-second case to the existing core bounds coverage and verify the resulting export duration.

### F2 — Main-window clipboard recovery cannot be copied (P2)

**Location:** `src/FunClipper.Desktop/MainWindow.xaml.cs:207` and `src/FunClipper.Desktop/MainWindow.xaml:77`.

After a successful row/tray upload, a busy clipboard produces “copy this link” followed by the URL in `ActivityStatus`. That control is a `TextBlock`, so the offered link cannot be selected and copied. The editor and web already have selectable recovery controls.

**Precise remedy:** Use a read-only selectable text control or dedicated URL fallback in the main window, then focus/select the URL when clipboard copying fails. Keep the fallback's local/hosted scope clear and avoid uploading the same clip again merely to recover its URL.

### F3 — Native connection fields lack accessible names (P2)

**Location:** `src/FunClipper.Desktop/ConnectionWindow.cs:15` and `:17`.

“Server URL” and “Access key” are neighboring `TextBlock` elements, with no `Label.Target`, `AutomationProperties.LabeledBy`, or `AutomationProperties.Name` on their `TextBox`/`PasswordBox`. The fields are keyboard reachable but their purpose is not programmatically identified for screen readers.

**Precise remedy:** Add `AutomationProperties.Name` to both controls, or bind proper labels to them; retain the key help and expose validation feedback through an appropriate accessible status mechanism. Confirm field names with a native accessibility inspector or screen reader.

## 4. Nonblocking limits

- Public hosting, account management, quotas and deployment controls are future work. Localhost links are explicitly labeled in the reviewed desktop success messages and web surfaces; hosting is not implied by a copied local URL.
- Game FPS, temperatures, frame-time/stutter attribution, in-game overlay, automatic game detection and adaptation are pending. The UI says “not connected” or “unavailable”; its displayed CPU/RAM/GPU/process readings come from native APIs rather than fabricated values. GPU readings represent the busiest Windows counter engine and are not proof of recording cost or game health.
- Low-end compatibility, live-game overhead, long sessions and audio synchronization need hardware testing. Profile names and the synthetic resource screenshot do not establish performance guarantees. Independent AMD core optimization does not change this UI verdict.
- The native balloon/tray and direct row actions satisfy the accepted no-focus-stealing save-to-edit path at this milestone. Reliance on Windows notification settings is a platform constraint; row/tray recovery remains available.
- The web uploads finished MP4s rather than trimming/transcoding in-browser. Server signature validation does not guarantee the entire video decodes; the viewer supplies a download recovery path. Both are documented candidate limits.
- No shipping raster artwork or generated visual assets were identified. The tray uses Windows `SystemIcons.Application`; video-derived posters and test media are functional content. There is no missing marketing-art requirement for this utility.
- The captures do not cover native Connection, active recording, every long-content/error state, all DPI scales or assistive technology. These are evidence limits rather than observed layout failures. The screenshot set is adequate for a local review, not broad release certification.

## 5. Quality verdict

The interface meets the ordinary utility goal: recent clips and useful actions are easy to find, the editor gives media the largest area, and the web library/viewer work at the captured desktop and mobile sizes. It avoids generic hero panels, metric-card grids, decorative gradients and asset filler. The muted palette, modest corners, separators and consistent action styling fit the agreed direction.

**Visual quality passes for a local prototype; task reliability/accessibility need F1–F3 fixed.** After those narrow fixes and their targeted checks, ship the local prototype with its documented limits. Further taste-driven polish or a new visual direction is not justified by this evidence.
