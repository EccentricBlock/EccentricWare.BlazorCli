# EccentricWare.BlazorCli

## Design specification v1.2 — CLI-first .NET 10 application template with optional MudBlazor web UI

| Property | Decision |
|---|---|
| Status | **Proposed specification; implementation and tests not yet executed** |
| Date | 10 November 2026 |
| Supersedes | v1.1 MudBlazor design |
| NuGet template package | `EccentricWare.BlazorCli` |
| Template identity | `EccentricWare.BlazorCli.CSharp` |
| Primary short name | `ew-blazorcli` |
| Generated target framework | `net10.0` (.NET 10 LTS) |
| Generated application | One C# executable project, `Microsoft.NET.Sdk.Web` |
| UI | Blazor Web App, Interactive Server, MudBlazor **9.12.0** |
| Default runtime | CLI commands; **no HTTP listener** |
| Optional runtime | `--web`: Kestrel + Blazor + status API + background worker |
| Publishing | **Normal multi-file folder publishing** (framework-dependent by default) |
| Template distribution | Standard NuGet `Template` package |

> **Primary change in v1.2:** Remove the single-file executable goal entirely from the initial release. No embedded web assets, custom static-asset provider, extraction cache, publish manifest rewriting or single-file release gate. The published *directory* is the deployment unit. The application executable/DLL, dependent assemblies, static assets and generated manifests are deployed together. Self-contained **folder** publishing is an optional, supported SDK invocation, not a custom template feature.

> **Version policy:** Pin MudBlazor 9.12.0 in generated source (verified against NuGet and MudBlazor installation guidance on 10 November 2026). Re-check the latest compatible stable version when implementing/releasing, and update this pin deliberately with build and browser tests; never float package versions.

---

## 1. Product objective and successful outcome

Create a *project template*, not a new application framework. A consumer installs the template using `dotnet new`, generates a normal editable .NET solution, and gets:

1. A CLI-first application: `myapp`, `myapp --help`, `myapp --version`, and `myapp status` are short-lived commands.
2. A background-service mode: `myapp run` runs a cancellable worker **without Kestrel**.
3. An opt-in web mode: `myapp --web` starts the same worker and a loopback-only HTTP server hosting a MudBlazor dashboard and minimal read-only API.
4. One shared application service called by CLI, worker, Blazor components and API adapters; no in-process HTTP for shared business operations.
5. A conventional `.NET publish` output folder whose entire contents can be copied to the target machine. No asset bundling customisation.
6. A NuGet-installable template with correct name and namespace substitution, documentation and automated generated-project tests.

### Acceptance principle

A generated project must restore, build, pass tests, and work both from source and from the **copied publish folder**. CLI-only modes must have no listening HTTP socket. Web mode must serve actual MudBlazor CSS/JavaScript and support browser interaction, not simply render static HTML or return HTTP 200.

### Non-goals for v1.2

- Single-file/bundled executable publishing, asset embedding, custom resource providers or extraction caches.
- Native AOT, trimming, ReadyToRun, WebAssembly or Interactive Auto.
- Remote/network-wide exposure, user login, OIDC, privileged administration or write APIs.
- Database, queue broker, persistence, distributed workers, plugin systems or extensive CLI infrastructure.
- Generic framework packages that generated applications must reference at runtime.
- Mandatory Docker/Kubernetes/systemd/Windows Service deployment helpers.
- Interactive console REPL or inter-process RPC; `status` concerns the current invocation, **not** a different already-running process.

A future version may explore these, but **none is required to release v1.2**.

## 2. Architecture

```text
                 executable arguments
                         |
                 typed CLI parser
                         |
         +---------------+----------------+
         |               |                |
     one-shot CLI      `run`           `--web`
         |               |                |
     common DI      Generic Host    WebApplication
   (no workers       + worker       + worker + Kestrel
    started)                          + Blazor + API
         |               |                |
         +---------------+----------------+
                         |
               application services
                   (shared DTOs)
```

**Decisions:**

