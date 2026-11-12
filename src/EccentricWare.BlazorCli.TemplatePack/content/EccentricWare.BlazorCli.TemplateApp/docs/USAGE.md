# Generated application operations

## Modes and contracts

| Arguments | Lifetime | Listens? | Meaning |
|---|---|---|---|
| none / `-h` / `--help` | short | no | Usage |
| `--version` | short | no | The project's `<Version>` (for example `1.0.0` or `1.2.0-beta.1`) |
| `status` | short | no | Human-readable current-invocation status |
| `status --json` | short | no | One JSON object on stdout |
| `run` | until signal | no | Worker with cancellable 30-second heartbeat |
| `--web` | until signal | `127.0.0.1:5080` | Worker + UI + read-only API |
| `--web --port N` | until signal | `127.0.0.1:N` | N in 1–65535 |

Arguments are case-sensitive. Unrecognised, repeated or incompatible switches,
including `--urls`, fail with exit code 2. `--` is deliberately unsupported.
`--help` and `--version` must appear alone. The application never forwards
CLI options into ASP.NET Core's command-line configuration.

Exit codes: 0 success/normal shutdown; 2 usage; 3 startup/configuration;
4 expected operation error (a handler threw `ExpectedOperationException`, whose
message is printed); 70 unexpected error, including a background worker that
threw (the host stops and the process exits 70, not 0). `run` and `--web` stop
cleanly on SIGTERM and Ctrl+C; signal-terminated exit codes can differ by
OS/supervisor. No console REPL or stdin input is used.

## Architecture and editing

`Bootstrap/CommandLineParser.cs` parses before hosting. `Bootstrap/Hosting.cs`
selects the Generic Host (worker only) or ASP.NET Core WebApplication
(worker + web). Register new workers in `ServiceRegistration.AddApplicationWorkers`,
which both modes call, and new shared services in `AddApplicationServices`; `Cli/StatusCommand.cs` and the web dashboard/endpoint share
`Application/IApplicationStatusService.cs`, injected directly. The sample worker
is `Workers/SampleWorker.cs`. Web components are in `Components/`.

Add application operations to the shared application service or other ordinary
DI services, then expose only explicitly safe DTOs through the API. UI code
must not call localhost HTTP to reach the shared service.

## Browser/UI

In `--web` mode open `http://127.0.0.1:5080/`. The dashboard demonstrates a
counter, snackbar, dialog and menu. These change local UI state only; no
privileged control or persistence is provided. Global Interactive Server is
set on `Routes` and `HeadOutlet`; the MudBlazor providers live in the
interactive `MainLayout`. MudBlazor CSS and JavaScript are served locally.

## Names

The name you pass to `dotnet new -n` becomes the solution, project, assembly and
namespace. Characters that are not valid in a C# identifier become `_`
(`-n My-Tool` creates `My_Tool`; `-n 1Tool` creates `_1Tool`). A name that would break the
project gets a leading underscore too: a reserved C# keyword (`-n class` creates `_class`), a
framework assembly name (`-n System` creates `_System`; an assembly called `System` collides with
the framework at run time) and the name of a package the app depends on (`-n MudBlazor` creates
`_MudBlazor`; otherwise NuGet reports a dependency cycle, NU1108).

## Publishing and deployment

Framework-dependent: `dotnet publish -c Release --self-contained false -o publish`
(from the app project, or pass `--project` path). The destination needs the
.NET 10 ASP.NET Core runtime. Copy **all** files in `publish/`. Run with
`dotnet /absolute/path/to/publish/YourApp.dll --web`, regardless of the shell's
working directory. For no installed .NET runtime, use an SDK-built multi-file
self-contained deployment, for example:

```bash
dotnet publish -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -o publish/linux-x64
```

Never ship only the executable/DLL. The worker uses stdout/stderr logging and
should be supervised for production usage; systemd/Windows Service units are
not generated. No HTTP listener opens in one-shot CLI or `run` mode.

## Safety

The only HTTP binding is 127.0.0.1. Host headers other than localhost and
127.0.0.1 are refused (400), and so is any request whose `Origin` header is not
this listener's own origin (403): a page on another website can reach
`127.0.0.1:5080` from the user's browser, and the Origin check stops it opening
the dashboard's WebSocket circuit. Requests without an Origin (curl, other
local programs) are still accepted, so this is not authentication. Arbitrary `ASPNETCORE_URLS`, `DOTNET_URLS`,
`Kestrel__Endpoints__*` and settings files cannot add a wildcard endpoint in
this baseline. Loopback **does not** imply authorisation: any local user/process
may still request its read-only status. There is no account system, TLS, remote
access, database, persistence, file service or control plane. Do not expose
this service through a reverse proxy or tunnel without a separate security
review and access controls. Security updates to self-contained deployments
require rebuilding and redeploying the complete folder.
