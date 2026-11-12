# EccentricWare.BlazorCli generated application

A single **.NET 10** CLI-first application. It starts **no HTTP listener** unless
`--web` is specified; web mode adds a local **MudBlazor 9.12.0** dashboard,
a read-only status API and the same background worker used by `run`.

## Requirements

- .NET 10 SDK to build/run from source.
- .NET 10 ASP.NET Core runtime for a framework-dependent deployment.

## Install and run

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/EccentricWare.BlazorCli.TemplateApp -- --help
dotnet run --project src/EccentricWare.BlazorCli.TemplateApp -- status --json
dotnet run --project src/EccentricWare.BlazorCli.TemplateApp -- run
dotnet run --project src/EccentricWare.BlazorCli.TemplateApp -- --web
```

Open http://127.0.0.1:5080 in web mode. To override its loopback port:

```bash
dotnet run --project src/EccentricWare.BlazorCli.TemplateApp -- --web --port 5081
```

Use Ctrl+C to stop `run` or `--web`. Status describes **the current invocation**;
it is not an IPC query for another process.

## Publish

```bash
dotnet publish src/EccentricWare.BlazorCli.TemplateApp -c Release \
  --self-contained false -o ./publish
dotnet ./publish/EccentricWare.BlazorCli.TemplateApp.dll status --json
dotnet ./publish/EccentricWare.BlazorCli.TemplateApp.dll --web
```

Copy the **whole publish directory**, including static assets and manifests;
the DLL by itself is not a complete deployment. A self-contained multi-file
folder may be produced with `-r linux-x64 --self-contained true
-p:PublishSingleFile=false`. No single-file publish is supported.

The API is `GET /api/v1/status` and publishes only application, version, mode
and status. Web mode is IPv4 loopback-only; it is not a secure admin interface.
The mode is never enabled by ASP.NET Core URL environment variables.

See [docs/USAGE.md](docs/USAGE.md) for architecture, command combinations,
security notes and publication details.

MIT licensed. Generated from the EccentricWare.BlazorCli template 0.2.0. The application's own version is `<Version>` in the project file (it starts at `1.0.0`); `--version`, `status` and the API report it.
