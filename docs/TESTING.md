# Tests, verification and evidence

## Test inventory

| Area | Location | What is checked |
|---|---|---|
| Pure parser | Generated `CommandLineParserTests.cs` | Valid modes, ports 1/65535, JSON, invalid flags/combinations, excessive input, escaped switches |
| Shared service/serialization | Generated `ApplicationStatusServiceTests.cs` | DTO allowlist, mode, version derived from the assembly (never a literal), version formatting, concurrency, null dependencies, JSON purity |
| Request guard | Generated `LoopbackGuardTests.cs` | Host/port and Origin predicates: other names, wrong port, scheme, `null`, joined duplicate headers |
| Exit codes and host | Generated `ApplicationEntryPointTests.cs`, `HostingTests.cs` | Exception to exit-code mapping, no echo of exception text, crashed worker exits 70, clean stop exits 0, single worker registration, invalid port |
| Worker | Generated `SampleWorkerTests.cs` | Cooperative cancellation, immediate cancellation, dependency failure |
| Generated process/API | Generated `ProcessAndApiTests.cs` | Short-lived exit codes, HTTP API 200/404/405, Host 400, foreign Origin 403 (negotiate, API, WebSocket) with same-origin still working, hostile environment, port conflict, `run` stops on SIGTERM with exit 0 (POSIX only), source web assets |
| Static contract | `tests/contract/test_template_contract.py` | Template metadata, pinned versions, README/docs, MIT, critical security paths |
| NuGet contract & generated build | `scripts/verify_release.py` | `dotnet pack`, install `.nupkg`, generate every name in `GENERATED_NAMES` (13 names: dotted, hyphenated, digit-leading, C# keywords, folder-name, framework-namespace, framework-assembly and dependency-ID collisions), check stale tokens and file names, restore/build/xUnit |
| Relocated publication | `scripts/smoke_publish.py` | Copy entire folder; CLI/worker, OS-owned socket inventory, API, local resources, hostile web host config, shutdown |
| Chromium browser | `tests/browser/` | Counter, dialog, snackbar, menu, mobile 375px/zoom, keyboard, console/network errors, off-loopback requests |

## Full release verification (release gate)

Install the .NET 10 SDK, Python 3.12 and Node.js 22. On Ubuntu, from the repository root:

```bash
cd tests/browser
npm install --no-audit --no-fund
npx playwright install --with-deps chromium
cd ../..
python3 scripts/verify_release.py --output-root artifacts/verification
```

On Windows, use `npx playwright install chromium` (without `--with-deps`) and
`python scripts/verify_release.py --output-root artifacts/verification` in PowerShell.
Where Playwright has no Chromium build for the OS, export `EW_BROWSER_EXECUTABLE=/path/to/chrome` before the verifier. CI does not use it.

The verifier **automatically runs** Playwright against both the source-built
project and a copied, published application. Do not manually run an unrelated
browser suite and mistake it for verification of that copied build.

The verifier creates `artifacts/verification/commands.log`, `result.json`,
`package.sha256`, the generated app trees, relocated publication and the SDK-produced
`EccentricWare.BlazorCli.0.2.0.nupkg`. Before deleting an existing output folder,
it requires its own `.eccentricware-verifier-owned` marker and refuses to delete
unmanaged folders. It does not sign/publish anything, and uses an isolated
`DOTNET_CLI_HOME` for local template installation.

For fast local diagnostics, `--skip-browser` and `--skip-smoke` exist, but
`result.json` then declares `INCOMPLETE`; these runs are **not** release gates.
Both browser passes enforce local-only resources, test actual interactivity,
check static asset MIME types, and reject a deliberately hostile inherited
web-host configuration. The publish smoke tests run the entire copied folder
from an unrelated working directory; on POSIX its published files/directories
are read-only during the checks. On Windows, the copied-directory read-only
ACL case is not implemented: see `VALIDATION_STATUS.md`.

## Test coverage and CI matrix

| Layer | Linux required | Windows required | Notes |
|---|---|---|---|
| Python source/package safety | Yes | Yes | Fail-closed source inventory, dangerous file types, guarded cleanup |
| Install real `.nupkg` and generate every name | Yes | Yes | Includes dotted, hyphenated and digit-leading names and spaces in output paths |
| `dotnet restore/build/test` | Yes | Yes | Includes parser, status, worker, process and API xUnit tests |
| Copied published-folder smoke | Yes | Yes | Process/socket ownership and CLI/HTTP contracts |
| Source and copied published Chromium | Yes | Yes | UI interactivity, off-loopback requests, static asset integrity |
| Native ARM64/self-contained publishing | Optional | Optional | Not represented as verified |

`.github/workflows/ci.yml` sets up Linux and Windows Chromium before invoking
the full release verifier. CI uploads command logs, package checksum, test
artefacts and the `.nupkg` for inspection. Playwright `@playwright/test` is
pinned to 1.65.0 in `package.json`, and `package-lock.json` pins the transitive
dependencies; CI installs with `npm ci`.

## Coverage and remaining verification

The test source includes normal, boundary and malformed inputs, shared service data paths, concurrent calls, port conflicts, hostile Host/URL settings, local resource checks and worker cancellation. The Playwright suite checks actual browser interactions rather than relying solely on status 200. Unexecuted validation is **not equivalent to passing tests**.

Results of the last full run, and what has not been run, are in [VALIDATION_STATUS.md](VALIDATION_STATUS.md). Known gaps in the automated checks:

- `RunModeStartsWithoutHttpAndStopsCleanlyOnTerminate` reports as skipped on Windows (`Assert.SkipWhen`), and the shutdown part of the `--web` process test is not run there. Console Ctrl+C is not automated on any platform; the tests send SIGTERM.
- The sanitised HTTP 500 response has no test, because the generated app has no endpoint that throws.
- The Chromium tests check that nothing overflows horizontally at 375px and that a CSS zoom still works; they do not use browser zoom or test snackbar dismissal or a dropped circuit.
- The contract tests in `tests/contract` are source greps for security-relevant strings. They guard against deletion; the xUnit, smoke and browser runs prove behaviour.
- `dotnet new uninstall` and overwrite behaviour are not tested.

Record the SDK, OS, `dotnet --info`, full commands, failures, output paths and browser console diagnostics when executing the release gates. Correct errors in the source and repeat all tests; don't merely waive failing tests.
