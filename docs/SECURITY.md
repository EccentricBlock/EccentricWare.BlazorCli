# Security model and operational boundary

## Intended exposure

The default process, its CLI commands and `run` **do not** listen on any network socket. Only the explicit `--web` mode starts ASP.NET Core and listens on **IPv4 127.0.0.1** (default TCP 5080). `--web --port N` chooses a different local port, validated as decimal 1–65535. The web UI and API are read-only demonstrations, not a secure remote administration mechanism.

Loopback **does not authenticate** callers: another local user or local process can connect, and malicious sites may attempt cross-origin/browser requests. The sample status DTO has four public fields (application, version, mode and status), and `/api/v1/status` has no mutations. No raw environment/configuration, secrets, arbitrary exception messages, filesystem contents or worker control are returned. Do not add a privileged web operation without designing authentication, authorisation, CSRF controls, audit trails and rate limits.

## Attack surface and controls

| Attack | Mitigation | Residual risk |
|---|---|---|
| Accidental listener from host flags/config | Strict parser; no forwarded args; isolated `run` host; web config sources cleared; explicit `Listen(IPAddress.Loopback, port)` | Must prove using OS socket inspection in CI; hosting behaviour can change on SDK upgrades |
| Host header manipulation / DNS rebinding | Explicit check accepts only `127.0.0.1` and `localhost`, responds 400 otherwise | Local callers with permitted Host may still invoke API |
| Cross-site page drives the loopback app (any website can request `http://127.0.0.1:5080`, and the Host header then matches) | `LoopbackGuard` answers 403 to any request whose `Origin` is not this listener's own origin, including the SignalR negotiate POST and the WebSocket handshake that carry a Blazor circuit | Requests without an Origin header (curl, native clients, other local processes) are still accepted; this is not authentication |
| Method abuse and state change | GET-only API; no write endpoints, shell execution, file access or persistence | DoS still possible; UI interactions consume local resources |
| Cross-origin browser access | No permissive CORS or external browser resources | Read-only public API may still be fetched by some local software |
| Unexpected CLI inputs | Full argument shape checks, length limits, bounded port range, case-sensitive exact grammar | OS command-line size limit, parent process/environment still uncontrolled |
| Static asset path attacks | Use native SDK static asset endpoint mapping; no custom directory browsing/provider | SDK/library CVEs require patching |
| Output/diagnostic leakage | Four-field DTO; JSON stdout purity; generic errors on stderr | Worker/web default logging may expose host-specific operational metadata to local logs |
| Dependency compromise | Pin MudBlazor 9.12.0 and test dependencies; advisory and licence review recorded in [DEPENDENCIES.md](DEPENDENCIES.md); npm lockfile with integrity hashes | Dependencies need review before every release; no SBOM; the .NET runtime must be kept at the latest patch (NuGet audit does not cover it) |

## Deployment rules

- Never bind `0.0.0.0`, use a tunnel or reverse proxy without designing **new** transport, authn/authz, CSRF, TLS, rate limiting and access logging. This is intentionally out of scope.
- Do not assume a personal laptop is single-tenant; localhost allows access from other local identities.
- Do not put secrets in public DTOs, pages, logs or exception responses.
- Do not enable permissive CORS and do not use `MarkupString` or unvalidated HTML derived from user input.
- Stop `run` and `--web` using ordinary host cancellation; use bounded service-supervisor shutdown.
- Copy the complete publish directory. The folder may be made read-only if all runtime outputs are sent elsewhere; the sample writes only to logs.
- Update self-contained framework runtime binaries by **republishing/redeploying** the full folder.

## Security verification

`scripts/smoke_publish.py` copies the publish folder into an unrelated path, runs commands with an unrelated working directory, examines process-owned TCP listening sockets, poisons `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `DOTNET_URLS`, `Kestrel__Endpoints__*` and development environment, and confirms only the selected loopback listener exists. It then checks Host header 400, unsupported method 405, absent route 404, non-sensitive DTO fields, local CSS/JS assets and shutdown. The generated `ProcessAndApiTests` assert similar properties. Manual security review is required before public distribution.

These tests were run on Linux x64 on 12 November 2026; see [VALIDATION_STATUS.md](VALIDATION_STATUS.md) for results and for what has not been run (Windows, GitHub runners). Not covered by any test: the generic 500 response, because the consumer project has no endpoint that throws. See [TESTING.md](TESTING.md) and [RELEASE.md](RELEASE.md) for the proof required before publication.

## Vulnerability reporting

For an unpublished/internal package, report findings privately to the repository maintainer; do not disclose sensitive exploit details publicly before remediation. No organisational vulnerability-reporting email address is invented in this project. Add a maintainer-approved reporting contact before a public NuGet release.

## v0.2 hardening

Web middleware checks the Host *name and explicit port*, rejects a foreign `Origin` with 403 (before this check, a request with `Origin: http://evil.example` opened a Blazor circuit over WebSocket and read the API), returns a generic
500 response rather than an exception page and sends `X-Frame-Options: DENY`,
`X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer` on accepted
requests. The document response also sends `Content-Security-Policy: frame-ancestors 'none'`, set through the Blazor endpoint option so that it agrees with `X-Frame-Options` (the framework default is `'self'`). `/api/v1/status` also sends `Cache-Control: no-store`. The server
header is disabled. This is defence in depth for a **read-only, non-sensitive,
local-only** demonstration; it does not establish authentication. The application
still permits any local process/user to invoke the read-only API, and the web
circuit can consume local resources. A production remote or privileged service
must be separately threat-modelled.

The template packaging now excludes common credential formats and rejects
unrecognised file extensions/symlinks during release verification. It cannot
prove that deliberately embedded secrets in valid source files are absent;
review generated source and `nupkg` contents before publishing. No sandbox
isolation, secret scanning service, browser cross-origin isolation, remote
binding or user authorisation is implied.
