#!/usr/bin/env python3
"""Copied-publish black-box security, CLI, worker, API and asset smoke.

Run on a machine with the .NET 10 ASP.NET Core runtime and a built publish folder.
Uses only Python's standard library. Socket inventory is OS-specific but fail-closed:
Linux uses /proc socket inodes; Windows uses Get-NetTCPConnection.
"""
from __future__ import annotations

import argparse
import stat
import json
import os
import re
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path


def check(condition, message):
    if not condition:
        raise AssertionError(message)


def socket_inventory(pid: int):
    """List (IP, port) for TCP LISTEN sockets owned by the exact process."""
    if sys.platform.startswith('linux'):
        path = Path(f'/proc/{pid}/fd')
        inodes = set()
        for descriptor in path.iterdir():
            try:
                match = re.fullmatch(r'socket:\[(\d+)\]', os.readlink(descriptor))
                if match:
                    inodes.add(match.group(1))
            except (OSError, FileNotFoundError):
                continue
        listeners = []
        for address_file in ('/proc/net/tcp', '/proc/net/tcp6'):
            for line in Path(address_file).read_text().splitlines()[1:]:
                fields = line.split()
                if fields[3] != '0A' or fields[9] not in inodes:
                    continue
                address_hex, port_hex = fields[1].split(':')
                if address_file.endswith('tcp'):
                    address = socket.inet_ntop(socket.AF_INET, bytes.fromhex(address_hex)[::-1])
                else:
                    address = socket.inet_ntop(socket.AF_INET6,
                        b''.join(bytes.fromhex(address_hex[i:i + 8])[::-1] for i in range(0, 32, 8)))
                listeners.append((address, int(port_hex, 16)))
        return listeners
    if sys.platform == 'win32':
        command = f'Get-NetTCPConnection -State Listen -OwningProcess {pid} -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort | ConvertTo-Json -Compress'
        result = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', command],
                                capture_output=True, text=True, timeout=15, check=True)
        data = json.loads(result.stdout) if result.stdout.strip() else []
        if isinstance(data, dict):
            data = [data]
        return [(item['LocalAddress'], int(item['LocalPort'])) for item in data]
    raise RuntimeError(f'Socket inventory is not implemented on {sys.platform}; cannot pass security gate.')


def launch(dll: Path, args: list[str], cwd: Path, env: dict[str, str] | None = None):
    flags = subprocess.CREATE_NEW_PROCESS_GROUP if sys.platform == 'win32' else 0
    output = tempfile.TemporaryFile(mode='w+t', encoding='utf-8')
    proc = subprocess.Popen(['dotnet', str(dll), *args], cwd=cwd, env=env,
                            stdout=output, stderr=subprocess.STDOUT, creationflags=flags)
    return proc, output


def stop(proc, output, label: str):
    try:
        if proc.poll() is None:
            if sys.platform == 'win32':
                # Windows console control handling depends on the parent console.
                proc.terminate()
            else:
                # SIGTERM, as sent by systemd and Docker. A child of a background job inherits an
                # ignored SIGINT and would never stop.
                proc.send_signal(signal.SIGTERM)
            try:
                proc.wait(timeout=12)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=5)
                raise AssertionError(f'{label} failed to stop within 12 seconds')
    finally:
        output.seek(0)
        captured = output.read()
        output.close()
    return captured


def port_available():
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]


def request(url, method='GET', host=None, origin=None):
    headers = {'Host': host} if host else {}
    if origin is not None:
        headers['Origin'] = origin
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(url, method=method, headers=headers)
    try:
        with opener.open(req, timeout=3) as response:
            return response.status, response.headers.get('Content-Type', ''), response.read()
    except urllib.error.HTTPError as exc:
        return exc.code, exc.headers.get('Content-Type', ''), exc.read()


