# Update Publishing Repair

**Goal:** Make About and Check Updates use the installed version, support stable/preview releases and publish usable manifest addresses.

**Evidence:** About and comparison are hardcoded to 0.1.5; System.Version rejects preview labels; standard package script does not pass Version to dotnet; the general API HTTP client rejects GitHub's required 302 download redirect. The current public stable manifest correctly remains 0.1.5.

**Design:** Keep the legacy numeric UpdateManifest.Version API and add an exact version label. Compare normalized numeric/semantic release versions, including preview ordering and metadata-insensitive equality. Read actual assembly informational version for About and checks. Introduce an updater-only HTTP client with bounded redirects, existing proxy settings and operation-specific deadlines; do not alter account API clients. Stable remains updates.json; publish updates-preview.json only after its ZIP is uploaded and verified. About offers stable/preview source buttons, with stable as the blank/missing default.

**Safety:** Never execute a real installation or quit the user's Pulse. Validate downloading, hashes and isolated staging using a temporary directory; existing installer rollback tests use only synthetic executables and temporary targets. Private settings/logs are excluded. Old preview binaries require one manual bootstrap upgrade because their parser and download client are defective.

- [x] Add red/green semantic version and manifest compatibility checks.
- [x] Add updater-only redirect/proxy handling; repair standard packager and add deterministic manifest generator.
- [x] Add dynamic About version, default/channel source controls and native checks; update six locales and publisher guide.
- [x] Run complete core/native regression, independent review and real public-package staging without installation. Self-contained startup is a post-build gate.
- [ ] Publish preview.6; generate and publish the preview manifest on main; verify old/new version comparison and public URLs.

Documents use default black text. Preserve the stable 0.1.5 manifest and all existing quotas, colors, hi automation and local data.

Validation before packaging: 295 Core checks passed; complete native functional regression passed with explicit pixel-capture skip; six locales retain 519 prior entries and add four keys (523 total). The first full native run hit a timing-sensitive existing spring-settle assertion; the isolated motion run and subsequent full native run passed without changing production motion or test tolerances. Independent review found no release-blocking defect. A real public preview.5 ZIP (77,666,792 bytes) downloaded through GitHub redirects, matched its published SHA-256 and staged 528 files; no installation was scheduled.
