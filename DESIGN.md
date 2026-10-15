---
name: "FunClipper"
description: "A dark Windows clip utility with a compact floating recording widget and web companion."
colors:
  background: "#111318"
  surface: "#191c23"
  elevated: "#222630"
  text: "#f3f4f8"
  muted: "#a6adbd"
  border: "#343946"
  control-border: "#626b7e"
  accent: "#b7a1ff"
  accent-hover: "#c8b8ff"
  accent-active: "#a58be8"
  accent-ink: "#1b1230"
  record-error: "#fb7185"
  preparing: "#fbbf24"
  idle: "#8992a4"
  control-hover: "#2c313d"
  control-hover-border: "#7a8499"
  native-field: "#16191f"
  native-player: "#090b0f"
  web-player: "#090a0d"
  overlay-border: "rgba(83, 91, 109, 0.396078)"
  overlay-background: "rgba(25, 28, 35, 0.847059)"
typography:
  native-display:
    fontFamily: "Bahnschrift"
    fontSize: "24px"
    fontWeight: 600
  native-title:
    fontFamily: "Bahnschrift"
    fontSize: "22px"
    fontWeight: 600
  native-section:
    fontFamily: "Bahnschrift"
    fontSize: "18px"
    fontWeight: 600
  native-body:
    fontFamily: "Bahnschrift"
    fontSize: "14px"
  native-label:
    fontFamily: "Bahnschrift"
    fontSize: "12px"
  native-measurement:
    fontFamily: "Cascadia Mono"
    fontSize: "12px"
  widget-reading:
    fontFamily: "Bahnschrift"
    fontSize: "18px"
    fontWeight: 600
  widget-label:
    fontFamily: "Bahnschrift"
    fontSize: "10px"
  widget-buffer:
    fontFamily: "Bahnschrift"
    fontSize: "11px"
  web-title:
    fontFamily: "Bahnschrift, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "-0.015em"
  web-section:
    fontFamily: "Bahnschrift, sans-serif"
    fontSize: "1.0625rem"
    fontWeight: 600
    lineHeight: 1.4
  web-brand:
    fontFamily: "Bahnschrift, sans-serif"
    fontSize: "1.125rem"
    fontWeight: 600
    lineHeight: 1.5
  web-body:
    fontFamily: "Bahnschrift, sans-serif"
    fontSize: "16px"
    lineHeight: 1.5
  web-button:
    fontFamily: "Bahnschrift, sans-serif"
    fontSize: "0.875rem"
    lineHeight: 1.5
  web-label:
    fontFamily: "Bahnschrift, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 600
    lineHeight: 1.5
  web-meta:
    fontFamily: "Bahnschrift, sans-serif"
    fontSize: "0.8125rem"
    lineHeight: 1.5
rounded:
  native-button: "7px"
  native-panel: "9px"
  web-control: "6px"
  web-panel: "8px"
  preview: "5px"
  widget: "12px"
  mobile-video: "4px"
spacing:
  xs: "4px"
  sm: "8px"
  compact: "12px"
  md: "16px"
  capture: "18px"
  panel: "20px"
  lg: "24px"
  header: "28px"
  wide: "32px"
components:
  native-button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
    typography: "{typography.native-body}"
    rounded: "{rounded.native-button}"
    padding: "8px 14px"
  native-button-standard:
    backgroundColor: "{colors.elevated}"
    textColor: "{colors.text}"
    typography: "{typography.native-body}"
    rounded: "{rounded.native-button}"
    padding: "8px 14px"
  native-capture-panel:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.native-panel}"
    padding: "18px"
  native-field:
    backgroundColor: "{colors.native-field}"
    textColor: "{colors.text}"
    typography: "{typography.native-body}"
    padding: "8px"
  web-button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
    typography: "{typography.web-button}"
    rounded: "{rounded.web-control}"
    padding: "7px 12px"
  web-button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
    textColor: "{colors.accent-ink}"
  web-button-primary-active:
    backgroundColor: "{colors.accent-active}"
    textColor: "{colors.accent-ink}"
  web-button-standard:
    backgroundColor: "{colors.elevated}"
    textColor: "{colors.text}"
    typography: "{typography.web-button}"
    rounded: "{rounded.web-control}"
    padding: "7px 12px"
  web-button-standard-hover:
    backgroundColor: "{colors.control-hover}"
    textColor: "{colors.text}"
  web-button-standard-active:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
  web-button-text:
    backgroundColor: "transparent"
    textColor: "{colors.muted}"
    typography: "{typography.web-button}"
    rounded: "{rounded.web-control}"
    padding: "7px 12px"
  web-button-delete:
    backgroundColor: "transparent"
    textColor: "{colors.record-error}"
    typography: "{typography.web-button}"
    rounded: "{rounded.web-control}"
    padding: "7px 12px"
  web-field:
    backgroundColor: "{colors.background}"
    textColor: "{colors.text}"
    typography: "{typography.web-body}"
    rounded: "{rounded.web-control}"
    padding: "8px 10px"
  web-upload-panel:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.web-panel}"
    padding: "20px"
  web-clip-row:
    textColor: "{colors.text}"
    typography: "{typography.web-body}"
    padding: "16px 0"
  web-navigation:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    padding: "10px 28px"
  floating-widget:
    backgroundColor: "{colors.overlay-background}"
    textColor: "{colors.text}"
    typography: "{typography.native-label}"
    rounded: "{rounded.widget}"
    padding: "12px 14px"
    width: "350px"
