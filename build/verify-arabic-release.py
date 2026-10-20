#!/usr/bin/env python3
"""Fail publication unless every native target passed Arabic content and installer tests."""
import collections
import json
import os
import pathlib
import re
import zipfile
import xml.etree.ElementTree as ET

root = pathlib.Path('TestReports')
runtimes = ('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
report = {'source_run': os.environ['SOURCE_RUN_ID'], 'version': os.environ['PACKAGE_VERSION'],
          'source_commit': os.environ['SOURCE_COMMIT'], 'platforms': {}}
for rid in runtimes:
    name = 'windows-arabic-test-results' if rid == 'win-x64' else 'unix-test-results-' + rid
    directory = root / name
    results = {}
    files = sorted(directory.rglob('*.trx'))
    assert files, 'Missing TRX results for ' + rid
    for file in files:
        for row in ET.parse(file).findall('.//{*}UnitTestResult'):
            key = row.get('testName')
            if key not in results or row.get('startTime', '') > results[key].get('startTime', ''):
                results[key] = row.attrib
    counts = collections.Counter(row.get('outcome') for row in results.values())
    assert counts and set(counts) <= {'Passed', 'NotExecuted'}, (rid, counts)
    arabic = {key: row for key, row in results.items() if 'ArabicPdfTests.' in key}
    assert len(arabic) >= 14 and all(row.get('outcome') == 'Passed' for row in arabic.values()), (rid, arabic)
    installs = list(directory.glob(f'**/native-installers/{rid}/installation.json'))
    assert len(installs) == 1, (rid, installs)
    installed = json.loads(installs[0].read_text(encoding='utf-8-sig'))
    assert installed['runtime'] == rid and installed['version'] == report['version'], installed
    assert all(installed[key] == 'passed' for key in ('install', 'arabic_pdf', 'desktop', 'uninstall')), installed
    images = list(directory.glob('**/arabic-pdf/**/*.png'))
    assert len(images) >= 9, (rid, 'Missing PDF visual artifacts')
    report['platforms'][rid] = {'counts': dict(counts), 'arabic_pdf_passed': len(arabic), 'installation': installed,
                              'skipped': sorted(key for key, row in results.items() if row.get('outcome') == 'NotExecuted')}
pathlib.Path('VALIDATION.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
with zipfile.ZipFile('Screenshots-Arabic.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for file in root.rglob('*.png'):
        if 'arabic-pdf' in file.parts or re.search(r'(^|[-_])ar([-_.]|$)', file.name):
            archive.write(file, file.relative_to(root))
with zipfile.ZipFile('PDF-Arabic-Samples.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for file in root.glob('**/arabic-pdf/**/*'):
        if file.is_file():
            archive.write(file, file.relative_to(root))
