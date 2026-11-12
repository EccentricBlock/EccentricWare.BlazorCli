# Architecture and design decisions

**Implementation:** EccentricWare.BlazorCli 0.2.0; design specification v1.2, dated 10 November 2026.

## Product contract

`dotnet new ew-blazorcli` instantiates one editable .NET 10 Web SDK executable project and one xUnit test project; consumers do **not** depend on a runtime EccentricWare framework. MudBlazor is a pinned package reference in the consumer-owned executable. The installed template identity is `EccentricWare.BlazorCli.CSharp`, NuGet ID `EccentricWare.BlazorCli`, package version `0.2.0` and placeholder name `EccentricWare.BlazorCli.TemplateApp`. The placeholder is replaced through a derived `safeName` symbol, not `sourceName` (see [Template names](#template-names)).

The supported modes are mutually exclusive, selected **before** constructing any host:

```text
arguments --pure parse--> typed AppInvocation
   |- help/version ---------------------------> Console, exit
   |- status/status --json --> common DI Host, not started --> status DTO --> Console, exit
   |- run ------------------> Generic Host + SampleWorker --> cancellation
   `- --web [--port N] ----> WebApplication + SampleWorker + Razor/MudBlazor + API
                                    |
                                    +---- IPv4 127.0.0.1:5080, or validated port
```

CLI options do not flow into the ASP.NET Core host configuration. Neither an SDK Web project nor presence of `ASPNETCORE_URLS` turns on Kestrel by itself. No command creates a background service unless `run` or `--web` is selected. A `status` invocation reports **its own** process status, not the state of an external worker.

## Source component responsibilities

| Path in generated `src/<AppName>/` | Responsibility / contract |
|---|---|
| `Program.cs` | Await entry point and return OS exit code. |
| `Bootstrap/CommandLineParser.cs` | Stateless strict grammar, port range, bounded input and errors. |
| `Bootstrap/AppInvocation.cs` | Typed command/mode and parse result. |
| `Bootstrap/ApplicationEntryPoint.cs` | Error mapping, stdout/stderr split, one-shot DI construction. |
| `Bootstrap/Hosting.cs` | Separate Generic Host and WebApplication; loopback listener; development detection; exit code after a worker crash. |
| `Bootstrap/ServiceRegistration.cs` | `AddApplicationServices` (every mode, no hosted services) and `AddApplicationWorkers` (`run` and `--web`). |
| `Bootstrap/ExpectedOperationException.cs` | The only exception that maps to exit code 4. |
| `Application/` | `AppMode`, the non-sensitive four-field DTO and the injectable status service. Depends on nothing in `Bootstrap`. |
| `Cli/StatusCommand.cs` | JSON and text presentations without logging or HTTP. |
| `Workers/SampleWorker.cs` | Cancellable timer and logging, with no input or privileged work. |
| `Web/ApiEndpoints.cs` | GET-only `/api/v1/status`, DTO allowlist. |
| `Web/LoopbackGuard.cs` | Host, port and Origin checks plus browser safety headers. Pure predicates, unit tested. |
| `Components/` | Interactive Server layout, dashboard and sample dialog. |
| `wwwroot/` | Only local static assets; no CDN dependencies. |

`IApplicationStatusService` is called directly by CLI, API and Blazor component; no HTTP client is injected for shared business logic. The `SampleWorker` is intentionally a minimal demonstration, with no process execution, database, secrets, queue or networking. Worker and web processes use standard ASP.NET Core cancellation/lifetime handling. UI counter state belongs to an interactive Blazor circuit; it is not shared or persisted. The worker heartbeat is a diagnostic example, **not** a job scheduler.

## Interactive Server and MudBlazor

The Web host registers `AddRazorComponents().AddInteractiveServerComponents()` and `AddMudServices()`. The `App` root is static. `Routes` and `HeadOutlet` use Interactive Server render mode with prerendering disabled. `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider` and `MudSnackbarProvider` are co-located once in the interactive `MainLayout`. The dashboard provides a status card, working counter, snackbar, dialog and menu. All resources use the SDK-managed `@Assets[...]` static asset pipeline, `app.MapStaticAssets()` and `app.MapRazorComponents<App>().AddInteractiveServerRenderMode()`; no custom file providers or extraction.

The project deliberately targets `.NET 10` and MudBlazor `9.12.0`. Source package versions are exact rather than floating. Upgrading a package requires generated-project, published-folder and real-browser checks.

## Host configuration and content roots

The web host binds using `ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port))`. Before building the web host, inherited configuration sources are cleared and replaced with a small allowlist. This is intended to prevent inherited `Kestrel__Endpoints__*`, `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `DOTNET_URLS` or a hostile `appsettings.json` from silently creating a second endpoint. `LoopbackGuard` rejects every Host other than `localhost` or `127.0.0.1` (or with a different explicit port) with 400, and every request whose `Origin` header is not this listener's own origin with 403. The Origin check matters because a page on any website can reach `127.0.0.1:5080` with a valid Host header and open a Blazor circuit over a WebSocket; Host checks alone do not stop that. Requests without an Origin header (same-origin navigation, curl, other non-browser clients) pass. These controls are intentionally redundant; they do **not** make an authenticated or remotely safe service.

