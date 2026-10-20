#!/usr/bin/env python3
"""Wrap an already built portable archive in a native DEB/DMG. Run on the package's own OS and CPU.

The workflow installs and tests these exact files separately; successful creation is not an installation test.
"""
import argparse
import re
import xml.etree.ElementTree as ET
import hashlib
import os
import pathlib
import platform
import shutil
import subprocess
import tarfile
import tempfile
import time


def run(*args):
    subprocess.run(args, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', required=True, choices=['linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'])
    parser.add_argument('--version', default='')
    parser.add_argument('--packages', type=pathlib.Path, default=pathlib.Path('Releases'))
    args = parser.parse_args()
    args.version = args.version.removeprefix('v') or ET.parse(
        pathlib.Path(__file__).resolve().parent.parent / 'Directory.Build.props').findtext('.//Version')
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?', args.version or ''):
        raise SystemExit('Invalid package version.')
    cpu = 'arm64' if platform.machine().lower() in ('aarch64', 'arm64') else 'x64'
    expected = ('osx' if platform.system() == 'Darwin' else 'linux') + '-' + cpu
    if args.runtime != expected:
        raise SystemExit(f'Build {args.runtime} on its native machine (this is {expected}).')
    name = f'Filee-{args.version}-{args.runtime}'
    archive = args.packages / f'{name}.tar.gz'
    suffix = '.deb' if args.runtime.startswith('linux-') else '.dmg'
    output = (args.packages / (name + suffix)).resolve()
    if output.exists():
        raise SystemExit(f'{output} already exists; use a new version or output directory.')
    with tempfile.TemporaryDirectory(prefix='filee-installer-') as temporary:
        work = pathlib.Path(temporary)
        with tarfile.open(archive) as tar:
            if suffix == '.dmg':
                # Managed assemblies in Contents/MacOS carry code signatures in extended attributes.
                # Python's tar extractor ignores Apple's metadata; BSD tar restores it, like smoke-portable.
                for member in tar.getmembers():
                    tarfile.data_filter(member, str(work))  # validate paths/links before native extraction
                run('/usr/bin/tar', '-xzf', str(archive.resolve()), '-C', str(work))
            else:
                tar.extractall(work, filter='data')
        source = work / name
        if suffix == '.deb':
            deb(source, work / 'deb', output, args.version, cpu)
        else:
            dmg(source, work / 'dmg', output, args.version)
    digest = hashlib.file_digest(output.open('rb'), 'sha256').hexdigest()
    output.with_name(output.name + '-SHA256SUMS.txt').write_text(f'{digest}  {output.name}\n', encoding='ascii')
    print(output)


def deb(source, root, output, version, cpu):
    architecture = 'arm64' if cpu == 'arm64' else 'amd64'
    installed = root / 'opt/filee'
    shutil.copytree(source, installed, symlinks=True)
    # The tar installer manages a different, per-user installation. apt owns the DEB's files.
    for name in ('install.sh', 'uninstall.sh'):
        (installed / name).unlink(missing_ok=True)
    binary = root / 'usr/bin'
    binary.mkdir(parents=True)
    (binary / 'filee').symlink_to('/opt/filee/filee-cli')
    applications = root / 'usr/share/applications'
    applications.mkdir(parents=True)
    (applications / 'com.filee.app.desktop').write_text(
        '[Desktop Entry]\nType=Application\nName=Filee\nGenericName=File converter\n'
        'GenericName[ar]=تحويل الملفات\nComment=Convert files with Filee\n'
        'Exec=/opt/filee/Filee --convert %F\nIcon=filee\nTerminal=false\nCategories=Utility;\n', encoding='utf-8')
    icons = root / 'usr/share/icons/hicolor/256x256/apps'
    icons.mkdir(parents=True)
    shutil.copyfile(source / 'filee.png', icons / 'filee.png')
    documentation = root / 'usr/share/doc/filee'
    documentation.mkdir(parents=True)
    (documentation / 'copyright').write_text(
        (source / 'LICENSE').read_text() + '\n\n' + (source / 'THIRD-PARTY-NOTICES.md').read_text(), encoding='utf-8')
    control = root / 'DEBIAN'
    control.mkdir()
    size = sum(p.stat().st_size for p in installed.rglob('*') if p.is_file()) // 1024
    # Debian's ~ sorts a preview before the corresponding stable release.
    deb_version = version.replace('-', '~', 1)
    (control / 'control').write_text(
        f'Package: filee\nVersion: {deb_version}\nArchitecture: {architecture}\nSection: utils\nPriority: optional\n'
        f'Maintainer: Filee Arabic fork contributors\nInstalled-Size: {size}\n'
        'Homepage: https://github.com/86msa/Filee\n'
        'Depends: ca-certificates, libc6 (>= 2.35), libgcc-s1, libgssapi-krb5-2, '
        'libicu70 | libicu72 | libicu74 | libicu76 | libicu78, libssl3 | libssl3t64, libstdc++6, '
        'tzdata, zlib1g, libfontconfig1, libfreetype6, libx11-6, libice6, libsm6, libxrandr2, '
        'libxkbcommon0, libxi6, libxtst6, libxt6\n'
        'Recommends: fonts-noto-cjk\n'
        'Description: File conversion desktop application with Arabic and RTL support\n'
        ' Self-contained .NET application; rhwp and 7-Zip are bundled.\n'
        ' Other conversion engines are optional. Settings and documents are kept when removed.\n', encoding='utf-8')
    for script in ('postinst', 'postrm'):
        path = control / script
        path.write_text('#!/bin/sh\nset -e\n'
                        'if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q; fi\n'
                        'if command -v gtk-update-icon-cache >/dev/null 2>&1; then '
                        'gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true; fi\nexit 0\n', encoding='ascii')
        path.chmod(0o755)
    # Preserve the executable bits from the tar archive; standard package metadata is world-readable.
    for directory, folders, files in os.walk(root):
        pathlib.Path(directory).chmod(0o755)
        for name in files:
            path = pathlib.Path(directory) / name
            if not path.is_symlink():
                path.chmod(0o755 if path.stat().st_mode & 0o111 else 0o644)
    run('dpkg-deb', '--root-owner-group', '--build', str(root), str(output))
    run('dpkg-deb', '--info', str(output))


def dmg(source, root, output, version):
    root.mkdir()
    run('ditto', str(source / 'Filee.app'), str(root / 'Filee.app'))
    (root / 'Applications').symlink_to('/Applications', target_is_directory=True)
    for name in ('LICENSE', 'THIRD-PARTY-NOTICES.md'):
        shutil.copyfile(source / name, root / name)
    (root / 'INSTALL.txt').write_text(
        f'Filee {version} — Arabic PDF preview\n\n'
        'Drag Filee.app to Applications, then eject this disk.\n'
        'This preview is ad-hoc signed, not Developer ID signed or notarized. macOS may block a downloaded copy.\n'
        'No Gatekeeper or quarantine bypass is included. A notarized release needs Apple signing credentials.\n'
        'To uninstall, run Filee.app/Contents/MacOS/Filee --uninstall-cleanup before removing the app.\n'
        'Settings and converted documents are kept.\n\n'
        'اسحب Filee.app إلى Applications ثم أخرج القرص.\n'
        'هذه نسخة تجريبية غير موثقة لدى Apple؛ قد يمنع macOS تشغيل نسخة محملة.\n'
        'لا تتضمن الحزمة تعطيل حماية النظام. يحتفظ إلغاء التثبيت بالإعدادات والمستندات.\n', encoding='utf-8')
    run('codesign', '--verify', '--deep', '--strict', str(root / 'Filee.app'))
    # DiskImages can report EBUSY transiently on hosted Intel runners. Keep incomplete images private, wait
    # for its helper to release resources, and retry only this specific failure. Other errors fail immediately.
    for attempt in range(4):
        temporary = root.parent / f'image-{attempt}.dmg'
        result = subprocess.run(['/usr/bin/hdiutil', 'create', '-fs', 'HFS+', '-volname', f'Filee {version}',
                                 '-srcfolder', str(root), '-format', 'UDZO', str(temporary)],
                                capture_output=True, text=True, timeout=180)
        print(result.stdout, end='')
        print(result.stderr, end='')
        if result.returncode == 0:
            shutil.move(str(temporary), str(output))
            break
        if 'Resource busy' not in result.stderr or attempt == 3:
            raise subprocess.CalledProcessError(result.returncode, result.args, result.stdout, result.stderr)
        temporary.unlink(missing_ok=True)
        delay = 2 ** (attempt + 1)
        print(f'DiskImages is busy; retrying creation in {delay} seconds.', flush=True)
        time.sleep(delay)
    run('hdiutil', 'verify', str(output))


if __name__ == '__main__':
    main()
