"""Static checks executable without .NET; these do not replace build/runtime tests."""
import json
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEMPLATE = ROOT / 'src/EccentricWare.BlazorCli.TemplatePack/content/EccentricWare.BlazorCli.TemplateApp'
APP = TEMPLATE / 'src/EccentricWare.BlazorCli.TemplateApp'
PACK = ROOT / 'src/EccentricWare.BlazorCli.TemplatePack/EccentricWare.BlazorCli.TemplatePack.csproj'


class TemplateContractTests(unittest.TestCase):
    def test_template_metadata(self):
        metadata = json.loads((TEMPLATE / '.template.config/template.json').read_text())
        self.assertEqual('EccentricWare.BlazorCli.CSharp', metadata['identity'])
        self.assertEqual('ew-blazorcli', metadata['shortName'])
        # sourceName would rewrite file names and file content with different spellings for names such
        # as Demo-CLI (slnx path Demo_CLI, folder Demo-CLI). One derived symbol keeps both identical.
        self.assertNotIn('sourceName', metadata)
        symbol = metadata['symbols']['safeName']
        self.assertEqual('EccentricWare.BlazorCli.TemplateApp', symbol['replaces'])
        self.assertEqual('EccentricWare.BlazorCli.TemplateApp', symbol['fileRename'])
        self.assertEqual('safeProjectName', symbol['valueTransform'])
        # safe_namespace first, then the C# keyword guard, then the framework/dependency name guard.
        self.assertEqual(['safe_namespace', 'escapeCSharpKeyword', 'escapeReservedAssemblyName'],
                         metadata['forms']['safeProjectName']['steps'])
        for form in ('escapeCSharpKeyword', 'escapeReservedAssemblyName'):
            self.assertEqual('replace', metadata['forms'][form]['identifier'])
        self.assertTrue(metadata['preferNameDirectory'])
        self.assertTrue((TEMPLATE / 'EccentricWare.BlazorCli.TemplateApp.slnx').exists())
        self.assertTrue((TEMPLATE / 'README.md').exists())
        self.assertTrue((TEMPLATE / 'LICENSE').exists())

    def test_versions_and_package(self):
        pack = ET.parse(PACK).getroot()
        app = ET.parse(APP / 'EccentricWare.BlazorCli.TemplateApp.csproj').getroot()
        self.assertEqual('EccentricWare.BlazorCli', pack.findtext('.//PackageId'))
        self.assertEqual('0.2.0', pack.findtext('.//Version'))
        self.assertEqual('Template', pack.findtext('.//PackageType'))
        self.assertEqual('net10.0', app.findtext('.//TargetFramework'))
        # The generated app owns its version; it must not inherit the template's package version.
        self.assertRegex(app.findtext('.//Version'), r'^\d+\.\d+\.\d+$')
        packages = {x.attrib['Include']: x.attrib['Version'] for x in app.findall('.//PackageReference')}
        self.assertEqual({'MudBlazor': '9.12.0'}, packages)
        tests = ET.parse(TEMPLATE / 'tests/EccentricWare.BlazorCli.TemplateApp.Tests/EccentricWare.BlazorCli.TemplateApp.Tests.csproj').getroot()
        # mtp-off keeps the classic VSTest runner: `dotnet test <path>` works from any directory with no
        # global.json. The default xunit.v3 package needs a global.json read from the working directory.
        self.assertEqual({'Microsoft.NET.Test.Sdk': '18.12.0', 'xunit.v3.mtp-off': '4.0.3',
                          'xunit.runner.visualstudio': '4.0.1'},
                         {x.attrib['Include']: x.attrib['Version'] for x in tests.findall('.//PackageReference')})
        self.assertEqual('Exe', tests.findtext('.//OutputType'))  # xunit.v3 test projects must be executables
        self.assertFalse((TEMPLATE / 'global.json').exists())
        browser = json.loads((ROOT / 'tests/browser/package.json').read_text())
        self.assertEqual({'@playwright/test': '1.65.0'}, browser['devDependencies'])
        self.assertEqual((ROOT / 'LICENSE').read_bytes(), (TEMPLATE / 'LICENSE').read_bytes())
        self.assertIn('MIT License', (ROOT / 'LICENSE').read_text())

    def test_slnx_paths_and_project_reference(self):
        solution = ET.parse(TEMPLATE / 'EccentricWare.BlazorCli.TemplateApp.slnx').getroot()
        paths = [p.attrib['Path'] for p in solution.findall('Project')]
        self.assertEqual(2, len(paths))
        for project in paths:
            self.assertTrue((TEMPLATE / project).exists(), project)
        test_project = TEMPLATE / paths[1]
        test_root = ET.parse(test_project).getroot()
        project_ref = test_root.find('.//ProjectReference').attrib['Include']
        self.assertTrue((test_project.parent / project_ref).resolve().exists())

    def test_no_build_outputs_or_external_resources_in_template(self):
        self.assertFalse([p for p in TEMPLATE.rglob('*') if p.is_dir() and p.name in ('bin', 'obj', 'node_modules', '.git')])
        ui = '\n'.join(p.read_text() for p in (APP / 'Components').rglob('*.razor'))
        self.assertNotIn('fonts.googleapis.com', ui)
        self.assertNotIn('https://', ui)
        self.assertIn('InteractiveServerRenderMode(prerender: false)', ui)
        for provider in ('MudThemeProvider', 'MudPopoverProvider', 'MudDialogProvider', 'MudSnackbarProvider'):
            self.assertEqual(1, (APP / 'Components/Layout/MainLayout.razor').read_text().count(f'<{provider}'))

    def test_web_separation_and_security(self):
        parser = (APP / 'Bootstrap/CommandLineParser.cs').read_text()
        host = (APP / 'Bootstrap/Hosting.cs').read_text()
        guard = (APP / 'Web/LoopbackGuard.cs').read_text()
        entry = (APP / 'Bootstrap/ApplicationEntryPoint.cs').read_text()
        self.assertIn('IPAddress.Loopback', host)
        self.assertIn('builder.Configuration.Sources.Clear()', host)
        self.assertIn('Kestrel__Endpoints__', host)
        self.assertIn('StatusCodes.Status400BadRequest', guard)
        self.assertIn('StatusCodes.Status403Forbidden', guard)
        self.assertIn('app.UseLoopbackGuard(port)', host)
        self.assertIn('app.MapStaticAssets()', host)
        self.assertIn('app.UseAntiforgery()', host)
        self.assertIn('AddInteractiveServerComponents()', host)
        self.assertIn('AddMudServices()', host)
        self.assertIn('AppCommand.Web', entry)
        self.assertIn('Host.CreateApplicationBuilder', entry)
        self.assertNotIn('WebApplication.CreateBuilder', entry)
        self.assertIn('args[0] == "--web"', parser)

    def test_docs_and_ci(self):
        required = ('DESIGN.md', 'DEVELOPMENT.md', 'SECURITY.md', 'RELEASE.md',
                    'TESTING.md', 'CONSUMER.md', 'CI.md')
        for doc in required:
            self.assertGreater((ROOT / 'docs' / doc).stat().st_size, 1000, doc)
        self.assertTrue((ROOT / '.github/workflows/ci.yml').is_file())
        self.assertTrue((ROOT / 'tests/browser/dashboard.spec.mjs').is_file())
        self.assertTrue((ROOT / 'scripts/smoke_publish.py').is_file())
        readme = (ROOT / 'README.md').read_text()
        self.assertIn('dotnet new install', readme)
        self.assertIn('dotnet new ew-blazorcli', readme)
        self.assertIn('dotnet publish', readme)


if __name__ == '__main__':
    unittest.main()
