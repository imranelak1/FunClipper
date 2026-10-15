# Medal investigation — first evidence pass

## MCP observation after reconnecting

The user restarted Codex after approving the scoped REA registration. A successful `binary_session` call confirmed the live `rea-agents` 6.4.0 server. No native analysis engine or native target was bound.

An actual MCP `capture_browser_scenario` call then launched headless Chrome with an owned temporary profile, loaded the public Medal homepage, navigated to `https://medal.tv/clips`, and waited one second. All declared steps completed; the observed final URL was `https://medal.tv/`. This is evidence of that anonymous navigation outcome, not a test of an authenticated editor, a particular shared clip, or private-library access. The provider reports that its owned browser process was terminated during cleanup.

Complete runtime Evidence is saved in [rea-mcp-public-browser.json](medal/rea-mcp-public-browser.json), ID `ev_03afc41f59879990b1c95dedcb873716fba08bc7ccfe546a027c664c3c4345dd`. Capture selected URL, DOM, accessibility, network metadata, and page errors; request/response bodies and header values were not selected. Some requests were unfinished at capture end, so later behavior remains unknown. This extends the earlier static evidence below without establishing Medal's recorder implementation or overhead.

Captured on 14 October 2026. This pass used **REA 6.4.0** against actual publicly shipped Medal JavaScript and an official Windows installer. It establishes a small part of the web/desktop boundary. **It does not establish Medal's recorder implementation or measure its performance.**

## What was examined

