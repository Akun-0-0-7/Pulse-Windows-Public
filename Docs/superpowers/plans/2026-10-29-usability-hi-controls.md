# Pulse Usability and Hi Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the five approved usability, reliability, cost visibility and performance improvements in 0.1.9.

**Architecture:** Extend existing partial desktop classes with focused helpers. Keep read-only preflight separate from execution and reserve daily attempts immediately before dispatch. Retain quota scheduling and account isolation.

**Tech Stack:** C#, .NET 10, WPF, existing synthetic CLI and native test harnesses.

**Spec:** `Docs/superpowers/specs/2026-10-29-usability-hi-controls-design.md`

## Global Constraints

- No real hi, secret export, normal Codex configuration changes, or new dependencies.
- Default daily limit 0 preserves existing behavior; recommendation 6. Shared manual/automatic cap by local calendar day.
- Six locales must retain matching key sets. Source/docs use no custom text colors.
- Reuse current checkout; independent agents edit nonoverlapping files, all builds serialized.

## Review Focus

- Preflight cannot start a turn or leak credentials: synthetic CLI records only version/config/model reads.
- Settings change or late async result cannot label an old check as current: capture identity/model and discard stale completion.
- Midnight, restarts and unknown outcome cannot reset or evade the daily cap: persistent reservation and local day tests.
- Ring labels must match actual selected windows, including missing/nonfinite/exhausted values: native drawing/label checks.
- Startup repair must not modify arbitrary commands or other apps: exact Pulse identity and path tests.

### Task 1: Read-only hi diagnostics

Files: new Core preflight helper/tests; new desktop diagnostics partial; SettingsWindowStarterModel.cs. Add current choice, last-result distinction and asynchronous check without sending. Test no exec, failures, cancellation and stale completion. Root integrates shared app status and locale files.

- [x] Fail targeted regression, implement, run targeted checks, review.

### Task 2: Quota clarity

Files: RailWindow.cs, RailCard.cs, SettingsPreferences.cs appearance section, dedicated native tests. Show actual outer/inner window and mode, both remaining/used detail labels, one-button remaining mode. Preserve colors and primary selection.

- [x] Fail targeted regression, implement, run native checks, review.

### Task 3: Startup consistency

Files: new startup inspection/repair helpers and settings partial, dedicated tests. Show running version/path/channel and read-only entry diagnosis; explicit repair scoped to Pulse. Root adds About invocation and startup snapshot integration.

- [x] Fail targeted regression, implement, run targeted checks, review.

### Task 4: Attempt history and daily cap

Files: new Core ledger/tests; AgentActivity.cs dispatch callback; AppWindowStarter.cs and new partial; dedicated settings partial/tests. Reserve and persist before exec; maintain bounded history and token unknowns. Root alone edits shared files.

- [x] Fail boundary regressions, implement dispatch gate and UI, test storage errors, restart and midnight.

### Task 5: Measurement, integration and release

Files: ignored measurement helper, Docs verification, six locales, version/package metadata. Measure idle CPU and synthetic motion without account reads; optimize only supported bottlenecks. Full core and native regression, source/privacy/package/startup/update checks. Merge exact tested tree, build 0.1.9 from merged source, publish assets then feeds. Preserve local settings when installing.

- [x] Integrate translations, measure, full core and native tests, review integration boundaries.
- [ ] Publish merged source and packages, then verify public downloads. Publication evidence is recorded in the release and commit history.