The content root is always `AppContext.BaseDirectory`, so neither web mode nor publishing depends on the current working directory. The environment is `Development` when `<assembly>.staticwebassets.runtime.json` sits beside the assembly and `Production` otherwise. A build writes that manifest and `dotnet publish` does not. Development lets the SDK resolve static assets from the project and the NuGet cache. Detection by this file works for any output path on the machine that built it (`-o`, `UseArtifactsOutput`). The manifest holds absolute paths to the project's `wwwroot` and the NuGet cache, so a `bin` folder copied to another machine, or after the project or cache was deleted, is Development with dead asset roots: deploy `dotnet publish` output, never `bin`. The earlier rule, which looked three directories above `bin/<config>/net10.0` for the project file, served every asset with an empty `Content-Type` outside that layout, and the browser refuses such files under `X-Content-Type-Options: nosniff`.

## Command-line grammar and exit status

| Command | Valid syntax | Exit code on success |
|---|---|---|
| help | none, `-h`, `--help` | 0 |
| version | `--version` | 0 |
| status | `status`, `status --json` | 0 |
| worker | `run` | 0 on normal stop |
| web | `--web`, `--web --port N` | 0 on normal stop |

`--help` and `--version` are valid only alone, including when mixed with unknown options. `--` is explicitly unsupported. Invalid combinations return 2, host startup errors 3, unhandled errors 70. Exit code 4 is reserved for `ExpectedOperationException`, which a command handler throws for a failure the user can fix; a framework `ArgumentException` is a bug and returns 70. A background worker that throws stops the host, and the generic host then returns normally, so `Hosting.RunToCompletionAsync` inspects the workers and returns 70 instead of letting the process exit 0. OS signal exit status is not universally guaranteed. The CLI's own error output (stderr) avoids exception traces, environment values and secrets; Generic Host console logging in `run` and `--web` can still print a stack trace to stdout when a worker crashes or startup fails; the JSON status command prints **exactly one** JSON object to stdout.

## Publishing and rejected complexity

The **directory** produced by `dotnet publish -c Release --self-contained false` is the deployment unit. It includes the app DLL, referenced assemblies, runtime/static-asset metadata, content and MudBlazor resources. A `linux-x64` self-contained *folder* is an optional native SDK publish choice; it is not a single binary and is not a separate feature implemented here. Do not use single-file, trimming, Native AOT, resource embedding, custom static-asset providers, packaging of the web root or extraction caches. Also not implemented: WebAssembly/Interactive Auto, remote binding, TLS/reverse proxy, user authentication, privileged command API, persistence, IPC, REPL or deployment supervisors.

