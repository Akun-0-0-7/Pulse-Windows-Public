# Five-hour starter recovery

User-approved scope: automatically recover explicit network/quota failures, detect freshly reported remaining 100% (raw used 0%), provide a manual retry, and refresh quota after sending.

Keep opt-in, local primary-account identity checks and allowed automatic hours. Automatic quota checks run at most once a minute while eligible accounts exist, independently of idle UI refresh. Only fresh, non-estimated, error-free matching quota can confirm recovery; exhausted five-hour or weekly limits block sends. Offline/stale readings never establish recovery.

Persist pending attempts before execution, distinct from confirmed successes. Explicit network/quota rejection retries after 1, 2, then 5 minutes (five-minute cap); no retry storm and only one send at a time. Restart retains pending attempts; in-flight/timeout/incomplete/unknown failures require manual retry because receipt may be uncertain. Auth/path failures pause until manually retried after repair. Save failures prevent sending. Old one-shot history remains deduplicated, with manual recovery for the last failed attempt.

A fresh full-quota episode can initiate one hi. Do not interpret rounded percentages as full quota or send repeatedly while raw usage remains zero. Successful attempts deduplicate scheduled and availability triggers. A new real reset may start a new cycle. Pending recovery remains available beyond the former five-minute grace window.

Manual retry refreshes quota first, keeps account ownership and single-flight guards, and can run outside automatic hours. A successful current cycle cannot be resent via this retry button; unknown prior results can be explicitly retried. No real account messages in tests.

Alternatives considered: scheduled-only misses recovery; testing visible 100% repeatedly duplicates requests. Chosen approach combines durable retries, fresh availability detection and manual fallback.