---
# Design System: FunClipper

## Overview

**Creative North Star: "The compact recording dock"**

FunClipper uses charcoal surfaces, pale violet actions, bright readable text and compact controls. Media and recent clips lead the main windows; the floating native widget groups recording state, real readings and immediate capture/save actions into a small corner surface. The web companion carries the same palette with plain HTML, CSS and SVG icons.

The requested references are adapted as original native and DOM patterns: Vengeance's glass dock informs the translucent shell and grouped actions; Skiper informs compact expansion and stable reading regions; Animmaster informs short local transitions. No library source was copied and no paid component code was used. The Windows app embeds no browser runtime for these patterns. The built widget uses alpha transparency without background blur.

This document records the October 15 source and reviewed native/v2 web captures. The [October 15 finish verdict](.impeccable/review/v2-verdict.md) clears the local UI, including the resolved opacity and hotkey findings. This remains a local prototype: public hosting, game FPS/temperature integrations, exclusive-fullscreen widget compatibility and gameplay or low-end performance guarantees are not established.

**Key Characteristics:**

- Charcoal layers, pale violet actions and clear neutral control strokes.
- Compact grouped recording controls and stable-width reading regions.
- A translucent corner widget with opaque text and controls.
- Actual media, measured vitals and explicit loading, readiness and recovery states.

## Colors

The frontmatter owns the implemented color values. Shared native and web roles now align; retain the source-specific field/player colors and stronger web control strokes. Sidecar tonal ramps are synthesized visualization aids, not extra shipping colors.

### Primary

- **Pale Action Violet** (`accent`): Start capture, sharing/upload actions, focused controls, selection and brand icon details.
- **Light Action Violet** (`accent-hover`) and **Pressed Violet** (`accent-active`): browser primary-button hover and active states.
- **Deep Violet Ink** (`accent-ink`): readable dark text over pale violet actions. Native primary buttons use this ink, too.

### Neutral

- **Charcoal Canvas** (`background`), **Charcoal Surface** (`surface`) and **Raised Charcoal** (`elevated`): page/window, panel/header and secondary-control layers.
- **Soft White Ink** (`text`) and **Cool Secondary Ink** (`muted`): primary text and supporting metadata/help.
- **Panel Line** (`border`) separates regions. **Control Line** (`control-border`) gives browser buttons and fields stronger boundaries.
- **Hovered Control** and **Hovered Control Line** (`control-hover`, `control-hover-border`): browser secondary hover feedback.
- **Native Field**, **Native Player Black** and **Web Player Black**: the existing platform-specific field fill and dark media letterboxing.
- **Widget Background** and **Widget Edge** (`overlay-background`, `overlay-border`): the default rendered native shell. The background token reflects the nominal 85% setting after the source's 8-bit alpha conversion. The edge translates WPF ARGB `#65535B6D` into CSS RGBA for portability.

### Functional feedback

- **Recording/Error Rose** (`record-error`): native recording or attention state; browser errors and Delete actions.
- **Preparing Amber** (`preparing`): the widget's preparing state.
- **Idle Gray** (`idle`): the widget's stopped state.

**The State Has Words Rule.** Color accompanies the widget's Recording, Preparing capture or attention label; it never replaces the state text. Measurements do not receive invented health or severity colors.

## Typography