- **Single generated executable project:** `Microsoft.NET.Sdk.Web` permits both Generic Host and ASP.NET Core hosting. The SDK choice does not itself start a network listener.
- **Parse before hosting:** invalid arguments, help and version do not construct a web host. CLI input is never blindly forwarded into ASP.NET Core configuration.
- **Separate host construction paths:** command mode creates a common DI host but does not start hosted workers; `run` starts the Generic Host; `--web` starts `WebApplication` with web-specific registrations.
- **Shared operations:** use one `IApplicationStatusService` (or equally small interface) and a sanitised `ApplicationStatusDto`. UI and API inject it directly, rather than calling localhost HTTP.
- **Minimal dependencies:** use built-in hosting/DI/logging plus MudBlazor. For this fixed CLI grammar, a small typed parser is acceptable; do not add `System.CommandLine` by default.
- **Minimal state:** sample worker demonstrates cooperative cancellation, and shared mutable sample state (if any) is thread-safe. The initial dashboard counter can be per-circuit UI state.

### Modes and lifetimes

| Invocation | Behaviour | Web listener | Process lifetime |
|---|---|---|---|
| `myapp` | Show concise usage | No | Short |
| `myapp --help`, `myapp -h` | Print help | No | Short |
| `myapp --version` | Print product/assembly version | No | Short |
| `myapp status` | Print local status from application service | No | Short |
| `myapp status --json` | Print exactly one JSON object on stdout | No | Short |
| `myapp run` | Start cancellable worker | No | Until shutdown |
| `myapp --web` | Worker + MudBlazor + `/api/v1/status` | `127.0.0.1:5080` | Until shutdown |
| `myapp --web --port 5081` | Same, different validated port | `127.0.0.1:5081` | Until shutdown |
| `myapp --web status` | Usage error | No | Short |
| `myapp --port 5081` | Usage error | No | Short |
| `myapp --urls http://0.0.0.0:80` | Usage error | No | Short |

`myapp --web` does not read stdin or start an interactive CLI prompt. This keeps foreground-service behaviour consistent in terminals, systemd and test runners. In development, use `dotnet run -- --web`.

## 3. Command-line and hosting contract

### 3.1 Argument parsing

Implement one pure `Parse(string[] args)` function returning a strongly typed `AppInvocation` or an error result. Cover:

- Recognised commands, flags and allowed combinations; `--web` is a top-level mode selector.
- `--port` requires `--web`; valid range `1..65535`, culture-invariant parsing.
- Unknown flags/commands, repeated incompatible flags, missing values and malformed numbers fail without starting any host.
- Help and version are standalone; an explicit decision and test should define precedence when combined with other arguments.
- Preserve `--` end-of-options semantics if supported; never accidentally convert a positional literal `--web` into an enablement flag.
- Never pass unrecognised application arguments to `Host.CreateApplicationBuilder` or `WebApplication.CreateBuilder`.

Recommended exit codes: `0` success/help/normal shutdown; `2` usage; `3` startup/configuration; `4` expected operation failure; `70` unexpected error. Signal termination semantics should be tested and documented for each OS rather than assuming universal numeric codes.

### 3.2 Host construction

Use an explicitly separated orchestration layer, approximately:

```csharp
// Architectural sketch — not a complete, drop-in Program.cs.
var invocation = CommandLineParser.Parse(args);
if (!invocation.IsValid) return ReportUsageError(invocation);
if (invocation.IsInformational) return PrintHelpOrVersion(invocation);

return invocation.Mode switch
{
    AppMode.Command => await ExecuteCommandWithCommonServicesAsync(invocation),
    AppMode.Worker  => await RunGenericHostWithWorkerAsync(invocation),
    AppMode.Web     => await RunWebHostWithWorkerAsync(invocation),
    _ => 70
};
```

For one-shot commands, resolve an explicit DI scope and invoke handlers **without starting any `IHostedService`**. For long-running modes, use an ordinary `BackgroundService` with cancellation and a bounded shutdown. Define shared service registration once, avoiding duplicates and preventing the worker from depending on HTTP or Blazor types.

For web mode, use `WebApplication` and configure Kestrel with an explicit loopback endpoint such as `Listen(IPAddress.Loopback, validatedPort)`. Do not use the presence of `ASPNETCORE_URLS`, `DOTNET_URLS`, or launch-profile settings to decide whether to enable web hosting. Validate configuration before startup and ensure inherited `Kestrel:Endpoints` settings cannot introduce an additional/wildcard listener. Use a clean host configuration source or deliberately override/reject conflicting Kestrel configuration and prove the result with socket tests.

