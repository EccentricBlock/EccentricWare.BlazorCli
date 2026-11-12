# v1.2 specification → v0.2 source traceability

The controlling requirements are in [DESIGN_SPEC.md](DESIGN_SPEC.md).
This map identifies implemented source and test definitions. The last column names the checks that cover each row. `VALIDATION_STATUS.md` holds the results and lists what has not been run.

| Specification | Implementation | Verification / known limitation |
|---|---|---|
| §1 CLI-first executable and optional HTTP | `src/.../content/.../src/.../Program.cs`, `Bootstrap/ApplicationEntryPoint.cs` | `CommandLineParserTests`, `ProcessAndApiTests`, `smoke_publish.py` |
| §2 shared DI without HTTP | `Application/IApplicationStatusService.cs`, `Application/ApplicationStatusService.cs`, `Bootstrap/ServiceRegistration.cs` | `ApplicationStatusServiceTests` and API/CLI output comparison |
| §3.1 strict typed parse and exit codes | `Bootstrap/AppInvocation.cs`, `Bootstrap/CommandLineParser.cs`, `Bootstrap/ApplicationEntryPoint.cs` | Parser boundary/adversarial theory tests; short-lived process smoke |
| §3.2 separate host construction | `Bootstrap/Hosting.cs` (`RunWorkerAsync` / `RunWebAsync`) | Worker tests; process socket ownership and host-rebinding smoke |
| §3.3 no output pollution / secrets | `Cli/StatusCommand.cs`, DTO, `Web/ApiEndpoints.cs` | JSON exact key set; no secret fields; HTTP response headers |
| §4.1 Interactive Server | `Components/App.razor`, `Routes.razor`, `Layout/MainLayout.razor` | Chromium suite against the source build and the relocated publish |
| §4.2 local SDK assets | `wwwroot/`, `Components/App.razor`, `MapStaticAssets()` | MIME/content/URL checks, no external asset requests |
| §4.3 UI counter/snackbar/dialog/menu | `Components/Pages/Dashboard.razor`, `SampleDialog.razor` | Browser interaction, concurrent circuit/rapid-click, responsive tests |
| §4.4 GET-only API | `Web/ApiEndpoints.cs` | API process tests and Playwright request checks |
| §4.5 localhost security | `Bootstrap/Hosting.cs` (loopback Kestrel, error handler, `frame-ancestors 'none'`), `Web/LoopbackGuard.cs` (Host, port, Origin) | `LoopbackGuardTests`, hostile env socket inspection, wrong Host 400, foreign Origin 403 (negotiate and WebSocket), port conflict 3 |
| §5 regular folder publish | no custom MSBuild targets; `scripts/smoke_publish.py` | Separate CWD, read-only POSIX copy; native Windows ACL not tested |
| §6 source template + pack | `src/EccentricWare.BlazorCli.TemplatePack/`, `content/**/.template.config/template.json` | `verify_release.py` packages and installs, checks full source/package inventory |
| §6 names and solution/test references | `content/.../*.slnx`, `content/.../tests/**` | `verify_release.py`: every name in `GENERATED_NAMES` (including `Demo.App`, `Demo-CLI`, `Web`), path with spaces |
| §7 tests and CI | generated `tests/**`, repository `tests/contract`, `tests/browser`, `.github/workflows/ci.yml` | Linux gate in `VALIDATION_STATUS.md`; Windows and GitHub-hosted CI not run |
| §8 risks and gaps | `docs/V02_AUDIT.md`, `docs/SECURITY.md`, `docs/VALIDATION_STATUS.md` | Open blockers tracked explicitly |
| §9 new-agent hand-off | `docs/DESIGN.md`, `DEVELOPMENT.md`, `RELEASE.md`, `TESTING.md`, `TRACEABILITY.md` | Full source archive; `.nupkg` built by `verify_release.py` |

**Acceptance remains contingent** on a successful native Windows full verifier
run and a GitHub-hosted CI run. The Linux run is recorded in `VALIDATION_STATUS.md`. The v1.2 optional self-contained folder command remains
SDK-supported but is not within the mandatory smoke matrix.
