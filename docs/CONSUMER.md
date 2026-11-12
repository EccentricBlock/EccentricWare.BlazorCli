# Consumer usage and extension guide

## Generate and use

After installing `EccentricWare.BlazorCli.0.2.0.nupkg` with `dotnet new install`, run (the name becomes the solution, project, assembly and namespace; characters that are not valid in a C# identifier become `_`, so `-n My-Tool` creates `My_Tool`):

```bash
dotnet new ew-blazorcli -n MyUtility -o ./MyUtility
cd MyUtility
dotnet test
dotnet run --project src/MyUtility -- status --json
dotnet run --project src/MyUtility -- run
dotnet run --project src/MyUtility -- --web --port 5081
```

For a published DLL, use `dotnet /path/to/publish/MyUtility.dll --web`. For a RID-specific self-contained apphost, use the apphost in the **complete** published directory. The default web URL is `http://127.0.0.1:5080` and a port override is limited to 1–65535. Browser dashboard and `GET /api/v1/status` share a DI service directly.

## Customise the generated app

The generated `<Version>` starts at `1.0.0` and is yours to change; `--version`, `status` and the API read it from the assembly (pre-release labels such as `1.2.0-beta.1` are kept, build metadata after `+` is dropped). Generated tests do not hard-code a version.

For a failure the user can fix (bad input, missing file), throw `ExpectedOperationException` from the command handler: its message is printed and the exit code is 4. Any other exception exits 70 and prints a fixed message.

`src/MyUtility/Bootstrap/CommandLineParser.cs` and `ApplicationEntryPoint.cs` govern command syntax and orchestration. `Application/` contains the shared status service; put new business services here and register them once in `ServiceRegistration.cs`. Add CLI command handlers in `Cli/`, workers in `Workers/`, and MudBlazor pages in `Components/Pages/`. Read-only web routes belong in `Web/`; use DTOs rather than returning services or internal models.

The `MainLayout` owns all MudBlazor providers. Keep the global Interactive Server render mode for the app unless you fully understand the implications of provider placement and circuit lifecycle. Avoid remote resources; assets are served locally through the SDK.

## Portability and operating limitations

Normal framework-dependent builds require .NET 10 ASP.NET Core runtime on the host. A self-contained deployment bundles the runtime but still comprises many files. App/worker cancellation uses the standard Generic Host model, not a service manager. No generated Docker, Kubernetes, systemd or Windows Service integration is included. For remote access, authentication, TLS, auditing or privileged operations, create a separate design and security review before extending the API; **do not** simply change the listener address.
