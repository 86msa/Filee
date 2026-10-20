#!/usr/bin/env python3
"""Exercise the actual packaged CLI, without a .NET SDK, through Arabic PDF, text and PNG conversions."""
import argparse
import json
import os
import pathlib
import subprocess

SAMPLES = [
    'السلام عليكم ورحمة الله وبركاته',
    'الجمهورية العراقية – وزارة التربية',
    'المبلغ الإجمالي: ١٢٥٬٠٠٠ دينار عراقي',
    'Invoice رقم 2026/125 – بغداد',
    'بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ',
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cli', type=pathlib.Path, required=True)
    parser.add_argument('--work', type=pathlib.Path, required=True)
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    env = {**os.environ, 'FILEE_DATA_DIR': str(args.work.resolve() / 'state')}
    input_file = args.work / 'ملف عربي (Filee 2026).txt'
    input_file.write_text('\n\n'.join(SAMPLES), encoding='utf-8')

    def convert(source, target):
        result = subprocess.run([str(args.cli.resolve()), 'convert', str(source.resolve()), '--to', target,
                                 '-o', str((args.work / target).resolve()), '--json'],
                                env=env, check=True, capture_output=True, text=True, encoding='utf-8', timeout=120)
        report = json.loads(result.stdout)
        (args.work / f'{target}-result.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        if report['failed'] or report['converted'] != 1:
            raise RuntimeError(report)
        outputs = [pathlib.Path(p) for file in report['files'] for p in file.get('outputs', [])]
        if not outputs or any(not p.is_file() or p.stat().st_size == 0 for p in outputs):
            raise RuntimeError('The packaged CLI reported missing/empty output.')
        return outputs[0]

    pdf = convert(input_file, 'pdf')
    text = convert(pdf, 'txt').read_text(encoding='utf-8-sig')
    for sample in SAMPLES:
        if sample not in text:
            raise RuntimeError(f'Arabic PDF round trip changed Unicode: {sample!r}; got {text!r}')
    image = convert(pdf, 'png')
    if not image.read_bytes().startswith(b'\x89PNG\r\n\x1a\n'):
        raise RuntimeError('The PDF renderer did not write a PNG.')
    print('Packaged CLI: Arabic filename, searchable text round trip and PDF rendering passed.')


if __name__ == '__main__':
    main()
