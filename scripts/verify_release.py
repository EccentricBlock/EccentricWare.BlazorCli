#!/usr/bin/env python3
"""v0.2 source-package release gate: installed NuGet template, generated apps,
unit/process tests, copied publishing, and source/published browser interactivity.

Requires .NET 10 SDK, Python >=3.10 and (unless --skip-browser) Playwright
installed in tests/browser with a matching Chromium browser executable.
This script never downloads dependencies itself; .NET restore and npm setup
are explicit prerequisite operations described in docs/TESTING.md.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import time
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parent))
import reserved_names  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_PROJECT = ROOT / 'src/EccentricWare.BlazorCli.TemplatePack/EccentricWare.BlazorCli.TemplatePack.csproj'
PACKAGE_ID = 'EccentricWare.BlazorCli'
PACKAGE_VERSION = '0.2.0'
SOURCE_NAME = 'EccentricWare.BlazorCli.TemplateApp'
TEMPLATE_ROOT = ROOT / 'src/EccentricWare.BlazorCli.TemplatePack/content' / SOURCE_NAME
ARTIFACTS = (ROOT / 'artifacts').resolve()
MARKER = '.eccentricware-verifier-owned'
SENSITIVE_SUFFIXES = {'.pfx', '.p12', '.pem', '.key', '.suo', '.user', '.db', '.sqlite', '.zip', '.nupkg'}
SENSITIVE_NAMES = {'.env', 'secrets.json', 'id_rsa', 'id_ed25519', '.npmrc'}
# Name -> output folder. Each name targets a past or plausible failure: a path with spaces, a final
# segment equal to the root component name (App), a hyphen (file path vs namespace spelling), a
# leading digit, a namespace segment equal to a template folder name (Web), a C# keyword, and a
# framework root namespace, a framework assembly and a dependency's package ID.
GENERATED_NAMES = {'DemoApp': 'Demo Path/DemoApp', 'Demo.App': 'Demo.App', 'Demo-CLI': 'Demo-CLI',
                   '1Demo': '1Demo', 'Acme.Web': 'Acme.Web',
                   # Reserved C# keywords, as a whole name and as a namespace segment, and a name that
                   # collides with a framework root namespace.
                   'class': 'class', 'Demo.namespace': 'Demo.namespace', 'Microsoft': 'Microsoft',
                   # A framework assembly name (TypeLoadException at run time) and an app dependency's
                   # package ID (NU1108 cycle) are prefixed with '_'.
                   'System': 'System', 'MudBlazor': 'MudBlazor',
                   # Single-segment names equal to a template folder or component (Razor usings resolve
                   # relative to the component namespace, so these once failed to compile).
                   'Web': 'Web', 'App': 'App', 'Application': 'Application'}
ALLOWED_EXTENSIONS = {'.cs', '.csproj', '.razor', '.slnx', '.md', '.css', '.svg', '.json'}


CSHARP_KEYWORDS = (
    'abstract as base bool break byte case catch char checked class const continue decimal default delegate '
    'do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int '
    'interface internal is lock long namespace new null object operator out override params private protected '
    'public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw '
    'true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while').split()


def safe_name(name: str) -> str:
    """Mirror template.json: safe_namespace, then '_' before reserved C# keywords and before a name that
    equals a framework assembly or an app dependency (see scripts/reserved_names.py)."""
    cleaned = re.sub(r'[^A-Za-z0-9_.]', '_', name)
    if cleaned[:1].isdigit():
        cleaned = '_' + cleaned
    cleaned = re.sub(r'(?<![A-Za-z0-9_])(' + '|'.join(CSHARP_KEYWORDS) + r')(?![A-Za-z0-9_])', r'_\1', cleaned)
    reserved = json.loads((TEMPLATE_ROOT / '.template.config/template.json').read_text())['forms'][reserved_names.FORM]
    return re.sub(reserved['pattern'], r'_\1', cleaned)


def project_version(project: Path) -> str:
    match = re.search(r'<Version>([^<]+)</Version>', project.read_text(encoding='utf-8'))
    check(match is not None, f'No <Version> in {project}')
    return match.group(1).strip()


def check(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def is_allowed_template_file(relative: Path) -> bool:
    """Fail-closed inventory, not solely a blacklist; inspect new file types."""
    if relative.name in SENSITIVE_NAMES or relative.suffix.lower() in SENSITIVE_SUFFIXES:
        return False
    if relative.as_posix() == 'LICENSE':
        return True
    if any(part in {'bin', 'obj', '.git', '.vs', 'node_modules', 'artifacts', '__pycache__'}
           for part in relative.parts):
        return False
    if relative.suffix.lower() not in ALLOWED_EXTENSIONS:
        return False
    if relative.parts[:1] == ('.template.config',):
        return relative.as_posix() == '.template.config/template.json'
    if relative.suffix == '.json':
        return False  # No other generated JSON config is intentionally in the source baseline.
    return True


def audit_source_tree() -> None:
    files = [file for file in TEMPLATE_ROOT.rglob('*') if file.is_file()]
    check(bool(files), 'Template source is empty')
    for file in TEMPLATE_ROOT.rglob('*'):
        check(not file.is_symlink(), f'Template includes a symlink: {file}')
    invalid = [str(file.relative_to(TEMPLATE_ROOT)) for file in files
               if not is_allowed_template_file(file.relative_to(TEMPLATE_ROOT))]
    check(not invalid, 'Unreviewed or sensitive template content: ' + ', '.join(invalid))
    print(f'PASS: fail-closed template source inventory ({len(files)} files)', flush=True)


def prepare_output(output: Path) -> None:
    output = output.resolve()
    check(output != ARTIFACTS and output.is_relative_to(ARTIFACTS),
          'Output must be below the repository artifacts/ directory')
    check(output != ROOT and output != TEMPLATE_ROOT, 'Output may not be a source directory')
    if output.exists():
        check((output / MARKER).is_file(),
              f'Refusing to delete pre-existing unmanaged directory: {output}')
        shutil.rmtree(output)
    output.mkdir(parents=True)
    (output / MARKER).write_text('Managed by scripts/verify_release.py\n')


def execute(*args: object, cwd: Path = ROOT, env: dict[str, str] | None = None,
            log_file: Path | None = None, expect_success: bool = True) -> int:
    argv = [str(arg) for arg in args]
    print('+ ' + ' '.join(argv), flush=True)
    started = time.monotonic()
    with subprocess.Popen(argv, cwd=str(cwd), env=env,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, encoding='utf-8', errors='replace') as proc:
        if log_file:
            log_file.parent.mkdir(parents=True, exist_ok=True)
        with log_file.open('a', encoding='utf-8') if log_file else open(os.devnull, 'w') as log:
            log.write('\n$ ' + ' '.join(argv) + '\n')
            for line in proc.stdout or []:
                print(line, end='', flush=True)
                log.write(line)
            code = proc.wait()
            log.write(f'Exit: {code}; elapsed: {time.monotonic() - started:.2f}s\n')
    if expect_success and code != 0:
        raise subprocess.CalledProcessError(code, argv)
    return code


def verify_package(nupkg: Path, evidence_dir: Path) -> None:
    check(nupkg.is_file(), f'NuGet package missing: {nupkg}')
    digest = hashlib.sha256(nupkg.read_bytes()).hexdigest()
    (evidence_dir / 'package.sha256').write_text(f'{digest}  {nupkg.name}\n')
    with zipfile.ZipFile(nupkg) as archive:
        names = archive.namelist()
        check(len(names) == len(set(names)), 'NuGet package contains duplicate archive paths')
        base = f'content/{SOURCE_NAME}/'
        expected = {
            base + '.template.config/template.json',
            base + f'{SOURCE_NAME}.slnx',
            base + f'src/{SOURCE_NAME}/{SOURCE_NAME}.csproj',
            base + f'tests/{SOURCE_NAME}.Tests/{SOURCE_NAME}.Tests.csproj',
            base + 'README.md', base + 'LICENSE',
            'LICENSE',
        }
        check(expected.issubset(names), f'Missing package entries: {sorted(expected.difference(names))}')
        for item in names:
            if item.startswith('content/') and not item.startswith(base):
                raise AssertionError(f'Unexpected content outside template root: {item}')
            check(not item.startswith('contentFiles/'), f'Unexpected contentFiles duplication: {item}')
            check('/LICENSE/' not in item, f'Extensionless file packed as a directory: {item}')
            check(not item.endswith(('.dll', '.pdb', '.exe', '.pfx', '.pem', '.nupkg')),
                  f'Compiled/secret item leaked: {item}')
            if item.startswith(base) and not item.endswith('/'):
                check(is_allowed_template_file(Path(item[len(base):])),
                      f'Unreviewed packaged template file: {item}')
        required_source = {base + str(p.relative_to(TEMPLATE_ROOT)).replace('\\', '/')
                           for p in TEMPLATE_ROOT.rglob('*') if p.is_file()}
        check(required_source.issubset(names),
              f'Package silently omitted template files: {sorted(required_source.difference(names))}')
        metadata = json.loads(archive.read(base + '.template.config/template.json'))
        check(metadata['shortName'] == 'ew-blazorcli', 'Incorrect installed shortName')
        check('sourceName' not in metadata, 'sourceName replaces file names and content with different forms; use the safeName symbol')
        symbol = metadata['symbols']['safeName']
        check(symbol['replaces'] == SOURCE_NAME and symbol['fileRename'] == SOURCE_NAME
              and symbol['valueTransform'] == 'safeProjectName', 'Incorrect safeName template symbol')
        pattern = metadata['forms']['escapeCSharpKeyword']['pattern']
        listed = pattern[pattern.index('_])(') + 4:pattern.rindex(')(?!')].split('|')
        check(sorted(listed) == sorted(CSHARP_KEYWORDS), 'template.json keyword list differs from CSHARP_KEYWORDS')
    print('PASS: complete package inventory, metadata and SHA-256', flush=True)


def verify_generated_tree(generated: Path, name: str) -> tuple[Path, Path]:
    check((generated / 'README.md').is_file(), 'Generated README absent')
    for file in generated.rglob('*'):
        check(not file.is_symlink(), f'Generated symlink: {file}')
        check(SOURCE_NAME not in str(file.relative_to(generated)), f'Leftover source path: {file}')
        if file.suffix in ('.cs', '.razor', '.csproj', '.slnx', '.md', '.json') and file.is_file():
            check(SOURCE_NAME.encode() not in file.read_bytes(), f'Leftover placeholder: {file}')
    solutions = list(generated.glob('*.slnx'))
    projects = list(generated.glob('src/*/*.csproj'))
    tests = list(generated.glob('tests/*/*.csproj'))
    check(len(solutions) == len(projects) == len(tests) == 1,
          f'Expected one solution, app and test project for {name}')
    expected = safe_name(name)
    check(solutions[0].stem == projects[0].stem == expected and tests[0].stem == expected + '.Tests',
          f'{name}: solution/project/test names must all be {expected}')
    check(f'src/{expected}/{expected}.csproj' in solutions[0].read_text(),
          f'{name}: solution does not reference the generated project path')
    check('dotnet run' in (generated / 'README.md').read_text(), 'Generated README has no run instructions')
    return solutions[0], projects[0]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-root', type=Path, default=Path('artifacts/verification'))
    parser.add_argument('--skip-browser', action='store_true',
                        help='Development-only partial check; NOT a release verification')
    parser.add_argument('--skip-smoke', action='store_true',
                        help='Development-only partial check; NOT a release verification')
    args = parser.parse_args()
    output = (args.output_root if args.output_root.is_absolute()
              else ROOT / args.output_root).resolve()
    prepare_output(output)
    log = output / 'commands.log'
    audit_source_tree()
    env = os.environ.copy()
    env.update({'DOTNET_CLI_HOME': str(output / 'dotnet-home'),
                'DOTNET_NOLOGO': '1', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
                'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1'})
    Path(env['DOTNET_CLI_HOME']).mkdir()

    execute(sys.executable, '-m', 'unittest', 'discover', '-s', 'tests/contract', '-v',
            env=env, log_file=log)
    execute('dotnet', '--info', env=env, log_file=log)
    packages = output / 'packages'
    execute('dotnet', 'pack', PACKAGE_PROJECT, '-c', 'Release', '-o', packages,
            env=env, log_file=log)
    package = packages / f'{PACKAGE_ID}.{PACKAGE_VERSION}.nupkg'
    verify_package(package, output)
    execute('dotnet', 'new', 'install', str(package), env=env, log_file=log)
    execute('dotnet', 'new', 'list', 'ew-blazorcli', env=env, log_file=log)

    source_app = None
    published = None
    for name, folder in GENERATED_NAMES.items():
        generated = output / 'generated' / folder
        execute('dotnet', 'new', 'ew-blazorcli', '-n', name, '-o', generated,
                env=env, log_file=log)
        solution, project = verify_generated_tree(generated, name)
        # Run from the repository root on purpose: dotnet test must work from any directory.
        execute('dotnet', 'restore', solution, env=env, log_file=log)
        execute('dotnet', 'build', solution, '-c', 'Release', '--no-restore',
                env=env, log_file=log)
        execute('dotnet', 'test', solution, '-c', 'Release', '--no-build',
                env=env, log_file=log)
        if name == 'DemoApp':
            # The reserved-name list is data; a newer SDK or dependency can add names it does not know.
            installed = (reserved_names.framework_assemblies()
                         | reserved_names.package_ids(project.parent / 'obj/project.assets.json'))
            missing = reserved_names.missing_from_template(installed)
            check(not missing, f'template.json reserved-name list is stale ({len(missing)} missing, e.g. '
                  f'{sorted(missing)[:3]}); run: python scripts/reserved_names.py --assets '
                  f'<generated app>/obj/project.assets.json --write')
            source_app = generated
            published = output / 'publish'
            execute('dotnet', 'publish', project, '-c', 'Release', '--self-contained', 'false',
                    '-o', published, env=env, log_file=log)
            if not args.skip_smoke:
                execute(sys.executable, ROOT / 'scripts/smoke_publish.py',
                        '--publish-dir', published, '--assembly', name,
                        '--expected-version', project_version(project),
                        env=env, log_file=log)
        print(f'PASS: generated, compiled and tested {name}', flush=True)

    if not args.skip_browser:
        check(source_app is not None and published is not None, 'Missing browser targets')
        # Exercise BOTH source assets and relocated publish assets in real Chromium.
        relocated = output / 'relocated' / 'app'
        shutil.copytree(published, relocated)
        node_bin = shutil.which('node')
        check(bool(node_bin), 'node is required for browser checks; install Node.js 22')
        browser_cwd = ROOT / 'tests/browser'
        playwright_cli = browser_cwd / 'node_modules/playwright/cli.js'
        check(playwright_cli.is_file(),
              'Playwright missing: run npm install from tests/browser')
        for mode in ('source', 'publish'):
            # Isolated local port per run, avoiding clashes with other development services.
            with socket.socket() as port_probe:
                port_probe.bind(('127.0.0.1', 0))
                port = port_probe.getsockname()[1]
            browser_env = env.copy()
            browser_env['EW_GENERATED_APP'] = str(source_app)
            browser_env['EW_BROWSER_PORT'] = str(port)
            if mode == 'publish':
                browser_env['EW_PUBLISHED_DIR'] = str(relocated)
            execute(node_bin, playwright_cli, 'test',
                    cwd=browser_cwd, env=browser_env, log_file=log)
            print(f'PASS: real browser interaction against {mode} build', flush=True)

    partial = args.skip_smoke or args.skip_browser
    report = {
        'template_version': PACKAGE_VERSION,
        'generated_names': GENERATED_NAMES,
        'package': str(package),
        'package_sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
        'copied_publish_smoke': not args.skip_smoke,
        'source_and_published_browser': not args.skip_browser,
        'release_gate': 'INCOMPLETE' if partial else 'PASS',
        'command_log': str(log),
    }
    (output / 'result.json').write_text(json.dumps(report, indent=2) + '\n')
    print(('PARTIAL (not a release gate)' if partial else 'PASS: full release gate') +
          f'; evidence in {output}', flush=True)


if __name__ == '__main__':
    main()
