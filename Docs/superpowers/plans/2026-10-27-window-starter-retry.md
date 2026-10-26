# Five-hour Starter Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Recover failed five-hour hi attempts and expose manual retry without duplicate successful sends.

**Architecture:** Core owns persisted scheduling, availability latch and attempt outcomes. Desktop owns account identity, persistence barriers, quota polling and the settings action. Receipt parsing recognizes explicit rejection separately from uncertain completion.

**Tech Stack:** C#/.NET, WPF, existing check executables and simulated tools.

**Spec:** `Docs/superpowers/specs/2026-10-27-window-starter-retry.md`

## Global Constraints

- Preserve opt-in, primary local account isolation and pre-send persistence.
- Automatic checks one minute; retries 1, 2, 5 minutes with five-minute cap.
- Unknown completion never automatically replays; no real hi in tests.
- Documents use default black text; preserve existing application styling.

## Review Focus

- Process stdout can contain structured quota/network rejection while stderr contains unrelated warnings.
- A rounded full quota or persistent zero must not retrigger completed requests.
- Restart and failed writes must preserve uncertainty and avoid duplicate sends.
- Weekly exhaustion and stale cached quota block recovery despite a full five-hour window.
- Manual action, timer, credential changes and shutdown must remain single-flight.

### Task 1: Receipt classification

- [x] Add failing checks for stdout rejection, stderr noise, quota exhaustion, network rejection, auth/model errors and ambiguous output.
- [x] Implement bounded classification in `WindowStarterReceipt.cs`; retain no raw output in stored receipts.
- [x] Run `dotnet run --project tests/Pulse.Tests -c Release -- WindowStarterExecutionTests`.

### Task 2: Durable core coordinator

- [x] Add failing checks for retry delays/reload, full-quota latch, successful deduplication, late recovery, weekly/stale guards and manual uncertain recovery.
- [x] Extend `WindowResetCoordinator.cs`: existing `Claim`, plus `Complete(job, receipt, now)` and `ClaimManual(account, reading, now)`; add persisted pending state.
- [x] Run existing and new core window checks; update only deliberately superseded five-minute expiry expectations.

### Task 3: Desktop orchestration and settings

- [x] Add failing native checks with injected fake send/refresh delegates, covering manual action, single flight, save rollback, polling and unknown status.
- [x] Integrate durable outcome persistence, one-minute quota checking and manual retry in `AppWindowStarter.cs`; add action/status in settings and six locale catalogs.
- [x] Run native RailChecks and core suite with fake credentials only; pixel capture is unavailable in this session (documented in the user guide).

### Task 4: Review and delivery

- [x] Review diff against spec and resolve findings with targeted checks.
- [ ] Commit, publish feature branch/PR and build `0.1.6-preview.3` containing visual changes plus recovery.
- [ ] Verify isolated startup, package contents and checksum; deliver preview without overwriting installed Pulse.

