# FunClipper

<!-- impeccable:product-schema 1 -->

## Platform

web

A native Windows desktop recorder and editor accompanies the web library and viewing pages.

## Stack

Implementation choice under the user's instruction to proceed: .NET 10, WPF for the native desktop, ASP.NET Core for the web companion, and plain HTML/CSS/JavaScript. FFmpeg supplies capture/encoding/media processing. This is a local prototype; hosted sharing remains an explicit deployment step.

## Users

PC gamers who want to preserve, quickly trim, and share moments while keeping gameplay responsive, including people with low-end PCs.

## Product Purpose

Save recent gameplay with a hotkey, access a compact editor directly, and copy a playable sharing link after upload. Show useful PC vitals and the recorder's own cost.

## Operating Context

The development PC has a Ryzen 7500F, Radeon RX 6600 XT with 8 GB VRAM, and 32 GB DDR5 RAM. Real low-end hardware is required to establish performance claims. The workspace was empty at the start. Medal was not found in its usual installation paths.

## Capabilities and Constraints

First milestone: opt-in desktop capture, bounded encoded replay storage, hotkey saving, local trim/mute, local web sharing, native system CPU/RAM metrics and application resource usage. Hardware encoding is preferred; audio and GPU readings must report actual availability. Game FPS is separate from recording FPS. Temperatures and actual game presentation telemetry are pending integrations, never simulated.

Starting recording profiles: 1080p60/30-second replay; 720p30/30 seconds; 480p30/15 seconds. They are test profiles, not performance guarantees.

Public hosting, account management, and live Medal-versus-FunClipper gaming benchmarks are not established by a local prototype. Sharing links must state their scope accurately. No feeds, chat, montages, or AI highlight detection are required in this milestone.

## Evidence on Hand

Official Medal/REA/AMD documentation in the conversation, local tool availability, and investigation output under research/. REA evidence is distinct from ordinary shell inspection and design decisions.

## Product Principles

- Preserve gameplay responsiveness and make resource costs measurable.
- Keep capture, editing, and sharing direct.
- Do not fabricate metrics, compatibility, or performance claims.
- Capture only after the user starts it; saving does not take game focus.
- Keep storage and monitoring bounded.

## October 15 update

The user requests a visual improvement drawing on Vengeance UI, Animmaster and Skiper UI, plus a floating semi-transparent corner widget for PC vitals and recording. The recorder must be validated with real replay saves because the first prototype did not clip successfully. Adapt component and interaction patterns natively; preserve the small runtime and avoid embedding a browser in the desktop app. The widget must show true capture/buffer state, keep the game focused, and expose direct capture/save actions.
