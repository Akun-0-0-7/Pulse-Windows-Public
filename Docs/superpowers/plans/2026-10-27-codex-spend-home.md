# Codex Token Scan Home Fix

**Goal:** Restore local Token records when Codex logs use CODEX_HOME instead of the default .codex folder, and make the actual scan roots visible.

**Evidence:** The default home contains older logs while current logs use an alternate CODEX_HOME. SpendSources currently ignores CODEX_HOME. The Token page defaults to seven days, so scanning only an obsolete default folder produces zero visible records. Machine-specific paths, counts and private record data are excluded from published artifacts.

**Design:** Codex scan roots resolve in this order: explicitly saved CodexSpendHome; supplied process CODEX_HOME; default user .codex. Both sessions and archived_sessions use the same root. Do not search arbitrary profile folders or combine different homes implicitly. Existing reader, account authentication and executable paths remain unchanged. Show actual roots and a committed optional folder setting on the Token page so a separately started Pulse can operate even with stale inherited environment. Preserve all source selections and ordinary import directory semantics.

**Checks and steps:**

- [x] Reproduce missing CODEX_HOME support with isolated synthetic JSONL and cached-empty roots.
- [x] Implement resolver/config overrides and scan snapshot invalidation when saved root changes.
- [x] Add native settings checks for displayed roots, valid/invalid/blank directory persistence and no automatic real scan.
- [x] Read real logs only through the existing Core parser, without networking or saved reports; print only aggregate counts/dates/Tokens, never prompts or credentials.
- [ ] Run core/native functional regression, review and publish preview.5; validate startup, package and public downloads.

The installed elevated Pulse instances are not terminated or modified. Private logs, cache and actual machine settings must not enter git, fixtures, PRs or releases. Documents use default black text. This is a log-path fix, not a Codex binary/version migration; no real hi requests.

