# Changelog

## [0.2.0] — 2026-11-10

- Audited v0.1 against the complete v1.2 specification; see `docs/V02_AUDIT.md`.
- Corrected template-package content globs; explicitly packaged template discovery metadata.
- Added fail-closed source/package file inventories, sensitive-content exclusions, SHA-256 and safe verification-output cleanup.
- Hardened web exception responses, Host/port checks, browser safety headers and API cache control.
- Expanded runtime smoke for read-only POSIX copies, hostile inherited settings, port conflicts, and socket ownership.
- Expanded Chromium tests to run against both source-built and relocated-published applications on Linux and Windows.
- Expanded static tests to cover release safety and CI/browser invocation; documented residual gaps.
- Preserved the .NET 10 / MudBlazor 9.12.0 / CLI-first / normal folder publish architecture.

**SDK verification pass (12 November 2026).** First build with a .NET 10 SDK; fixes, with evidence, are listed in `docs/V02_AUDIT.md`:

- `dotnet pack` now succeeds (license path, package version, extensionless files, package readme).
- Names with a hyphen (`Demo-CLI`), a leading digit and a final segment `App` (`Demo.App`) now generate and build.
- A crashed worker makes `run` and `--web` exit 70 instead of 0.
- Requests with a foreign `Origin` get 403, which blocks cross-site WebSocket circuits and API reads.
- Static assets resolve from any build output path and are served with a Content-Type.
- The generated app has its own version (starts at `1.0.0`), reads it from the assembly, and its tests no longer hard-code it.
- Exit code 4 is reserved for `ExpectedOperationException`; framework `ArgumentException` is 70.
- Reserved C# keywords as project names now generate compilable code (`class` becomes `_class`). Names equal to a framework assembly or an app dependency also get a `_` prefix (`System` becomes `_System`, `MudBlazor` becomes `_MudBlazor`); the list comes from `scripts/reserved_names.py`.
- Dependency review (`docs/DEPENDENCIES.md`): generated tests moved from deprecated `xunit` 2.9.3 to `xunit.v3.mtp-off` 4.0.3 (classic VSTest runner, so `dotnet test` needs no `global.json` and works from any directory); Test SDK 18.12.0, runner 4.0.1; Playwright 1.56.0 to 1.65.0. No vulnerable NuGet or npm packages found; MudBlazor 9.12.0 is the latest stable.
- Added the missing `.github/workflows/ci.yml`; removed a duplicate spec file; fixed a flaky worker test.

**Review pass (12 November 2026).** Details in `docs/V02_AUDIT.md`, items 18 to 24:

- The document response sends `Content-Security-Policy: frame-ancestors 'none'`, matching `X-Frame-Options: DENY`.
- `--help` and usage errors name the generated application instead of `app`.
- `AppInvocation.Mode` is derived from `Command`; the two can no longer disagree.
- The app bar shows the application name.
- Process tests check stdout/stderr separation and the `--version` text.
- `TRACEABILITY.md` no longer reports finished checks as pending.

**Status:** see `docs/VALIDATION_STATUS.md` for what was executed and what was not.

## [0.1.0] — 2026-11-10

- First source implementation of the v1.2 design with separate CLI, worker and web hosts, MudBlazor dashboard, status API, tests and docs.
- Source distribution only; no `.NET` or browser verification in the authoring environment.