**Display and Body Font:** Bahnschrift on Windows; `Bahnschrift, sans-serif` in the browser. No downloaded font is required.

**Measurement Font:** Cascadia Mono for the native main vitals line, trim timestamps and widget elapsed timer. Browser dates, sizes, counts and upload progress use tabular numerals. The widget keeps readings in stable grid columns; its CPU/GPU/RAM values use Bahnschrift rather than a substituted monospaced face.

**Character:** Modest semibold headings, short labels and stable numeric regions keep the utility readable without a promotional type scale. Native line heights remain platform-owned.

### Hierarchy

- **Native display/title/section:** main app title, editor title and Recent clips. The connection title remains a source-local (21-unit) semibold variation.
- **Native body/label/measurement:** controls and ordinary text; metadata and status; aligned vitals/time values.
- **Widget:** CPU/GPU values use the reading role; RAM is a local (15-unit) semibold variation. Status, elapsed timer and action text are (12 units); the buffer line uses the buffer role, and CPU/GPU/RAM captions plus GPU-memory text use the label role. Captions are literal metric abbreviations, not decorative uppercase headings.
- **Web title/section/brand:** page/clip headings, form/empty-state sections and the header brand. At the mobile breakpoint the title becomes (1.375rem), retaining its existing weight and tracking.
- **Web body/button/label/meta:** body establishes the (16px) root scale; primary buttons additionally use (600) weight; field labels are semibold. Metadata becomes (0.75rem) with (1.6) line height on mobile. Explanatory connection/empty-state paragraphs remain (0.9375rem).

Use sentence case for actions and explanatory copy. Preserve MP4, CPU, GPU, RAM, GB and F8 as meaningful abbreviations.

## Layout

Native dimensions are WPF device-independent units; browser dimensions are CSS pixels. Compact internal spacing and larger outer margins define the rhythm.

- **Main window:** initial size (850 × 660), minimum (730 × 520), outer margin (24). Automatic header/capture/vitals/footer rows surround a star-sized recent-clip list. The capture panel has (18) padding and lower spacing. The profile selector remains (222) wide. Widget, Web library and Connection actions sit at the upper right.
- **Editor:** initial size (760 × 690), minimum (600 × 560), outer margin (24). The star-sized player dominates; Start/End rows use columns (56 / remaining / 76), followed by playback, mute, history and save/share controls.
- **Connection:** fixed (500 × 350), centered on its owner, outer margin (24), stacked labels/fields and a right-aligned Save connection action.
- **Widget:** fixed width (350), expanded height (178), collapsed height (54). The shell padding is (14 horizontal / 12 vertical); a (28)-high header contains state, timer and utility actions. Expanded metrics use (1 / 1 / 1.2) grid proportions; buffer/GPU-memory text and two equal-width action regions follow.
- **Widget position:** defaults to bottom right and docks to any of four corners with a (20)-unit work-area inset. Drag the header to snap to the nearest corner on the window's current monitor. Right-click offers corner, opacity and Hide widget commands. Corner, opacity and collapsed state persist locally. Collapse/expand re-docks after its height transition.
- **Web frame:** header minimum height (64px), padding (10px 28px), gap (20px). Main content is centered at maximum width (1080px), including padding (32px 24px); viewer maximum width is (1128px).
- **Web library/forms:** rows have (16px) gaps and vertical padding. Media previews are (144px) wide at (16:9). Upload fields use a (1.2fr / 1fr) grid with (24px) gap. Connection is a bordered surface of maximum width (600px) with (24px) padding; upload padding is (20px).
- **Web at 800px and below:** rows wrap; the action group moves beneath the text with a (160px) left offset aligned past the preview. Viewer details may wrap.
- **Web at 640px and below:** header uses (10px 16px) padding, main uses (24px 16px), button targets become at least (44px) high, forms become one column and panels use (16px) padding. Previews become (112px) wide, actions occupy a full row, and Delete aligns at its far end. Viewer details stack and video corners use the mobile-video radius.
- **Web at 420px and below:** Disconnect library occupies another header row. The mobile header is allowed to grow rather than compress its controls.
- **Media:** browser viewer uses full width, (16:9), maximum height (68vh) and `object-fit: contain`; native poster/player uses uniform scaling. Browser list previews use `object-fit: cover` for their small frame regions. Preserve the viewer's aspect ratio and letterboxing.

## Elevation & Depth

