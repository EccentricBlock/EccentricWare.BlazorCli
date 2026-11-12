# CI and automation

`.github/workflows/ci.yml` is the reference verification workflow: pull requests and pushes to any branch trigger jobs on `ubuntu-latest` and `windows-latest`. Jobs provision .NET 10, Python and Node.js 22, run the repository static contract checks, pack and install the template, build/test every generated name in `verify_release.GENERATED_NAMES`, and smoke-test a relocated publish directory. Both jobs now provision Chromium and the verifier runs Playwright against the source build and relocated publish folder.

`verify_release.py` uses `DOTNET_CLI_HOME` under its output tree to reduce the chance of polluting an existing user profile; do not point its marker-protected output directory at valuable files. It accepts `--skip-smoke` and `--skip-browser` only for diagnostics, **not** as release evidence. It does not automatically upload/publish any package.

The verifier allocates an available local port for each of its browser runs; direct manual Playwright invocation defaults to port 5087. Workers within a single CI job should be serial. Split runtime integration and browser jobs only if artefacts and ports are coordinated. On Windows, graceful signal delivery differs from Linux and the copied-folder smoke currently uses a process termination rather than a true console Ctrl+C; validate Windows console Ctrl+C separately before claiming identical graceful cancellation semantics.

A generated .NET 10 application does not need Node.js at runtime. Node and Playwright are verification-only dependencies of the template repository. Browser tests run with blocked external requests to prove the dashboard works using only local resources.

Do not promote the `0.2.0` package until the CI workflow has run successfully on real GitHub runners; the workflow file has been written but not yet run on GitHub-hosted runners (it was not executed locally; the verifier it calls was run on Linux x64 only). Windows has not been run at all. Browser dependencies are installed with `npm ci` from the committed `tests/browser/package-lock.json`.
