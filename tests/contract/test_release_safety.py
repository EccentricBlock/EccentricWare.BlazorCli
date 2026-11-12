"""Fail-closed local checks for packaging and release automation.

No .NET install or external packages required; they complement, not replace, the full release gate.
"""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import socket
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def load_script(name):
    source = ROOT / 'scripts' / (name + '.py')
    spec = importlib.util.spec_from_file_location(name, source)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


release = load_script('verify_release')
smoke = load_script('smoke_publish')


class ReleaseSafetyTests(unittest.TestCase):
    def test_source_inventory_is_complete(self):
        release.audit_source_tree()
        actual = {p.relative_to(release.TEMPLATE_ROOT).as_posix()
                  for p in release.TEMPLATE_ROOT.rglob('*') if p.is_file()}
        self.assertIn('.template.config/template.json', actual)
        self.assertIn('LICENSE', actual)
        self.assertGreater(len(actual), 15)

    def test_source_inventory_blocks_sensitive_content_and_unknown_extensions(self):
        for name in ('src/App/secrets.json', 'src/App/.env', 'src/App/.env.local',
                     'src/App/tls.pfx', 'src/App/private.pem', 'src/App/obj/binfile.cs',
                     'src/App/bin/thing.cs', 'src/App/node_modules/a.cs',
                     'src/App/private.tar', 'src/App/private.sh',
                     'docs/unreviewed.json', '.template.config/other.json',
                     'src/App/.npmrc'):
            with self.subTest(name=name):
                self.assertFalse(release.is_allowed_template_file(Path(name)))
        for name in ('.template.config/template.json', 'LICENSE', 'README.md',
                     'src/App/Program.cs', 'src/App/app.csproj',
                     'src/App/wwwroot/app.css', 'docs/USAGE.md'):
            with self.subTest(name=name):
                self.assertTrue(release.is_allowed_template_file(Path(name)))

    def test_safe_name_mirrors_template_engine_forms(self):
        for raw, expected in {'DemoApp': 'DemoApp', 'Demo.App': 'Demo.App', 'Demo-CLI': 'Demo_CLI',
                              '1Demo': '_1Demo', 'class': '_class', 'Demo.namespace': 'Demo._namespace',
                              'Class': 'Class', 'Classic': 'Classic', 'my-class': 'my_class',
                              'base.int': '_base._int', 'System': '_System', 'system': '_system',
                              'MudBlazor': '_MudBlazor', 'System.Linq': '_System.Linq',
                              'Demo.System': 'Demo.System', 'System.Foo': 'System.Foo',
                              'Microsoft': 'Microsoft'}.items():
            with self.subTest(raw=raw):
                self.assertEqual(expected, release.safe_name(raw))

    def test_no_json_config_is_allowed_in_the_template(self):
        for name in ('global.json', 'src/App/global.json', 'appsettings.json'):
            with self.subTest(name=name):
                self.assertFalse(release.is_allowed_template_file(Path(name)))

    def test_reserved_name_pattern_round_trips_and_covers_known_collisions(self):
        names = {'System', 'Microsoft.AspNetCore', 'MudBlazor', 'System.Text.Json'}
        self.assertEqual(names, release.reserved_names.listed_names(release.reserved_names.build_pattern(names)))
        form = json.loads((release.TEMPLATE_ROOT / '.template.config/template.json').read_text())
        listed = {n.lower() for n in release.reserved_names.listed_names(form['forms'][release.reserved_names.FORM]['pattern'])}
        for known in ('system', 'mscorlib', 'netstandard', 'mudblazor', 'system.linq', 'microsoft.aspnetcore'):
            self.assertIn(known, listed)
        self.assertNotIn('microsoft', listed)  # no assembly of that exact name exists

    def test_guarded_output_never_deletes_unmanaged_artifacts(self):
        (ROOT / 'artifacts').mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=ROOT / 'artifacts', prefix='safety-test-') as tmp:
            test_path = Path(tmp) / 'unmanaged'
            test_path.mkdir()
            secret = test_path / 'important.txt'
            secret.write_text('preserve')
            with self.assertRaises(AssertionError):
                release.prepare_output(test_path)
            self.assertEqual('preserve', secret.read_text())
            (test_path / release.MARKER).write_text('owned')
            release.prepare_output(test_path)
            self.assertFalse(secret.exists())
            self.assertTrue((test_path / release.MARKER).exists())

    def test_package_has_uniform_glob_and_explicit_template_discovery(self):
        package = ET.parse(release.PACKAGE_PROJECT).getroot()
        self.assertEqual('0.2.0', package.findtext('.//Version'))
        self.assertEqual('Template', package.findtext('.//PackageType'))
        self.assertEqual('README.md', package.findtext('.//PackageReadmeFile'))
        for item in package.findall('.//None'):
            self.assertEqual('', item.attrib['PackagePath'], item.attrib['Include'])  # directory form: root of package
        entries = package.findall('.//Content')
        self.assertEqual(2, len(entries))
        main = next(x for x in entries if x.attrib['Include'] == 'content/**/*')
        # A directory-form PackagePath keeps extensionless files such as LICENSE; a file-form path with
        # no extension is treated by NuGet as a directory and packs LICENSE/LICENSE.
        self.assertEqual('content/', main.attrib['PackagePath'])
        self.assertIn('content/**/bin/**', main.attrib['Exclude'])
        self.assertIn('content/**/*.pfx', main.attrib['Exclude'])
        self.assertIn('content/**/.template.config/**', main.attrib['Exclude'])
        explicit = next(x for x in entries if '.template.config' in x.attrib['Include'])
        self.assertEqual('content/EccentricWare.BlazorCli.TemplateApp/.template.config/template.json',
                         explicit.attrib['PackagePath'])

    def test_copied_publish_has_posix_readonly_permission_smoke(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / 'publish'
            folder = root / 'wwwroot'
            folder.mkdir(parents=True)
            css = folder / 'app.css'
            css.write_text('body {}')
            folders = smoke.set_copy_readonly(root)
            try:
                self.assertTrue(css.exists())
                if os.name == 'posix':
                    self.assertEqual(root.stat().st_mode & 0o222, 0)
                    self.assertEqual(css.stat().st_mode & 0o222, 0)
            finally:
                smoke.restore_copy_permissions(root, folders)
            if os.name == 'posix':
                self.assertNotEqual(root.stat().st_mode & 0o200, 0)

    @unittest.skipUnless(sys.platform.startswith('linux'), 'Linux /proc socket ownership contract')
    def test_socket_inventory_detects_own_listener(self):
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            sock.listen()
            port = sock.getsockname()[1]
            listeners = smoke.socket_inventory(os.getpid())
            self.assertIn(('127.0.0.1', port), listeners)

    def test_web_source_contains_explicit_error_sanitisation_and_headers(self):
        app = release.TEMPLATE_ROOT / 'src/EccentricWare.BlazorCli.TemplateApp'
        hosting = (app / 'Bootstrap/Hosting.cs').read_text()
        api = (app / 'Web/ApiEndpoints.cs').read_text()
        self.assertIn('app.UseExceptionHandler(', hosting)
        self.assertIn('An internal error occurred.', hosting)
        self.assertIn('StatusCodes.Status500InternalServerError', hosting)
        guard = (app / 'Web/LoopbackGuard.cs').read_text()
        self.assertIn('host.Port', guard)
        self.assertIn('Origin', guard)
        for header in ('X-Content-Type-Options', 'X-Frame-Options', 'Referrer-Policy'):
            self.assertIn(header, guard)
        self.assertIn('Cache-Control', api)
        self.assertIn('no-store', api)

    def test_generated_tests_do_not_hard_code_a_version(self):
        # Consumers change <Version>; a literal in their generated tests would break on the first bump.
        test_root = release.TEMPLATE_ROOT / 'tests/EccentricWare.BlazorCli.TemplateApp.Tests'
        for source in test_root.glob('*.cs'):
            self.assertNotIn('"0.2.0"', source.read_text(), source.name)

    def test_browser_suite_has_source_and_published_configuration(self):
        script = (ROOT / 'scripts/verify_release.py').read_text()
        config = (ROOT / 'tests/browser/playwright.config.mjs').read_text()
        self.assertIn("for mode in ('source', 'publish')", script)
        self.assertIn('EW_PUBLISHED_DIR', script)
        self.assertIn('EW_PUBLISHED_DIR', config)
        self.assertIn('os.tmpdir()', config)
        self.assertIn('Playwright missing', script)
        self.assertTrue((ROOT / 'tests/browser/package-lock.json').is_file())


if __name__ == '__main__':
    unittest.main()