Depth comes from charcoal tonal layers, borders and the floating widget's background transparency. No app-owned shadow or blur effect is implemented. Desktop chrome requests the operating system's dark title bar; the widget is borderless and topmost.

The widget menu offers nominal background opacity (85%, 92%, 97%), with (85%) as the default and persisted values clamped to the supported interval. The earlier (65%) option was removed for contrast. Only the shell background changes alpha; text and controls remain opaque. Do not lower the whole window's opacity or imply an acrylic/backdrop-blur effect.

**The Readable Overlay Rule.** Keep the widget's text and action contrast independent of the user's background. Preserve the supported opacity floor and explicit state labels.

## Shapes

Controls are compact rounded rectangles. Native custom buttons and combo chrome use `native-button` corners; main native panels and the editor player use `native-panel`. Browser buttons/fields/player use `web-control`, browser forms use `web-panel`, previews use `preview`, and the widget uses `widget`. Browser video corners reduce on mobile.

The native empty-state glyph sits in a source-local (48 × 48) tonal square with (10)-unit corners. This is a functional empty-state marker, not a general badge pattern. Web notices are unboxed text with a bottom divider. No pills, avatars, decorative chips or social-card system is implemented.

Most borders are (1px). Native focused buttons use a (2-unit) violet border; web focus uses a separate outline. Native list-row hover and selection use existing charcoal/violet-tinted fills; selection is not inferred from button styling.

## Components

### Buttons and grouped actions

Buttons name their action directly. Pale violet primary fills mark native capture/share and browser connect/upload/viewer copy. Secondary buttons use raised charcoal; Delete remains rose in both its resting and hover states.

- **Native:** documented padding, minimum height (36), trailing gap (8), hover opacity (0.9), pressed opacity (0.74), disabled opacity (0.45), and a focused (2-unit) violet stroke. There is no duration-based animation in the shared button template.
- **Widget:** capture/save use (12-unit) type, minimum height (30) and padding (10 horizontal / 5 vertical). Collapse/Open buttons have minimum height (24), compact padding and transparent resting chrome. Their vector glyphs have accessible names and tooltips.
- **Web:** documented padding, minimum height (38px), icon/text gap (7px), standard control stroke, and no label wrapping. Primary text is semibold with deep-violet ink. Disabled opacity is (0.55). Text actions use transparent resting chrome; hover adds a raised surface. Delete retains rose and uses the panel-line border on hover.
- **Motion:** browser button background, border and text colors transition over (140ms ease); links transition color over the same duration. There is no hover translation or spring effect.

### Inputs / Fields

Web fields use canvas fill, strong control strokes, control corners, minimum height (40px), accent caret and the frontmatter padding. Labels sit (6px) above fields; help starts (8px) below. Placeholders use muted ink. The access key is a password field; the optional title is explicit. The native file picker has matching raised chrome and a (40px) minimum-height selector button.

Native text/password fields use native-field fill, light text, native panel-line strokes and light carets. Text boxes use (8) padding and violet selection. The custom native ComboBox uses dark raised chrome and a vector chevron; its popup is bordered and padded, with distinct highlighted and selected item fills. Disabled combo opacity is (0.5), and keyboard focus changes its border to violet. Sliders and checkboxes retain their existing native behavior.

Server URL and Access key have programmatic names; connection validation is a polite live region. Web `:focus-visible` is a (2px) violet outline offset by (3px) on interactive elements. Text selection uses action violet with deep-violet ink. Browser/native validation remains available; there is no custom invalid-field skin.

### Panels, navigation and icons

Functional panels use tonal fills and fine strokes without shadows or card-hover motion. Main/connection/editor windows retain normal native chrome; only the widget is borderless. Browser navigation is a single header with brand, scope divider and Disconnect library or Clip library at the far side. Skip links become visible on focus. No sidebar, tabs, active-route pill or feed exists.

Browser icons are original local SVG symbols, generally (17px) with rounded strokes (1.7px); the brand icon is (22px). Native row, empty-state and widget glyphs are local WPF vector paths. Keep labels alongside action icons; icon-only widget controls retain accessible names/tooltips. No library icon runtime or downloaded font is required.

### Clip rows and media

Native rows show a (72 × 44) play tile, clip name, smaller details and Edit/Share. That native tile is an action marker, not a decoded thumbnail. Native rows use padding (14 horizontal / 8 vertical), a small internal separator, hover fill `#242831`, selected fill `#2f293d`, and violet keyboard focus. Long native titles use ellipsis.

