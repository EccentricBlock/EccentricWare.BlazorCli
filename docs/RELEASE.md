# Packaging, publishing and release procedure

**Package version:** `0.2.0` (referred to as **v0.2**); template identity `EccentricWare.BlazorCli.CSharp`, short name `ew-blazorcli`. The user-selected licence is **MIT**. There is no requirement for a single-file executable or runtime extraction.

## Mandatory prerelease gates

1. Ensure no secrets, local cache files, `bin`/`obj` or unintended files are part of the content tree. Verify both MIT licence files are present.
2. Confirm .NET 10 SDK on Linux x64 and Windows x64 and check NuGet restore works.
3. Run `python scripts/verify_release.py --output-root artifacts/verification` on each OS; retain command logs and failing outputs.
4. The release verifier runs Chromium against both source and copied published output on Linux and Windows. Inspect browser failures, the socket inventory, network captures, keyboard and responsive checks.
5. Inspect `artifacts/verification/packages/EccentricWare.BlazorCli.0.2.0.nupkg`: it must include `content/EccentricWare.BlazorCli.TemplateApp/.template.config/template.json` and the generated project/tests/docs, but **not** builds, caches or secrets.
6. Verify `dotnet new list ew-blazorcli` and every generated name in `GENERATED_NAMES` (see the comment above it in `verify_release.py` for what each covers): matching solution, project and test names, `--version` equal to the generated `<Version>`, no remaining template placeholder, and normal builds/tests.
7. Verify copied publish from an unrelated working directory, IPv4 loopback-only socket, no worker/CLI listeners, Host filtering, API 404/405, all browser CSS/JS and graceful cancellation on Linux.
8. Review pinned dependencies, licences, known advisories and any environment-specific security issues. Decide whether to publish publicly only after this review.
9. Update changelog, ensure the package version (pack project, docs, verifier) agrees, and tag the release **v0.2.0** after all gates are green.

## Manual build and local install

```bash
dotnet pack src/EccentricWare.BlazorCli.TemplatePack/EccentricWare.BlazorCli.TemplatePack.csproj \
  -c Release -o artifacts/packages
dotnet new install ./artifacts/packages/EccentricWare.BlazorCli.0.2.0.nupkg
dotnet new list ew-blazorcli
dotnet new ew-blazorcli -n DemoApp -o ./artifacts/sample/DemoApp
dotnet test ./artifacts/sample/DemoApp/DemoApp.slnx -c Release
```

If the CLI template is already installed, uninstall the existing package deliberately before updating the installed version (use `dotnet new uninstall` to identify the appropriate source). Do not assume a stale install represents the package under test. Use the verifier's isolated CLI home to avoid such interference.

## Consumer publishing

Run from the generated app project:

```bash
dotnet publish -c Release --self-contained false -o ./publish
```

Deploy **all** of `publish/` together. The target needs the .NET 10 ASP.NET Core runtime. Use `dotnet /path/to/publish/DemoApp.dll status --json`, `run` or `--web`. A host with no .NET runtime may use a **self-contained multi-file** folder:

```bash
dotnet publish -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -o publish/linux-x64
```

Self-contained folder publishing is a standard SDK capability; it has **not** been separately verified in the initial required test matrix. Never claim a single-file deployment or copy only the apphost/DLL.

## Release artefacts

The main deliverable is the versioned repository source archive. After CI passes, release the **actual SDK-produced** `.nupkg`, evidence logs, checksums and source revision/tag. A `.nupkg` built by `scripts/verify_release.py` on Linux x64 is under `artifacts/verification/packages/`. It has not been verified on Windows or on GitHub runners, and no dependency advisory review has been recorded, so it is not a release candidate yet.

## CI/release policy

The included workflow is **verification only**. It does not publish to NuGet, deploy apps, or have NuGet push credentials. Release and signing secrets must not be added to the generated template content. Record security/advisory review and the dependency licences in the release issue or checklist. A public release requires a maintainer-approved vulnerability reporting channel.
