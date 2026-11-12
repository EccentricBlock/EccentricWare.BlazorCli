#!/usr/bin/env python3
"""Names a generated project must not have, and the template.json form that avoids them.

A project (assembly) named after a framework assembly builds but fails at run time with a
TypeLoadException (-n System), and one named after a package it depends on fails to restore
(-n MudBlazor, NU1108). template.json prefixes such names with '_' (case-insensitive, whole name).

The list is data: the managed assemblies of the installed .NET 10 shared frameworks plus the packages
in the generated app's restore closure. Run with --write after upgrading the SDK or a dependency;
scripts/verify_release.py fails when the list in template.json misses an installed name.

    python scripts/reserved_names.py --assets <app>/obj/project.assets.json [--write]
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TEMPLATE_JSON = (ROOT / 'src/EccentricWare.BlazorCli.TemplatePack/content/'
                 'EccentricWare.BlazorCli.TemplateApp/.template.config/template.json')
FORM = 'escapeReservedAssemblyName'
FRAMEWORKS = ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App')


def framework_assemblies() -> set[str]:
    """Managed assembly names from the deps.json of each installed 10.x shared framework."""
    listing = subprocess.run(['dotnet', '--list-runtimes'], capture_output=True, text=True, check=True).stdout
    names: set[str] = set()
    for line in listing.splitlines():
        match = re.match(r'(\S+) (10\.\S+) \[(.+)\]$', line.strip())
        if not match or match.group(1) not in FRAMEWORKS:
            continue
        deps = Path(match.group(3)) / match.group(2) / f'{match.group(1)}.deps.json'
        for target in json.loads(deps.read_text(encoding='utf-8'))['targets'].values():
            for library in target.values():
                names.update(Path(runtime).stem for runtime in library.get('runtime', {}))
    if not names:
        raise SystemExit('No .NET 10 shared framework found; cannot build the reserved-name list.')
    return names


def package_ids(assets: Path) -> set[str]:
    libraries = json.loads(assets.read_text(encoding='utf-8'))['libraries']
    return {key.split('/')[0] for key, value in libraries.items() if value['type'] == 'package'}


def build_pattern(names: set[str]) -> str:
    return '(?i)^(' + '|'.join(re.escape(n) for n in sorted(names, key=str.lower)) + ')$'


def listed_names(pattern: str) -> set[str]:
    """Inverse of build_pattern, so the verifier can compare without re-deriving the regex."""
    inner = pattern[len('(?i)^('):-len(')$')]
    return {part.replace('\\.', '.') for part in inner.split('|')}


def current_form(metadata: dict) -> dict:
    return metadata['forms'][FORM]


def missing_from_template(installed: set[str]) -> set[str]:
    listed = {n.lower() for n in listed_names(current_form(json.loads(TEMPLATE_JSON.read_text()))['pattern'])}
    return {n for n in installed if n.lower() not in listed}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--assets', type=Path, required=True, help="the generated app's obj/project.assets.json")
    parser.add_argument('--write', action='store_true', help='rewrite the form in template.json')
    args = parser.parse_args()
    installed = framework_assemblies() | package_ids(args.assets)
    missing = missing_from_template(installed)
    print(f'{len(installed)} reserved names; {len(missing)} missing from template.json')
    for name in sorted(missing):
        print('  +', name)
    if args.write:
        metadata = json.loads(TEMPLATE_JSON.read_text())
        metadata['forms'][FORM]['pattern'] = build_pattern(installed)
        TEMPLATE_JSON.write_text(json.dumps(metadata, indent=2) + '\n')
        print('template.json updated')
    elif missing:
        sys.exit(1)


if __name__ == '__main__':
    main()
