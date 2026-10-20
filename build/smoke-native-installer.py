#!/usr/bin/env python3
"""Install/run/remove the built DEB or mount/copy/run/remove the DMG on a native runner."""
import argparse
import re
import xml.etree.ElementTree as ET
import json
import os
import pathlib
import plistlib
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent


def run(*args, env=None):
    subprocess.run(args, check=True, env=env, timeout=180)


def exercise(app, cli, work):
    env = {**os.environ, 'FILEE_SMOKE_APP': str(app), 'FILEE_DATA_DIR': str(work / 'state')}
    run(sys.executable, str(ROOT / 'build/smoke-arabic-pdf.py'), '--cli', str(cli), '--work', str(work / 'arabic'), env=env)
    run(sys.executable, str(ROOT / 'build/smoke-launch-app.py'), env=env)
    run(str(app), '--uninstall-cleanup', env=env)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', required=True)
    parser.add_argument('--version', default='')
    parser.add_argument('--packages', type=pathlib.Path, default=pathlib.Path('Releases'))
    args = parser.parse_args()
    args.version = args.version.removeprefix('v') or ET.parse(
        pathlib.Path(__file__).resolve().parent.parent / 'Directory.Build.props').findtext('.//Version')
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?', args.version or ''):
        raise SystemExit('Invalid package version.')
    artifact = args.packages / f'Filee-{args.version}-{args.runtime}'
    diagnostics = pathlib.Path(os.environ.get('RUNNER_TEMP', tempfile.gettempdir())) / f'filee-installed-{args.runtime}'
    diagnostics.mkdir(parents=True, exist_ok=True)
    if sys.platform.startswith('linux'):
        package = artifact.with_name(artifact.name + '.deb').resolve()
        sudo = [] if os.geteuid() == 0 else ['sudo']
        installed = False
        try:
            run(*sudo, 'apt-get', 'install', '-y', str(package))
            installed = True
            run('dpkg-query', '-W', '-f=${Status} ${Architecture} ${Version}\n', 'filee')
            exercise(pathlib.Path('/opt/filee/Filee'), pathlib.Path('/usr/bin/filee'), diagnostics)
        finally:
            if installed:
                run(*sudo, 'apt-get', 'remove', '-y', 'filee')
        if pathlib.Path('/opt/filee/Filee').exists() or pathlib.Path('/usr/bin/filee').exists():
            raise RuntimeError('The DEB uninstaller left its executables installed.')
        print('DEB: installation, Arabic PDF/text/PNG conversion, desktop launch and removal passed.')
    elif sys.platform == 'darwin':
        disk = artifact.with_name(artifact.name + '.dmg').resolve()
        run('hdiutil', 'verify', str(disk))
        result = subprocess.run(['hdiutil', 'attach', '-readonly', '-nobrowse', '-plist', str(disk)],
                                check=True, capture_output=True, timeout=90)
        volumes = [e['mount-point'] for e in plistlib.loads(result.stdout)['system-entities'] if 'mount-point' in e]
        if len(volumes) != 1:
            raise RuntimeError(f'Unexpected DMG volumes: {volumes!r}')
        mount = pathlib.Path(volumes[0])
        app = diagnostics / 'Applications/Filee.app'
        try:
            if app.exists():
                raise RuntimeError(f'The test installation already exists: {app}')
            app.parent.mkdir(parents=True, exist_ok=True)
            run('ditto', str(mount / 'Filee.app'), str(app))
            run('codesign', '--verify', '--deep', '--strict', str(app))
            exercise(app / 'Contents/MacOS/Filee', app / 'Contents/MacOS/filee-cli', diagnostics)
            shutil.rmtree(app)
            if app.exists():
                raise RuntimeError('The DMG test installation was not removed.')
        finally:
            run('hdiutil', 'detach', str(mount))
        print('DMG: verify, mount, app copy, signature integrity, Arabic conversion, native launch and removal passed.')
        print('This local-build test does not verify Gatekeeper acceptance or Apple notarization of a downloaded copy.')
    else:
        raise SystemExit('Run this on Linux or macOS; Windows uses smoke-windows-installer.ps1.')
    report = ROOT / 'TestResults/native-installers' / args.runtime / 'installation.json'
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps({'runtime': args.runtime, 'version': args.version, 'install': 'passed',
                                 'arabic_pdf': 'passed', 'desktop': 'passed', 'uninstall': 'passed',
                                 'notarization_tested': False}, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