- Public HTML from [medal.tv](https://medal.tv/) and [medal.tv/download](https://medal.tv/download), saved in `medal/`.
- Five scripts referenced by the homepage: 321,783 bytes in total. This is a sample of a page's dependencies, not the complete web app.
- The official download link [install.medal.tv](https://install.medal.tv/) resolved to [Medal-production-2641.538.1-Web-Setup.exe](https://cdn.medal.tv/production/electron/Medal-production-2641.538.1-Web-Setup.exe). Its 2,520,992 bytes were downloaded for static PE resource inspection. **The installer was never executed.**
- The shipped REA reverse-engineering skill and its JavaScript application reference, including the distinction between observed syntax, inferred relationships, and runtime evidence. Repository reference: [JavaScript application workflow](https://github.com/morluto/rea/blob/main/.agents/skills/reverse-engineer-anything/references/javascript-applications.md).

No installed desktop ASAR, recorder DLL, game capture, authenticated account, or upload was examined. No Medal desktop application or disassembly engine was installed by this investigation.

## Findings and their limits

| Question | Actual evidence | Conclusion supported by this pass |
|---|---|---|
| How does the homepage lead to a clip? | `3r6d6euuwe5mu.js`, line 1, zero-based UTF-16 column 29,975: `onClipClick` passes `gameSlug`, `clipId`, `locale`, and query parameters to `PathBuilder.CLIPS`, then calls `navigateToLegacyApp`. | There is a static handoff from the homepage component to a separately named legacy app navigation helper. Its destination, account policy, and execution remain unverified. |
| How does the homepage obtain featured clips? | The same chunk calls `useGetContentsBulkQuery` with comma-joined content IDs. Its `skip` condition depends on an empty list or absence of a `useCurrentUser()?.userId` value. | The component is built around IDs and an API query abstraction. This does not prove that shared viewing requires login, identify the API endpoint, or describe all clip retrieval. |
| Is this the full clip editor/viewer? | The homepage clip component supplies `withFullPlayer: false`, `muted: true`, hides several metadata controls, and navigates on click. | The homepage has a compact clip presentation. It is unsuitable evidence for the main editor or upload workflow. |
| How does the site lead to desktop installation? | Homepage/download HTML links to `https://install.medal.tv/`; the download response resolves to a production/electron URL. `useSendEmailDownloadLinkMutation` appears in the homepage chunk. | There are desktop download and email-link entry points. The URL naming suggests Electron distribution, but does not establish the installed process architecture or its resource use. No email request was sent. |
| What is the official installer? | SHA-256 `1804b4a8769f3ca9107abc21bdcb1b879574d68897709d22c9968d3a659a45c0`. Windows PE version metadata: product/description `Medal`, file/product version `2641.538.1`. REA reports PE32+, machine value 34404, 12 resources. | This identifies the downloaded Windows installer. It does not provide the installed recorder's code or configuration. |
| How does Medal capture, buffer, trim, or upload? | No desktop capture artifact or relevant runtime evidence was available. | Unknown. Encoder choice, hardware paths, replay memory, disk writes, hotkey handling, trim strategy, upload authentication, and resource overhead cannot be inferred from these files. |

The independently verified snippets, hashes, and source locations are in [verified-static-excerpts.json](medal/verified-static-excerpts.json). These literal excerpts are supplemental static verification; they are not a browser execution trace.

## Actual REA results

The complete application Evidence is in [rea-web-application-full.json](medal/rea-web-application-full.json), approximately 49 MB. Read the [summary](medal/rea-web-summary.json) first.

| Operation | Evidence ID | Saved result |
|---|---|---|
| `analyze_javascript_application` | `ev_42d57ca290cae861a3a0699631f947bbc7253a06f76d189a9724c6f1b902e5be` | `medal/rea-web-application-full.json` |
| Summary projection | `ev_191953cb209f638dc43be36d99f84933b0ee0050ee50fc1554c9ae26a38932c3` | `medal/rea-web-summary.json` |
| Module identity page | `ev_3f7d8da4015723ab67e20c306c57e3f32ae1529afaa727b4fc0d478ce1f23a92` | `medal/rea-web-modules-page.json` |
| Homepage module item | `ev_c2b2e4f393ac4ad49b800ad59d0a0b699f6c2b36a62000817526b9e48485f45d` | `medal/rea-home-module-item.json` |
| `inspect_pe_resources` | `ev_4f81ecf38d3f44db8d95c4ae573308bef03bb44e4850b89622bffc372f0b12ba` | `medal/rea-installer-pe-resources.json` |

REA parsed all five scripts without a parse failure and retained five file-level module identities plus five asset identities. Its `statistics.modules` is **0**; do not read this as successful recovery of Turbopack's internal module structure. Application and semantic graph coverage are **partial**. One vendor chunk, `2fb33wq1q_q1t.js`, exceeded the parent semantic result budget; its static/module facts were retained. The summary reports one application graph unknown and 10,161 semantic graph unknowns.

Some generic syntax classifications can be misleading on website bundles: for example, an HTML video `preload` property can produce an Electron-related candidate. No Electron runtime, preload bridge, native capture call, or recorder process was observed. A count of zero in this sample does not prove absence from Medal.

The PE resource inventory reports complete coverage **within that resource-table operation**: 28 directory entries examined, 12 resources. Its opaque payloads and icon relationships do not constitute application decompilation. Windows version metadata was read independently from the same hashed file.

## Artifact identities

The sampled tree's REA artifact digest is `fc1df260fe4bc284899906d4dbc779b041f61156a947c7ab90eed85ce6530f40`. Per-download URLs, retrieval timestamps, sizes, and SHA-256 values are retained in [download-manifest.json](medal/download-manifest.json). Installer identity is in [installer-manifest.json](medal/installer-manifest.json); redirect/HEAD metadata is in [installer-head.json](medal/installer-head.json).

| Script | SHA-256 |
|---|---|
| `2fb33wq1q_q1t.js` | `74082f513f6a5563ec41c7b94d2e7a980c175762584d5c32af750b4e3d920606` |
| `3r6d6euuwe5mu.js` | `97bfef4f4cd86da4b22d6be716520cea8f5d38397abf67ae8879faac4a2985f2` |
| `2064t9gzbwqjx.js` | `8152e6103fad5ceffaa95ce23a25f94570d4ae550d0c851c3300130d3b595d19` |
| `0cz1d0mv5g_q7.js` | `0973c1d64c88adc8e3c950410cb58b288f72118d5965b78049438deb8f2f9683` |
| `3pmn-rdtwmba7.js` | `f112f633ef99069a121f13fb7ac1c5da4dae1e186c75b1dbee56363f5604d499` |

## Commands and reproduction

The initial CLI acquisition used `npx.cmd -y rea-agents@latest`; its help resolved to **6.4.0**. Subsequent projections and PE inspection pinned 6.4.0. The actual static application command was:

```powershell
npx.cmd -y rea-agents@latest analyze-javascript-application `
  (Join-Path $PWD 'research\medal\public-web-bundles') `
  --artifact-format directory --max-heap-mb 512 --analysis-timeout-ms 60000 `
  --format json --full-output |
  Set-Content -LiteralPath (Join-Path $PWD 'research\medal\rea-web-application-full.json') -Encoding UTF8
```

Each projection used `npx.cmd -y rea-agents@6.4.0 inspect-analysis-view <input-json-path> --format json --full-output`. The input contained the **complete inline parent Evidence** from the saved envelope's `data`, with `view` set to a summary, a 16-item module page at offset zero, or the returned homepage module node ID `jag_node_4ddec36918d4447674ca77c822a1e898d9aeb0f010c6a8fd1eabb8e681d536ce`. No retained ID was incorrectly reused across CLI processes.

```powershell
npx.cmd -y rea-agents@6.4.0 inspect-pe-resources `
  (Join-Path $PWD 'research\medal\Medal-Web-Setup.exe') `
  --format json --full-output |
  Set-Content -LiteralPath (Join-Path $PWD 'research\medal\rea-installer-pe-resources.json') -Encoding UTF8
```

[Investigate-Medal.ps1](../scripts/Investigate-Medal.ps1) reproduces bounded homepage sampling and saves complete REA Evidence before projecting a compact summary. It pins REA 6.4.0 and creates an absent output directory for each run. Public scripts change, so a new run receives new hashes and may cover different features. It does not install Medal, configure MCP, or execute captured code.

Once an installed Medal ASAR or extracted application tree is available, analyze that exact supplied path:

```powershell
.\scripts\Investigate-Medal.ps1 -ArtifactPath 'C:\path\to\Medal\resources\app.asar'
```

Script verification: PowerShell parser passed; local-artifact mode passed end to end against a small inert validation specimen. That validation Evidence is under `medal/script-validation-run/` and is explicitly **not Medal evidence**.

## Consequences for FunClipper

These files support keeping the proposed capture service, quick local edit surface, and shared web player as separate concerns. They do not prove that Medal lacks a particular feature, needs more resources, or has more sharing steps. FunClipper's proposed advantage remains a target to validate: measured game overhead, fewer actions from save to copied link, and fast local trimming.

The next desktop evidence pass should inventory an installed distribution, trace the hotkey/save and trim entry points, and identify recorder/native boundaries. The performance comparison then needs an actual same-settings game run with and without each recorder; record game frame times, dropped capture frames, process CPU/RAM, GPU engine load, and disk writes. Browser sharing/editing behavior needs a separate bounded walkthrough of the real clip UI. None of those measurements has been performed in this pass.