def test_cli(dll, cwd, expected_version=None):
    for args, code in [([], 0), (['--help'], 0), (['--version'], 0),
                       (['status'], 0), (['status', '--json'], 0),
                       (['--web', 'status'], 2), (['--port', '5081'], 2),
                       (['--urls', 'http://0.0.0.0:80'], 2),
                       (['--web', '--port', '0'], 2),
                       (['--web', '--port', '65536'], 2),
                       (['status', '--json', '--json'], 2),
                       (['--', '--web'], 2)]:
        result = subprocess.run(['dotnet', str(dll), *args], cwd=cwd, text=True,
                                capture_output=True, timeout=20)
        check(result.returncode == code, f'CLI {args}: exit {result.returncode} not {code}: {result.stderr}')
        if args == ['--version']:
            check(re.fullmatch(r'\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?', result.stdout.strip()),
                  f'--version is not a semantic version: {result.stdout!r}')
            version = result.stdout.strip()
            check(expected_version is None or version == expected_version,
                  f'--version printed {version!r}, project <Version> is {expected_version!r}')
        if args == ['status', '--json']:
            data = json.loads(result.stdout)
            check(set(data) == {'application', 'version', 'mode', 'status'}, f'API leaked keys: {data.keys()}')
            check(data['mode'] == 'cli', 'Wrong CLI mode')
            check(data['version'] == version, 'status --json and --version disagree')
            check(result.stdout.count('\n') == 1 and result.stderr == '', 'JSON stdout/stderr not pure')
    print('PASS: CLI status/help/version/invalid arguments')


def test_worker(dll, cwd):
    env = os.environ.copy()
    env.update({'ASPNETCORE_URLS': 'http://0.0.0.0:56789',
                'Kestrel__Endpoints__Injected__Url': 'http://0.0.0.0:56790'})
    proc, output = launch(dll, ['run'], cwd, env)
    try:
        time.sleep(1.5)
        check(proc.poll() is None, 'worker exited before cancellation')
        for _ in range(4):
            check(not socket_inventory(proc.pid), f'worker opened TCP listeners: {socket_inventory(proc.pid)}')
            time.sleep(0.2)
    finally:
        log = stop(proc, output, 'worker')
    if os.name == 'posix':
        check(proc.returncode == 0, f'worker exit code {proc.returncode} after SIGTERM, expected 0')
    if sys.platform.startswith('linux'):
        check('Sample worker started' in log and 'Sample worker stopped' in log,
              'Worker did not log start and graceful stop')
    print('PASS: worker runs without listeners and stops')


def test_web(dll, cwd):
    port = port_available()
    env = os.environ.copy()
    env.update({'ASPNETCORE_URLS': 'http://0.0.0.0:56789',
                'ASPNETCORE_HTTP_PORTS': '56800', 'DOTNET_URLS': 'http://0.0.0.0:56801',
                'Kestrel__Endpoints__Injected__Url': 'http://0.0.0.0:56802',
                'ASPNETCORE_ENVIRONMENT': 'Development'})
    proc, output = launch(dll, ['--web', '--port', str(port)], cwd, env)
    endpoint = f'http://127.0.0.1:{port}'
    try:
        deadline = time.monotonic() + 40
        while time.monotonic() < deadline:
            if proc.poll() is not None:
                raise AssertionError(f'web process terminated with exit {proc.returncode}')
            try:
                code, _, body = request(endpoint + '/api/v1/status')
                if code == 200:
                    break
            except (OSError, TimeoutError):
                pass
            time.sleep(0.2)
        else:
            raise TimeoutError('Web app not ready')
        listeners = socket_inventory(proc.pid)
        check(listeners == [('127.0.0.1', port)], f'Unexpected TCP listeners: {listeners}')
        data = json.loads(body)
        check(set(data) == {'application', 'version', 'mode', 'status'}, 'Web API DTO leaked fields')
        check(data['mode'] == 'web', 'Wrong API mode')
        check(request(endpoint + '/api/v1/status', method='POST')[0] == 405, 'POST not rejected')
        check(request(endpoint + '/api/v1/unknown')[0] == 404, 'Unknown route not 404')
        check(request(endpoint + '/', host='rebind.attacker.invalid')[0] == 400, 'Host filtering missing')
        check(request(endpoint + '/', host=f'localhost:{port + 1}')[0] == 400, 'Host port mismatch accepted')
        # A page on another site can reach 127.0.0.1 with a valid Host: Origin must be refused (403).
        for hostile in ('http://evil.invalid', 'null', f'http://127.0.0.1:{port + 1}'):
            check(request(endpoint + '/_blazor/negotiate?negotiateVersion=1', method='POST', origin=hostile)[0] == 403,
                  f'Cross-origin negotiate accepted for Origin {hostile}')
        check(request(endpoint + '/api/v1/status', origin='http://evil.invalid')[0] == 403, 'Cross-origin API read accepted')
        check(request(endpoint + '/_blazor/negotiate?negotiateVersion=1', method='POST', origin=endpoint)[0] == 200,
              'Same-origin negotiate rejected')
        status, content_type, html = request(endpoint + '/')
        check(status == 200 and b'MudBlazor' in html and b'blazor.web' in html, 'Dashboard markup missing')
        check(b'fonts.googleapis.com' not in html and b'cdn.' not in html, 'External browser resources used')
        resources = re.findall(rb'(?:src|href)="([^"]+)"', html)
        resources = [r.decode() for r in resources if b'_content/MudBlazor/' in r or b'app.' in r or b'_framework/blazor.web' in r]
        check(any('MudBlazor' in r and '.css' in r for r in resources), 'MudBlazor CSS reference missing')
        check(any('MudBlazor' in r and '.js' in r for r in resources), 'MudBlazor JS reference missing')
        check(any('blazor.web' in r for r in resources), 'Blazor JS reference missing')
        check(any('app.' in r and '.css' in r for r in resources), 'App CSS reference missing')
        for resource in resources:
            url = endpoint + '/' + resource.lstrip('/')
            response_code, response_type, response_body = request(url)
            check(response_code == 200, f'Missing static resource: {resource} ({response_code})')
            check(response_body and not response_body.lstrip().startswith(b'<!DOCTYPE html>'),
                  f'Static resource was HTML fallback: {resource}')
            if '.css' in resource:
                check('css' in response_type, f'Wrong CSS MIME type: {resource}: {response_type}')
            if '.js' in resource:
                check('javascript' in response_type, f'Wrong JS MIME type: {resource}: {response_type}')
        if sys.platform.startswith('linux'):
            with socket.socket() as sock:
                sock.settimeout(0.5)
                check(sock.connect_ex(('127.0.0.2', port)) != 0, 'Service accepts another loopback address')
    finally:
        stop(proc, output, 'web')
    with socket.socket() as probe:
        probe.settimeout(1)
        check(probe.connect_ex(('127.0.0.1', port)) != 0, 'Web listener survived shutdown')
    print('PASS: copied-folder web listener, API, hostile configuration, host header and assets')