Browser rows show actual video-frame previews when media loads; a vector video icon remains as fallback. Muted inline preview videos load near the viewport, do not autoplay, and are hidden from accessibility navigation inside a labeled Watch link. Long browser titles and messages wrap. Web viewer playback uses native video controls with Download MP4 recovery. The web uploads finished MP4s; trimming/muting remains in the desktop editor.

### Floating recording widget

The widget groups real recording state, elapsed time, CPU/GPU/RAM, optional dedicated GPU-memory usage, replay buffer state and capture/save controls. Fixed columns and monospaced elapsed time keep changing readings from shifting adjacent controls. A small colored dot accompanies a text state rather than serving as the only status.

State copy includes Not recording, Preparing capture…, Recording, Saving clip… and Capture needs attention. The buffer line distinguishes Replay buffer off, buffered seconds versus capacity, writing, saved feedback and error recovery. Save stays disabled until the recorder reports replay readiness and while saving. The record button disables during capture transitions. A hotkey conflict removes F8 from the widget's button label and supplies button/tray recovery.

Showing the widget requests no activation (`ShowActivated=false` and `WS_EX_NOACTIVATE`), with a tool-window style and no taskbar entry. Capture exclusion is requested with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`; the returned result is retained, so exclusion remains a Windows/runtime capability rather than a universal promise. The explicit Open library action opens the main app.

Collapse changes height over (150ms) with cubic ease-out when Windows client-area animation is enabled; otherwise it changes immediately. Expanded content is hidden/revealed as part of the toggle rather than animated item by item. The collapsed header retains state and elapsed time. Closing hides the widget; the main Widget action and tray can restore it.

### Real readings, status and copy recovery

The shared native sampler ticks at (1Hz). It samples while the main window is visible, the expanded widget requests vitals, or capture is active; hidden/stopped/collapsed-only use skips sampling. Widget values update only while it is visible and expanded. CPU/GPU missing readings use n/a in the widget and unavailable in the main line; initial widget values use an em dash. RAM shows measured used/total memory. The GPU number is the busiest Windows engine, not recording overhead or game health. App/encoder process cost is separate.

Game FPS and temperature integrations remain pending. Profile FPS describes the encoder setting, not measured game presentation. Stored editor history reports its existing peak summary rather than recalculating over a trim interval, with the existing caveat that usage alone does not establish the cause of a stutter. Missing history stays explicit. No live-game or low-end performance claim follows from the readings or review media.

Main/editor status is transparent, borderless, read-only selectable text. Clipboard failure focuses/selects the URL without uploading again; the web uses a labeled read-only fallback field. Browser status regions are polite; upload progress is an actual slim progress control, not a decorative meter. Conflicting actions disable while work is in progress.

Keep “Local preview” and the PC/service-running notices on local URLs. Configured public-URL support does not mean a deployment exists. Copy states success, cancellation and actionable errors in short sentence-case language. The main tagline is “Keep the moment.”; its empty-state heading is “Your clips will appear here”, followed by capture/F8 guidance.

No shipping raster artwork is required or identified. The tray uses Windows `SystemIcons.Application`; decoded video frames/posters are functional media. Synthetic color-bar review footage is test content, not palette or branding guidance.

## Do's and Don'ts

### Do:

- **Do** use the implemented charcoal layers, pale violet actions and clear control strokes across native and web surfaces.
- **Do** keep recording state, elapsed time, real readings and capture/save actions grouped compactly in the widget.
- **Do** preserve opaque widget text/controls, supported background opacity presets and visible keyboard focus.
- **Do** show actual buffer readiness, missing telemetry and selectable clipboard recovery in plain language.
- **Do** preserve native window behavior and adapt reference ideas through local WPF, CSS and vector code.

### Don't:

- **Don't** copy library source, introduce paid component code or embed a browser to reproduce these reference patterns.
- **Don't** restore 65% widget opacity, fade the entire window or claim that alpha transparency includes background blur.
- **Don't** add a social feed, chat, decorative metric-card dashboard or promotional hero to this clip utility.
- **Don't** substitute synthetic metrics or treat profile FPS, busiest-engine GPU use or test footage as gameplay-performance evidence.
- **Don't** imply that game FPS/temperatures, low-end compatibility or public hosting are established by this local prototype.
