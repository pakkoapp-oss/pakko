"""Fills a Partner Center listing export from docs/store-listing/<locale>.txt.

Partner Center, submission overview: "Export listing" gives a CSV with one column per language;
this script writes the text rows of that CSV from the files and leaves every other row (images,
trailers) as exported, so the result can go back through "Import listings".

Without --write it only checks the export against the files.
"""
import argparse
import csv
import io
import os
import re
import sys

COPYRIGHT = 'Copyright © 2026 Pakko Contributors'
ICON = 'StoreLogo300x300'  # uploaded in one language; its URL is copied to every column
TEXT = re.compile(r'StoreLogo300x300|CopyrightTrademarkInformation|Description|ShortDescription|ReleaseNotes|Feature\d+|SearchTerm\d+')
FIRST_LANGUAGE_COLUMN = 4


def dump(rows):
    s = io.StringIO(newline='')
    csv.writer(s, lineterminator='\r\n', quoting=csv.QUOTE_MINIMAL).writerows(rows)
    return b'\xef\xbb\xbf' + s.getvalue().encode('utf-8')[:-2]


def read(raw):
    return list(csv.reader(io.StringIO(raw.decode('utf-8-sig'), newline='')))


def listing(path):
    sec, cur = {}, None
    with open(path, encoding='utf-8') as f:
        for line in f.read().split('\n'):
            if line.startswith('[') and line.endswith(']'):
                cur = line[1:-1]
                sec[cur] = []
            elif cur:
                sec[cur].append(line)
    return {k: '\n'.join(v).strip('\n') for k, v in sec.items()}


def fill(rows, files, new_locales, keep_lists):
    h = rows[0]
    en = h.index('en-us')
    for locale in new_locales:
        h.append(locale)
        for r in rows[1:]:
            keep = r[0] in ('Title', 'OverrideLogosForWin10') or r[2].startswith('Relative path')
            r.append(r[en] if keep else '')
    for r in rows[1:]:
        icon = {c for c in r[FIRST_LANGUAGE_COLUMN:] if c} if r[0] == ICON else set()
        assert len(icon) <= 1, icon
        for i in range(FIRST_LANGUAGE_COLUMN, len(h)):
            text = files[h[i]]
            m = re.fullmatch(r'(Feature|SearchTerm)(\d+)', r[0])
            if r[0] == 'Description':
                r[i] = text['Description']
            elif r[0] == 'ShortDescription':
                r[i] = text['Short description']
            elif r[0] == 'ReleaseNotes':
                r[i] = text["What's new in this version"]
            elif r[0] == 'CopyrightTrademarkInformation':
                r[i] = COPYRIGHT
            elif r[0] == ICON and icon:
                r[i] = next(iter(icon))
            elif m and h[i] not in keep_lists:
                lines = text['Product features' if m.group(1) == 'Feature' else 'Search terms'].split('\n')
                n = int(m.group(2))
                r[i] = lines[n - 1] if n <= len(lines) else ''


def verify(old, new, files, new_locales, keep_lists):
    """Checks the written file, read back from disk, against the export and the listing files."""
    assert len(new) == len(old) and all(len(r) == len(new[0]) for r in new)
    en = new[0].index('en-us')
    changed = {}
    for o, n in zip(old[1:], new[1:]):
        assert o[:FIRST_LANGUAGE_COLUMN] == n[:FIRST_LANGUAGE_COLUMN]
        if TEXT.fullmatch(n[0]):
            for i in range(FIRST_LANGUAGE_COLUMN, len(o)):
                if o[i] != n[i]:
                    changed.setdefault(old[0][i], []).append(n[0])
            continue
        assert o[FIRST_LANGUAGE_COLUMN:] == n[FIRST_LANGUAGE_COLUMN:len(o)], n[0]
        for locale in new_locales:
            assert n[new[0].index(locale)] in ('', n[en]), n[0]
    for i, locale in enumerate(new[0][FIRST_LANGUAGE_COLUMN:], FIRST_LANGUAGE_COLUMN):
        text = files[locale]
        col = {r[0]: r[i] for r in new[1:]}
        assert col['CopyrightTrademarkInformation'] == COPYRIGHT
        assert col['Description'] == text['Description'] and col['ShortDescription'] == text['Short description']
        assert col['ReleaseNotes'] == text["What's new in this version"]
        if locale in keep_lists:
            continue
        assert [col[f'Feature{k}'] for k in range(1, 21)] == text['Product features'].split('\n') + [''] * 8
        assert [col[f'SearchTerm{k}'] for k in range(1, 8)] == text['Search terms'].split('\n')
    return changed


def main():
    repo_listing = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'docs', 'store-listing')
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('source', help='the CSV exported from Partner Center')
    parser.add_argument('--output', help='the CSV to write (required with --write)')
    parser.add_argument('--listing-dir', default=repo_listing, help='folder with <locale>.txt (default: docs/store-listing)')
    parser.add_argument('--add-locale', action='append', default=[], metavar='LOCALE',
                        help='a language the export lacks; it gets the title and image rows of en-us (repeatable)')
    parser.add_argument('--keep-lists', action='append', default=[], metavar='LOCALE',
                        help='leave this language\'s features and search terms as exported (repeatable)')
    parser.add_argument('--write', action='store_true', help='write --output; without it only the checks run')
    args = parser.parse_args()
    new_locales = [x.lower() for x in args.add_locale]
    keep_lists = [x.lower() for x in args.keep_lists]

    with open(args.source, 'rb') as f:
        raw = f.read()
    rows = read(raw)
    print('round-trip identical:', dump(rows) == raw)
    files = {f[:-4].lower(): listing(os.path.join(args.listing_dir, f))
             for f in os.listdir(args.listing_dir) if f.endswith('.txt')}
    header = rows[0]
    print('csv langs missing a file:', [c for c in header[FIRST_LANGUAGE_COLUMN:] if c not in files])
    print('files missing a column:', [x for x in files if x not in header and x not in new_locales])
    if not args.write:
        return 0
    if not args.output:
        parser.error('--write needs --output')

    fill(rows, files, new_locales, keep_lists)
    with open(args.output, 'wb') as f:
        f.write(dump(rows))
    with open(args.output, 'rb') as f:
        new = read(f.read())
    changed = verify(read(raw), new, files, new_locales, keep_lists)
    print('written', args.output, len(new[0]) - FIRST_LANGUAGE_COLUMN, 'languages')
    print('icon copied to', sum(1 for v in changed.values() if ICON in v), 'columns')
    print('other cells changed:', {k: [x for x in v if x != ICON] for k, v in changed.items() if v != [ICON]})
    return 0


if __name__ == '__main__':
    sys.exit(main())