def test_port_conflict(dll, cwd):
    with socket.socket() as listener:
        listener.bind(('127.0.0.1', 0))
        listener.listen()
        port = listener.getsockname()[1]
        process = subprocess.run(['dotnet', str(dll), '--web', '--port', str(port)],
                                 cwd=cwd, capture_output=True, text=True, timeout=30)
        check(process.returncode == 3,
              f'Port-conflict startup exit {process.returncode} rather than 3; stderr={process.stderr}')
    print('PASS: occupied web port returns startup error 3')


def set_copy_readonly(root: Path):
    """On POSIX exercise a deployment whose published directories are not writable.

    Windows ACL checks require platform-specific tooling and are not covered by chmod.
    """
    if os.name != 'posix':
        return []
    dirs = []
    for path in root.rglob('*'):
        if path.is_file():
            path.chmod(stat.S_IRUSR | stat.S_IRGRP | stat.S_IROTH)
        elif path.is_dir():
            dirs.append(path)
    for path in sorted(dirs, key=lambda d: len(d.parts), reverse=True):
        path.chmod(stat.S_IRUSR | stat.S_IXUSR | stat.S_IRGRP | stat.S_IXGRP | stat.S_IROTH | stat.S_IXOTH)
    root.chmod(0o555)
    return dirs


def restore_copy_permissions(root: Path, dirs: list[Path]):
    if os.name != 'posix':
        return
    root.chmod(0o755)
    for path in sorted(dirs, key=lambda d: len(d.parts)):
        path.chmod(0o755)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish-dir', required=True, type=Path)
    parser.add_argument('--assembly', required=True)
    parser.add_argument('--expected-version', help="The generated project's <Version>; --version must print exactly this")
    args = parser.parse_args()
    publish = args.publish_dir.resolve()
    check(publish.is_dir(), f'Publish directory missing: {publish}')
    with tempfile.TemporaryDirectory(prefix='ew-publish-') as temp:
        temp = Path(temp)
        target = temp / 'copied' / 'app'
        shutil.copytree(publish, target)
        dll = target / (args.assembly + '.dll')
        check(dll.exists(), f'Assembly missing from publish: {dll}')
        cwd = temp / 'unrelated-cwd'
        cwd.mkdir()
        changed = set_copy_readonly(target)
        try:
            test_cli(dll, cwd, args.expected_version)
            test_worker(dll, cwd)
            test_web(dll, cwd)
            test_port_conflict(dll, cwd)
        finally:
            restore_copy_permissions(target, changed)
    print('PASS: complete copied-publish smoke suite')


if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        print(f'FAIL: {type(exc).__name__}: {exc}', file=sys.stderr)
        raise
