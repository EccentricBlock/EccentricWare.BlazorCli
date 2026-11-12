# Dependency review

**Reviewed:** 12 November 2026, against api.nuget.org and registry.npmjs.org as they stood that day. Repeat the commands below before every release; advisory data changes daily.

## Result

| Area | Finding | Action |
|---|---|---|
| NuGet vulnerabilities | None in either generated project, direct or transitive (`dotnet list package --vulnerable --include-transitive`) | None needed |
| NuGet deprecation | `xunit` 2.9.3 was flagged *Legacy*, alternative `xunit.v3` | Replaced by `xunit.v3.mtp-off` 4.0.3 |
| NuGet currency | `MudBlazor` 9.12.0 is the latest stable. `Microsoft.NET.Test.Sdk` 17.14.1 and `xunit.runner.visualstudio` 3.1.5 were behind | Upgraded to 18.12.0 and 4.0.1 |
| npm vulnerabilities | None (`npm audit`, 3 packages) | None needed |
| npm currency | `@playwright/test` 1.56.0, latest 1.65.0 | Upgraded and pinned; `package-lock.json` pins `playwright` and `playwright-core` with integrity hashes |
| Licences | 40 NuGet packages in the resolved closure: 31 MIT, 9 Apache-2.0. npm: 3 packages, all Apache-2.0 | None needed |
| .NET runtime | 10.0.15 (released 2026-11-10) is the latest patch. The last three patches (10.0.13, 10.0.14, 10.0.15) are all security releases | See below |

## Versions now pinned

| Package | Version | Where |
|---|---|---|
| MudBlazor | 9.12.0 | generated app (consumer-owned) |
| xunit.v3.mtp-off | 4.0.3 | generated tests |
| Microsoft.NET.Test.Sdk | 18.12.0 | generated tests |
| xunit.runner.visualstudio | 4.0.1 | generated tests |
| @playwright/test | 1.65.0 | `tests/browser` (verification only) |

The generated test project is an executable (`OutputType` Exe), as every xunit.v3 project must be. It uses `xunit.v3.mtp-off`, the xunit.v3 variant that runs on the classic VSTest runner. The default `xunit.v3` package runs on Microsoft Testing Platform, which on the .NET 10 SDK needs a `global.json` read from the working directory and rejects `dotnet test <solution path>`; `mtp-off` avoids both, so `dotnet test <path>` and `--filter` work from any directory. Moving to Microsoft Testing Platform later means switching the package to `xunit.v3`, removing the VSTest packages and adding that `global.json`.

## The shared framework is not covered by NuGet audit

`net10.0` web apps run on the ASP.NET Core and .NET runtime that is installed on the machine, and `dotnet list package --vulnerable` does not see it. Every recent 10.0.x patch fixed CVEs.

- Framework-dependent deployment (the default): install the latest 10.0.x ASP.NET Core runtime on the target and keep it patched.
- Self-contained deployment: republish and redeploy the whole folder after each patch.
- Build machines: use an SDK that ships the latest runtime patch.

## Not reviewed

- GitHub Actions in `.github/workflows/ci.yml` (`actions/checkout@v4`, `setup-dotnet@v4`, `setup-python@v5`, `setup-node@v4`, `upload-artifact@v4`) are pinned by tag, not by commit SHA. Pin by SHA before a public release.
- The browser binary (Chrome or Chromium) and operating-system packages.
- No SBOM is generated and no scanner runs in CI.

## Commands to repeat

```bash
cd src/EccentricWare.BlazorCli.TemplatePack/content/EccentricWare.BlazorCli.TemplateApp
dotnet restore
dotnet list package --vulnerable --include-transitive
dotnet list package --deprecated
dotnet list package --outdated
cd ../../../../tests/browser
npm audit
npm outdated
# runtime patch level: https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
```

Building in the template directory leaves `bin/` and `obj/`; delete them before running the release verifier (see [DEVELOPMENT.md](DEVELOPMENT.md)).
