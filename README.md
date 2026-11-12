# EccentricWare.BlazorCli

**v0.2.0** · MIT · .NET 10 · MudBlazor **9.12.0**

A `dotnet new` **project template** for a CLI-first C# executable with an
optional loopback-only Blazor Interactive Server dashboard and read-only API.
By default it runs short-lived commands, not Kestrel. `run` starts a cancellable
worker without HTTP; `--web` starts the same worker, MudBlazor UI and API.

## Requirements

.NET 10 SDK with access to NuGet.org (for restore) to build the generated
application. Framework-dependent published applications require the .NET 10
ASP.NET Core runtime on the destination machine.

## Install

To package and install this source checkout:

```bash
dotnet pack src/EccentricWare.BlazorCli.TemplatePack -c Release -o artifacts
dotnet new install ./artifacts/EccentricWare.BlazorCli.0.2.0.nupkg
```

Create a project, build and run:

```bash
dotnet new ew-blazorcli -n DemoApp -o ./DemoApp
cd DemoApp
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DemoApp -- status --json
dotnet run --project src/DemoApp -- run
dotnet run --project src/DemoApp -- --web
```

The name you pass with `-n` becomes the solution, project and namespace; characters that are not valid in a C# identifier become `_` (`Demo-CLI` creates `Demo_CLI`).

Open `http://127.0.0.1:5080` for the dashboard. Use
`--web --port 5081` for a different port. Stop long-running modes with Ctrl+C.
`--help`, `--version`, `status` and `status --json` work without HTTP.

Normal publishing produces a **multi-file folder**:

```bash
dotnet publish src/DemoApp -c Release --self-contained false -o publish
dotnet ./publish/DemoApp.dll --web
```

Deploy the **entire** `publish/` directory, not only the DLL. Single-file
publishing, remote binding, accounts and privileged APIs are not part of v0.2.

## Documentation

Full architecture, configuration, usage, security, validation and release
procedures are in [docs/](docs/), including the [v0.2 audit and critique](docs/V02_AUDIT.md)
and a [verification-status record](docs/VALIDATION_STATUS.md). The generated project also contains its own
README and `docs/USAGE.md`.

Source: [MIT licence](LICENSE) · [Changelog](CHANGELOG.md).
