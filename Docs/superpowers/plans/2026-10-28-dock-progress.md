# Collapsed Quota Progress Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans to implement the checked tasks.

**Goal:** Show the selected quota as a proportional, colored progress strip while docked and collapsed.

**Architecture:** Core returns one `DockProgressState` containing effective usage, displayed fraction and color. WPF paints its track and fill within the existing rail geometry and keeps the current hover/spring behavior. One appearance switch restores the solid strip.

**Tech Stack:** Existing .NET 10 C#, WPF and current publish/manifest scripts.

**Spec:** `Docs/superpowers/specs/2026-10-28-dock-progress.md`.

## Global Constraints

- Only collapsed, docked strips change; rings and detail cards keep their existing form.
- Preserve current quota selection, existing colors and `ShowRemaining`.
- Never run real hi or install into the user's program directory during validation.
- Documents retain default black text; no private data enters public artifacts.

## Review Focus

- Empty/invalid/exhausted readings must not look like fabricated remaining quota.
- Fixed colors must keep their saved color while length still reflects quota.
- All four edges, zero/full values and spring reversal must clip and anchor correctly.
- The legacy switch must update without reopening the collapsed rail.
- Quota color and length must come from the same selected window.

## Task 1: Core selection and display state

Files: `AppearancePalette.cs`, `Models.cs`, `DockProgressTests.cs`.
Interface: `DockProgressState(double? UsedPercent,double? Fraction,string Color)` and `AppearancePalette.DockState(config,readings)`; preserve `Dock()` as the color-only wrapper.

- [x] Observe failing contract tests for selection, remaining/used, zero/full, exhausted, invalid, fixed and legacy configuration.
- [x] Implement state and `DockShowProgress=true`; run targeted and full Core checks (304 passed).

## Task 2: WPF progress rendering and settings

Files: `RailChrome.cs`, `RailWindow.Presentation.cs`, `SettingsColors.cs`, six locale JSON files and native progress checks.

- [x] Observe a native failing check for the currently solid strip.
- [x] Paint theme track and clipped fill; keep fixed window, spring and fully expanded output.
- [x] Add the live appearance switch and localization (six locales, 526 keys each).
- [x] Verify four-edge geometry, live source changes, missing data and switch behavior, plus existing native regression. Pixel captures remain explicitly skipped; actual DrawingContext commands are checked. The first full regression hit an existing detail-reopen assertion; isolated motion and complete regression reruns passed without changing motion behavior or tolerances.

## Task 3: Deliver the preview

- [ ] Independent review, self-contained packaging, version/hash/strict staging and startup checks.
- [ ] Publish preview.9 assets and source; update preview manifest only after remote digests match. Preserve stable 0.1.5.