**Content-root rule:** Standard web publish assumes its own published content and static-asset metadata are deployed together. Keep framework-supported asset handling. Verify that development (`dotnet run`) and published applications started from an unrelated working directory both locate the correct content root and assets; explicitly configure the content root only with a tested strategy that does not break local development static assets.

### 3.3 Configuration, output and safety

- Default web port: 5080, IPv4 loopback only. A port override must be explicit and validated.
- Optional user config (if included) must not enable HTTP on its own; absence is not a failure.
- Use application-prefixed environment settings rather than treating arbitrary ASP.NET Core keys as trusted application options. Document precedence, ideally defaults < optional config file < application environment < allowed CLI flags.
- Machine-readable `status --json`: one JSON document on stdout; logging/diagnostics to stderr or suppress them for that invocation.
- No default sample operation accesses secrets, starts child processes, writes a database or reads user stdin.
- Keep CLI and web request failures sanitised and distinguish between validation and unexpected exceptions.

## 4. Blazor and MudBlazor integration

### 4.1 Dependencies and render model

- Target **.NET 10 LTS** and Interactive Server Blazor Web App (no `.Client` project).
- Generated application owns `<PackageReference Include="MudBlazor" Version="9.12.0" />`.
- Add `builder.Services.AddRazorComponents().AddInteractiveServerComponents()` and `builder.Services.AddMudServices()` **only in the web host branch**.
- Map the Razor component endpoint with `.AddInteractiveServerRenderMode()`.
- Place global Interactive Server render mode on `Routes` and `HeadOutlet` in `App.razor`, with `prerender: false` for the baseline. Do not attempt to make the `App` root component interactive.
- Place `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider` and `MudSnackbarProvider` together **once** in the interactive `MainLayout`. MudBlazor providers are not supported in a static-only render tree.
- Use the actual .NET 10 SDK-created Blazor Web App scaffold and the pinned MudBlazor 9.12.0 installation instructions as the implementation reference. Treat example fragments in this document as a contract, not as compile-verified generated files.

### 4.2 MudBlazor assets: use the normal SDK pipeline

Include the locally served MudBlazor resources in the app's document head/body as appropriate:

```razor
<link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
<script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
```

The exact Razor expression/attribute syntax must be validated in the actual .NET 10 `App.razor` scaffold and adjusted to match its generated asset conventions; **do not paste this schematic fragment without compiling**. Retain the framework's own Blazor script/import-map placement and `MapStaticAssets()` endpoint mapping. Do not replace the SDK pipeline with a custom `IFileProvider` or custom middleware merely to support publishing.

Standard folder publishing may include `wwwroot`, static asset manifests and separate `_content/MudBlazor` resources. **This is expected and required deployment content.** Deploy the entire publish directory, never only its executable. Do not copy resources from a developer's NuGet cache at runtime. No external CDN or Google Fonts dependency: use bundled framework/MudBlazor files and system fonts.

Remove Bootstrap/open-iconic markup and stylesheet references from the default Blazor scaffold where they clash with MudBlazor; keep a small `wwwroot/app.css` for application-specific styling only.

### 4.3 Minimum generated dashboard

The dashboard is deliberately modest but must demonstrate MudBlazor interactivity and correct provider placement:

| UI element | Contract |
|---|---|
| Application shell | `MudLayout`, `MudAppBar`, `MudMainContent`, `MudContainer`, simple navigation/drawer |
| Status display | `MudCard`/`MudPaper`, application name/version/mode/healthy state from `IApplicationStatusService` |
| Counter | `MudButton` updates a visible per-circuit counter |
| Snackbar | A button invokes `ISnackbar`; message appears and can be dismissed |
| Dialog | A button invokes `IDialogService`; sample dialog opens and closes without privileged side effect |
| Menu/popover | A `MudMenu` or equivalent opens and selects an item |
| Theme | Minimal Material theme, system font, no externally fetched fonts/icons |
| Responsive access | Keyboard usable, 375px mobile width, normal desktop and 200% zoom |

Do not add a large component gallery, a database, charts, external APIs, theme persistence or a control plane. A readable status/counter UI is enough to prove the template.

