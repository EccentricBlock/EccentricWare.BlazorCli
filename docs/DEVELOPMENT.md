# Development and contributor guide

## Prerequisites

- .NET 10 SDK (`dotnet --info`), with NuGet.org access for `MudBlazor`, xUnit and test SDK packages.
- Python 3.10+ for repository verification scripts (standard library only).
- The generated tests use `xunit.v3.mtp-off`: xunit.v3 with the classic VSTest runner, so `dotnet test <path>` and `--filter` work from any directory. The test project must be `OutputType` Exe. See [DEPENDENCIES.md](DEPENDENCIES.md).
- Node.js 22 and Playwright Chromium for browser checks. Where Playwright has no Chromium build for the OS, set `EW_BROWSER_EXECUTABLE` to an installed Chrome (for example `/usr/bin/google-chrome`); CI does not set it.
- Supported baseline verification platforms: Ubuntu x64 and Windows x64 (see CI).

Use `dotnet --list-sdks` to confirm a 10.x SDK. No repository-wide custom build props or package version files are imposed on consumers.

## The source template is executable

The template app is an ordinary C# project under:

`src/EccentricWare.BlazorCli.TemplatePack/content/EccentricWare.BlazorCli.TemplateApp/`

Its `.slnx` loads its application project and generated tests. Work on that normal project **before** adjusting template metadata. Building inside the template directory creates `bin/` and `obj/` there, and both `scripts/verify_release.py` and two contract tests reject them (fail-closed inventory). Delete them (`rm -rf src/*/bin src/*/obj tests/*/bin tests/*/obj`) before running either, or build a copy elsewhere. For example:

```bash
cd src/EccentricWare.BlazorCli.TemplatePack/content/EccentricWare.BlazorCli.TemplateApp
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/EccentricWare.BlazorCli.TemplateApp -- status --json
dotnet run --project src/EccentricWare.BlazorCli.TemplateApp -- --web
```

Interactive Server requires a working SignalR connection; checking only the returned HTML is insufficient. Run Chromium tests on generated output from the **packed** template, too.

## Repository layout

```text
README.md, LICENSE, CHANGELOG.md
.github/workflows/ci.yml
src/EccentricWare.BlazorCli.TemplatePack/
  EccentricWare.BlazorCli.TemplatePack.csproj
  content/EccentricWare.BlazorCli.TemplateApp/
    .template.config/template.json
    EccentricWare.BlazorCli.TemplateApp.slnx
    README.md, docs/USAGE.md, LICENSE
    src/EccentricWare.BlazorCli.TemplateApp/
    tests/EccentricWare.BlazorCli.TemplateApp.Tests/
scripts/verify_release.py, scripts/smoke_publish.py
tests/contract/, tests/browser/
docs/
```

The pack project is **not** referenced by generated applications. Its `PackageType=Template` and the packed files are under `content/` in the `.nupkg`. Hidden `.template.config/template.json` must be present in the package.

## Add a command or service

Extend `AppInvocation.cs` (including its `Mode` switch, which has no default arm so a new command must choose a mode) and the **pure** parser first, add parser tests for allowed/invalid combinations and update the usage grammar. Put shared business operations in `Application/` behind an injectable interface registered in `ServiceRegistration.cs`. For one-shot commands, resolve a scope from the unstarted DI host. Do not register workers in the CLI one-shot path. For long-running operations, register `IHostedService` in `run` and/or `--web` deliberately. When adding web endpoints, expose DTO allowlists, avoid privileged actions and add method/Host header/negative tests. Preserve `--web` as the only listener-enabling input. Register new background workers in `ServiceRegistration.AddApplicationWorkers`, which both `run` and `--web` call.

For a new Blazor component, prefer MudBlazor components inside the existing Interactive Server tree; do not duplicate providers or switch root `App` to interactive. Ensure no runtime CDN calls. Review whether a sample operation might require authentication before exposing it.

## Template symbols and renaming

The derived `safeName` symbol in `template.json` replaces the token `EccentricWare.BlazorCli.TemplateApp` in file and directory names and in file content, always with the `safeProjectName` form of `-n`: `safe_namespace`, then a `_` prefix for C# keywords, then a `_` prefix for names equal to a framework assembly or an app dependency (`Demo-CLI` becomes `Demo_CLI`, `1Demo` becomes `_1Demo`, `class` becomes `_class`, `System` becomes `_System`). The last list is data in `template.json`: after upgrading the SDK or a package, run `python scripts/reserved_names.py --assets <generated app>/obj/project.assets.json --write`; `verify_release.py` fails when the list misses an installed name. Do not switch back to `sourceName`: it renames files with the raw value and content with the safe value, which broke `Demo-CLI`. `scripts/verify_release.py` generates and builds every name in `GENERATED_NAMES`; add a name there when you find a new collision, for example a final segment equal to a component class (`Demo.App`). Keep `CSHARP_KEYWORDS` in that script identical to the keyword list in `template.json`; the verifier checks it. The exact spelling in template source files is intentional and must never survive in generated app source. When you add a C# type or Razor component, check that a project name equal to its simple name cannot capture a reference (see the `RootComponent` alias in `Hosting.cs`). Avoid building extra command-line parameters into `template.json`: `--web` is a generated app flag, not a `dotnet new` option.

Use `scripts/verify_release.py` instead of manually copying template content: it packs, installs and tests the package. Do not publish to NuGet until all release gates pass.

## Troubleshooting

- Missing MudBlazor styles or scripts: verify `MapStaticAssets()`, `@Assets[...]` and the **entire** publish folder. Check browser network responses for 404 or HTML substituted for CSS/JS.
- Non-interactive buttons / missing provider: check `Routes`/`HeadOutlet` render mode and `MainLayout` providers; browser console errors are actionable.
- `--web` cannot start: check port conflicts with `ss -ltnp`/`Get-NetTCPConnection`, address in use and malformed inherited config; the source should override inherited endpoints.
- `dotnet new` not found: inspect `dotnet new list ew-blazorcli`, ensure package installs and contains `content/<template>/.template.config/template.json`.
- Assets served without a `Content-Type` or the log says "Static Web Assets are not enabled": `Hosting.IsRunningFromBuildOutput` decides Development from `<assembly>.staticwebassets.runtime.json` beside the assembly. Check that the file exists next to the DLL for a build output and is absent from the publish folder.
- CI browser failures: install the pinned Playwright Chromium version, ensure local SignalR/WebSocket is accessible, and check browser console/resource errors.

## Style and contribution contract

Use nullable C#, simple dependency injection and small cohesive services. Prefer SDK conventions over custom packers, source generators or generic framework layers. Every functionality change must update normal, boundary, negative and misuse tests and the relevant documentation. Security changes require hostile-config and Host-header regression tests. Update the generated README when the consumer command contract changes.

## v0.2 contributor safeguards

The NuGet package uses a uniform `content/**/*` recursive glob with explicit
exclusion patterns and an individually listed `.template.config/template.json`.
Do not add new file extensions to template content without extending the
fail-closed `scripts/verify_release.py` source inventory and package tests.
Never include an `.env`, private key, secret, build artefact or dependency folder.
The publish verifier requires a marker file before deleting an existing output
folder; do not bypass it. Set up Playwright **before** invoking the full verifier;
using `--skip-browser` or `--skip-smoke` produces a partial, non-release result.
The generated app is not a dependency of the repository's pack-only project.