## Threat model summary

The relevant assets are local process status and local CPU/memory; no secrets or privileged admin operations are introduced. Attacker-controlled inputs are CLI arguments, hostile inherited hosting environment/configuration, web requests and browser events. Defences include strict parse-before-host, loopback address binding, Host checking, no permissive CORS, no state-changing API, no raw untrusted HTML and a DTO with four explicit safe fields. Residual threats include access by other local users/processes, malicious local browser pages, denial-of-service and dependencies. See [SECURITY.md](SECURITY.md).

## References

- [DESIGN_SPEC.md](DESIGN_SPEC.md): design specification v1.2, the controlling requirements.
- https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates
- https://learn.microsoft.com/en-us/aspnet/core/blazor/components/prerender?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0
- https://mudblazor.com/getting-started/installation
- https://www.nuget.org/packages/MudBlazor/9.12.0

## Refinements in v0.2

- A generic HTTP exception handler prevents accidental development-time stack
  trace exposure; accepted requests get standard browser safety headers.
- Host checks reject a mismatching explicit port in addition to an unrecognised
  name, without claiming loopback implies authentication.
- The template package uses an explicit discovery file and a uniform recursive
  content glob, with sensitive-file exclusions and a fail-closed source audit.
- Release verification requires actual SDK package installation/builds,
  a copied read-only POSIX publish folder, and real browser runs against both
  development and relocated-publish assets. Python tests enforce safety rules.
- The version is 0.2.0. The generated app remains ordinary editable
  `net10.0` source and still has a single executable project.

## Template names

`sourceName` replaces file and directory names with the raw `-n` value but replaces file content with the `safe_namespace` form. For `-n Demo-CLI` that produced `src/Demo-CLI/` on disk and `src/Demo_CLI/Demo_CLI.csproj` inside the `.slnx` and the test project reference, so restore failed. The template now declares one derived symbol, `safeName`, whose `valueTransform` is the chain `safeProjectName`: `safe_namespace`, then `escapeCSharpKeyword` (a regex form that puts `_` in front of each of the 77 reserved C# keywords, matched as whole lowercase segments), and uses it for both `replaces` and `fileRename`. Every file name, namespace, project reference and README command then uses the same spelling:

| `-n` value | Solution, project and namespace |
|---|---|
| `DemoApp` | `DemoApp` |
| `Demo.App` | `Demo.App` |
| `Demo-CLI` | `Demo_CLI` |
| `1Demo` | `_1Demo` |
| `class` | `_class` |
| `Demo.namespace` | `Demo._namespace` |
| `System` | `_System` |
| `MudBlazor` | `_MudBlazor` |

Two kinds of name used to fail and now get a leading underscore. A project whose assembly name equals a framework assembly (`-n System`) compiled, but the runtime resolved `System` to the framework facade and every type load failed with `TypeLoadException`. A project named after a package it depends on (`-n MudBlazor`) referenced itself (NU1108). `escapeReservedAssemblyName` matches the whole name, case-insensitively (NuGet IDs and Windows/macOS file names ignore case), against 315 names: the managed assemblies of the .NET 10 shared frameworks and the app's package closure, generated by `scripts/reserved_names.py`. `System.Foo`, `Demo.System` and `Microsoft` are not assemblies and keep their names. The output directory still follows `-o` (or the raw name when `-o` is absent). `Hosting.cs` aliases the root component (`using RootComponent = ...Components.App`) and `Routes.razor` anchors on `typeof(Routes)`, because a project named `Acme.App` otherwise turns the simple name `App` into the namespace `Acme.App` (error CS0118).

## Verification status

The 0.2.0 source was first built with a real .NET 10 SDK on 12 November 2026. That pass found defects the static review could not see; they and their fixes are listed in [V02_AUDIT.md](V02_AUDIT.md), and the current results are in [VALIDATION_STATUS.md](VALIDATION_STATUS.md).