### 4.4 API contract

`GET /api/v1/status` returns a small explicitly defined, non-sensitive DTO, for example:

```json
{
  "application": "DemoApp",
  "version": "1.0.0",
  "mode": "web",
  "status": "healthy"
}
```

The version is illustrative, not a hard-coded version requirement. Status is the local process' state. Unsupported API routes return 404 and unsupported methods on an existing endpoint return 405 (subject to the mapped endpoint's actual method semantics). Do not serialise internal service objects, configuration, environment variables or exception details. Avoid mutable/admin API examples. The Blazor dashboard consumes the same application service directly, not via localhost HTTP.

### 4.5 Security boundaries

- `--web` **only** listens on `127.0.0.1` unless a future version introduces reviewed remote binding.
- Loopback is not authentication: other local processes/users and malicious browser pages may attempt to reach it. The sample API is read-only and intentionally non-sensitive.
- Reject unexpected Host headers / DNS rebinding requests; verify how Kestrel and any host filtering middleware interact for the configured loopback address.
- Do not enable permissive CORS. Antiforgery middleware may protect supported form interactions but does not replace authentication/authorisation.
- Browser events are untrusted; do not wire MudBlazor buttons to arbitrary shell commands, filesystem access or privileged worker control.
- Avoid untrusted raw HTML/`MarkupString`, accidental debug endpoints, directory browsing or mapping sensitive folders as static files.
- Future Internet/LAN access is a separate design requiring TLS/proxy trust, authentication, authorisation, CSRF defences, auditing and tests.

## 5. Publishing and deployment: standard folders only

### 5.1 Default: framework-dependent folder publish

```bash
# From the generated application's project directory:
dotnet restore
dotnet test
dotnet publish -c Release --self-contained false -o ./publish

# Deploy the whole ./publish directory, then run on a host with
# the compatible .NET 10 ASP.NET Core runtime installed:
dotnet ./publish/DemoApp.dll status
dotnet ./publish/DemoApp.dll --web
```

`DemoApp` is the generated assembly name. Apphost launchers may also be present, depending on RID/build settings. The publish folder is the **complete, atomic deployment unit**, including assemblies, configuration/manifest files and web assets. Copy it in full to its intended deployment directory and point service supervisors at the published executable or DLL.

### 5.2 Optional: self-contained folder publish

For a target machine without .NET installed, use the SDK's existing self-contained **multi-file** folder publishing:

```bash
dotnet publish -c Release -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o ./publish/linux-x64

# Copy the ENTIRE folder to the Linux host and run:
./publish/linux-x64/DemoApp --web
```

This includes .NET runtime files and is platform-specific; it still produces **many files**. The template contains no specialised MSBuild targets, asset packer or extraction logic. Supporting the optional command is not a mandatory initial-release integration matrix, but the README must describe the distinction accurately. If distributed as self-contained, runtime security updates require republishing/redeployment.

### 5.3 Deployment verification

1. Publish using the standard SDK command.
2. Copy the **entire publish directory** into a fresh location, separate from source and NuGet caches.
3. Start `status --json` and `run`; assert no HTTP listeners.
4. Start `--web --port <testPort>` from that copied directory; assert loopback-only binding.
5. Load the dashboard and API; check all referenced framework, app and MudBlazor resources return valid CSS/JS (not HTML fallbacks or 404).
6. Exercise counter, snackbar, dialog and menu in a real browser; capture console and network failures.
7. Stop cleanly and prove the worker cancels and listener closes.

The copied folder may be read-only if any runtime-generated data/logs are written to a separate writable app-data directory. A full no-runtime host test applies **only** to self-contained folder publishing, not the default framework-dependent build.

## 6. Generated project and template packaging

### 6.1 Generated solution layout

Keep one executable project and one small test project:

