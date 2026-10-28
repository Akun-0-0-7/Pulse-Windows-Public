# Adjustable Colors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Use focused red/green checks for each task.

**Goal:** User-adjustable rail, quota and app colors with immediate persistent updates.

**Architecture:** A core settings/palette helper owns RGB validation, quota mapping, thresholds and aggregate policy. Shared WPF brushes and rail colors consume this policy. A settings partial owns committed picker/hex edits; application saves color changes without resetting placement or animation.

**Tech Stack:** C#/.NET, WPF, existing WinForms ColorDialog, existing core/native checks.

**Spec:** `Docs/superpowers/specs/2026-10-27-appearance-colors.md`

## Global Constraints

- No new dependencies or modifications to hi scheduling, report export colors or installed user files.
- All changes remain compatible with omitted or invalid old color settings.
- Thresholds default to 75 and 90 USED percent; showing remaining does not reverse palette severity.
- Documents use default black text. Application foregrounds are chosen for readable contrast.

## Review Focus

- Disabled/stale account caches must not recolor the collapsed rail.
- Invalid edits keep the previous saved value and remain visibly correctable.
- Full and collapsed rail updates preserve spring progress and detail anchor.
- Application, rail and detail backgrounds can differ; foregrounds must follow the corresponding background.
- Restoring defaults and changing theme refresh all consumers without focus loss or stale detail cache.

### Task 1: Core color policy

- [x] Add red tests for empty/invalid RGB, thresholds, modes, gradients, raw-percent and aggregate filtering.
- [x] Add `AppearanceSettings` to `AppConfig` and `AppearancePalette` in Core; document the exact helper API to integration workers.
- [x] Run focused and complete core checks.

### Task 2: Shared app palette

- [x] Add failing native assertions for shared brush identity and custom/fallback colors.
- [x] Add Ui.ApplyTheme(AppConfig), contrast helpers and accent selection brush, preserving the string overload.
- [x] Test live changes and defaults without pixel-render claims.

### Task 3: Settings and rail consumers

- [x] Add native failing checks for settings picker/hex/defaults, saved config, quota colors and preserving collapsed state.
- [x] Add committed color controls, picker and reset; integrate rail/detail/meter/tray colors and detail cache signature.
- [x] Add six language labels and user guide; run core and native functional suite.

### Task 4: Deliver

- [x] Independent diff review; fix findings with targeted tests.
- [ ] Commit/push feature PR, build preview.4, verify isolated startup/package/checksums.
- [ ] Publish the verified preview and provide the direct download.