```text
DemoApp/
├── DemoApp.slnx
├── README.md
├── src/
│   └── DemoApp/
│       ├── DemoApp.csproj
│       ├── Program.cs
│       ├── Bootstrap/
│       │   ├── CommandLineParser.cs
│       │   └── Hosting.cs
│       ├── Application/
│       │   ├── IApplicationStatusService.cs
│       │   └── ApplicationStatusService.cs
│       ├── Cli/
│       │   └── StatusCommand.cs
│       ├── Workers/
│       │   └── SampleWorker.cs
│       ├── Web/
│       │   └── ApiEndpoints.cs
│       ├── Components/
│       │   ├── App.razor
│       │   ├── Routes.razor
│       │   ├── _Imports.razor
│       │   ├── Layout/MainLayout.razor
│       │   └── Pages/Dashboard.razor
│       └── wwwroot/app.css
└── tests/
    └── DemoApp.Tests/
        ├── DemoApp.Tests.csproj
        ├── CommandLineParserTests.cs
        └── ApplicationStatusServiceTests.cs
```

This is a guide, not an instruction to create empty files. Add a test/integration file only when it tests concrete behaviour. Use a `.slnx` only after verifying the selected SDK/template-engine correctly generates and builds it; otherwise use a `.sln`.

Generated `.csproj` baseline:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MudBlazor" Version="9.12.0" />
  </ItemGroup>
</Project>
```

Test dependencies are pinned separately. Do not impose repository-only `Directory.Packages.props` or `.props` on the generated consumer inadvertently.

### 6.2 Template source and metadata

```text
EccentricWare.BlazorCli/
├── README.md
├── LICENSE
├── docs/
│   ├── DESIGN.md
│   ├── DEVELOPMENT.md
│   ├── SECURITY.md
│   └── RELEASE.md
├── src/
│   └── EccentricWare.BlazorCli.TemplatePack/
│       ├── EccentricWare.BlazorCli.TemplatePack.csproj
│       └── content/
│           └── EccentricWare.BlazorCli.TemplateApp/
│               ├── .template.config/template.json
│               └── ...generated project source...
└── tests/
    ├── EccentricWare.BlazorCli.TemplateContractTests/
    └── EccentricWare.BlazorCli.IntegrationTests/
```

`EccentricWare.BlazorCli.TemplatePack` is a *pack-only* NuGet project; set NuGet `PackageType=Template`, include the entire intended content tree, exclude `bin`, `obj`, secrets and temporary files, and ensure the `.nupkg` can be installed. `EccentricWare.BlazorCli.TemplateApp` is the replaceable `sourceName`, not a dependency the consumer must retain.

Minimal `template.json`:

```json
{
  "$schema": "https://json.schemastore.org/template",
  "author": "EccentricWare",
  "classifications": ["Web", "Blazor", "Console", "CLI"],
  "identity": "EccentricWare.BlazorCli.CSharp",
  "name": "EccentricWare Blazor CLI Application",
  "shortName": "ew-blazorcli",
  "sourceName": "EccentricWare.BlazorCli.TemplateApp",
  "preferNameDirectory": true,
  "description": "CLI-first .NET 10 application with optional MudBlazor dashboard and API via --web.",
  "tags": { "language": "C#", "type": "project" }
}
```

This fragment is metadata guidance, not a substitute for validating template-engine rename rules. Generate both simple and dotted project names; test namespace, assembly, project, solution, test and README substitution. Reject or safely normalise invalid C# identifiers. The user supplies `-n`/`-o` at template-generation time; `--web`/`--port` are **generated executable** flags, not template parameters.

### 6.3 Build and usage contract

```bash
# Template repository, after implementing pack-only project:
dotnet pack src/EccentricWare.BlazorCli.TemplatePack -c Release

# Use the actual generated .nupkg path (do not assume output directory):
dotnet new install /absolute/path/EccentricWare.BlazorCli.<version>.nupkg
dotnet new ew-blazorcli -n DemoApp -o ./generated/DemoApp

dotnet restore ./generated/DemoApp
dotnet build ./generated/DemoApp
dotnet test ./generated/DemoApp

# Generated app's project directory:
cd ./generated/DemoApp/src/DemoApp
dotnet run -- status --json
dotnet run -- --web
```

Publish the NuGet template package only after validating the installed package (not only the template source tree). Include package metadata, licence decision, changelog and supported SDK/runtime requirements. The generated README must be usable independently of this design document.

## 7. Test plan and release gates

### 7.1 Required automated coverage

| Suite | Normal behaviour | Failure/adversarial behaviour |
|---|---|---|
| Parser unit tests | All recognised commands, help, JSON, port overrides | Unknown/duplicate options, missing/invalid ports, mixed modes, oversized input, `--urls`, escaped `--web` |
| Service unit tests | Correct status DTO and version/mode values | Invalid inputs and data leakage, concurrent calls |
| Worker tests | Starts once per long-running host, cancellation and shutdown | Dependency failure, immediate cancel, exceptional tasks, scope disposal |
| CLI process tests | Short-lived help/status, exit codes, JSON purity | Unexpected exception, no worker launched, no HTTP listener |
| Hosting integration | `run` no listener, `--web` loopback HTTP/API | Hostile `ASPNETCORE_URLS`/Kestrel config, port conflict, stale process, shutdown |
| Web/API integration | Dashboard, DTO, 404/405, local static resources | Host header/DNS rebinding attempt, unexpected methods, sensitive fields |
| Browser (Playwright) | MudBlazor counter, menu, dialog, snackbar, responsive keyboard navigation | Missing provider, failed JS, disconnected circuit, rapid clicks, concurrent sessions, blocked outbound web |
| Folder-publish smoke | Copy full publish output; CLI + worker + API + MudBlazor UI all work | Wrong CWD, missing asset/file, no unexpected source-tree dependency |
| Template contract | Install packed `.nupkg`, discover, generate, build/test/publish | Dotted names, spaces in path, invalid names, leftover tokens, unintended overwrite, uninstall |

Process tests must inspect actual bound socket endpoints, rather than checking only whether `curl localhost:5080` happens to fail. Browser tests must assert resource types, browser console/network errors and observable interaction state; a 200 on `/` is insufficient. Execute tests in CI using isolated ports and time-bounded processes to avoid false failures and hangs.

### 7.2 Initial CI platform policy

- **Required:** Ubuntu x64 with .NET 10 SDK; restore, build, unit/integration/browser smoke, packed-template generation, and default framework-dependent folder publish.
- **Required:** Windows x64 with .NET 10 SDK; build, CLI/process/template contract and folder-publish smoke. Browser checks on Windows are recommended; at minimum Linux browser coverage is mandatory.
- **Optional:** Linux ARM64 and self-contained folder publish. Advertise only platforms actually exercised by CI or equivalent native verification.
- No strict single-binary inventory checks, no custom asset-provider tests, no runtime extraction attack suite and no ARM64 requirement unless explicitly added later.

### 7.3 Work sequence and completion evidence

1. **Working app reference:** Create a minimal net10.0 Blazor Web App, add pinned MudBlazor, verify global interactive providers and local resources in a browser.
2. **Runtime modes:** Implement typed parser, shared DI/DTO, one-shot CLI, worker host and web host; prove absence of listeners in CLI/run modes.
3. **Standard publishing:** Folder publish, copy output to a clean directory, and test API and MudBlazor from the copy. Fix only actual content-root or asset-discovery defects.
4. **Template packaging:** Convert the working app into a replaceable source project, pack the `Template` `.nupkg`, and validate generated projects with different names and paths.
5. **Security and release:** Complete negative tests, dependency/licence review, contributor and consumer docs, native CI matrix and prerelease install test.

Every work item must include applicable automated tests and documentation updates. Keep failures and skipped tests visible; do not claim unexecuted functionality is verified.

### 7.4 Definition of done

- [ ] Template package `EccentricWare.BlazorCli` installs and lists as `ew-blazorcli`.
- [ ] Clean `dotnet new` generation produces a buildable, renamed solution with consumer-owned MudBlazor 9.12.0 dependency.
- [ ] `myapp`, `--help`, `--version`, `status` and `status --json` behave predictably with correct output/exit status.
- [ ] One-shot CLI and `run` open **no** HTTP listener; `--web` is the only path enabling Kestrel.
- [ ] `--web` binds loopback only, respects `--port`, serves `/api/v1/status` and exposes no privileged sample API.
- [ ] Interactive MudBlazor dashboard renders and its counter, menu, snackbar and dialog actually work.
- [ ] No required off-loopback browser resources, no missing MudBlazor/framework assets and no secrets in rendered output/API.
- [ ] Normal framework-dependent **publish directory**, copied in full and run from unrelated CWD, passes CLI and web tests.
- [ ] Worker cancellation, command parsing, config override and hostile binding attempts are covered by automated tests.
- [ ] Linux and Windows release CI requirements pass; dependency/licence and security review recorded.
- [ ] Generated README and repository docs explain runtime modes, publishing, deployment and deferred features accurately.

**Not a release condition:** one executable file, no adjacent assets, native AOT, runtime-free deployment, or remote access. The optional self-contained folder publish is supported by the SDK but must not be advertised as tested unless validated.

## 8. Risks, open decisions and constraints

| Area | Risk / decision | Response |
|---|---|---|
| Host binding | ASP.NET Core config may add/override a listener | Explicit Kestrel loopback endpoint; negative OS socket tests |
| Asset relocation | Framework development and publish roots differ | Use standard SDK asset pipeline; test both environments and unrelated CWD |
| MudBlazor render mode | Static layout/provider causes failed interactivity | Global Interactive Server, providers under interactive layout; real-browser tests |
| Template substitutions | Names and paths leave placeholders or invalid namespaces | Generate from packed nupkg across representative names; compile and test |
| Worker/CLI lifecycle | A one-shot command inadvertently starts a worker | Never start hosted services in one-shot mode |
| Local security | Hostile browser processes can access loopback | Read-only nonsensitive API; Host filtering; no privileged browser actions |
| Platform differences | SDK, apphost and folder runtime packaging differ | Explicit .NET 10 support; platform-specific smoke tests |
| MudBlazor upgrades | Breaking component/provider changes | Pin 9.12.0; upgrade only with regenerated-build/browser verification |

**Decisions fixed now:** CLI-first, separate Generic Host/WebApplication, `--web` only, localhost HTTP, MudBlazor 9.12.0, Interactive Server, ordinary folder publish, NuGet template package, one generated executable project. The remaining implementation choices are limited to concrete SDK-compatible details verified by building the generated template (for example, content-root resolution and `.slnx` handling). They are not grounds for building a custom framework.

## 9. Implementation hand-off for a new coding agent

Read the design and current official .NET 10/MudBlazor docs. Start with a normally buildable reference app; do not begin by editing template placeholders. Establish CI-backed evidence for CLI socket isolation and MudBlazor browser interactivity, then validate standard publishing from a copied folder. Finally author and pack the template; run generated-project tests **from the `.nupkg`**. Record SDK version, exact commands, failures, output paths and evidence. Do not add single-file, embedding/extraction or remote-administration code.

**Delivery checklist:** source repository, generated-project code and tests, pack-only NuGet project, template.json, release/CI automation, README/security/development documentation, `.nupkg` and build/test logs. A design document alone does not demonstrate the resulting project works.

## 10. Reference documentation

1. Microsoft — .NET deployment/publishing overview (framework-dependent and self-contained folder modes): https://learn.microsoft.com/en-us/dotnet/core/deploying/
2. Microsoft — Generic Host: https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
3. Microsoft — Custom templates: https://learn.microsoft.com/en-us/dotnet/core/tools/templates
4. Microsoft — Template packages: https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-template-package
5. Microsoft — Blazor render modes: https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0
6. Microsoft — Blazor static assets: https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0
7. Microsoft — `MapStaticAssets`: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0
8. Microsoft — Kestrel endpoint binding: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0
9. MudBlazor 9.12.0 NuGet: https://www.nuget.org/packages/MudBlazor/9.12.0
10. MudBlazor installation (interactive providers, service registration and asset paths): https://mudblazor.com/getting-started/installation

---

### Revision rationale and verification status

**v1.1 → v1.2:** Deliberately removed mandatory single-executable publishing, custom embedded/extracted asset systems, bundled manifest rewriting, asset-provider abstractions, strict one-file tests, native-extraction security machinery and their blocking release milestones. Replaced them with the .NET SDK's normal folder publishing and focused copied-folder/browser smoke testing. Kept the important CLI/Web host separation, shared services, pinned MudBlazor UI, security checks, template packaging and meaningful automated tests.

**Design reviewed; code not built.** The .NET 10 publishing and static-assets approach, MudBlazor 9.12.0 package and installation contract were checked against the above primary documentation. No generated application, template package, browser tests or runtime validation has been executed as part of producing this specification.
