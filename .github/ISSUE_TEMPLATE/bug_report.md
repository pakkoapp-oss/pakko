---
name: Bug report
about: Report something that doesn't work as expected
title: "[Bug] "
labels: bug
assignees: ''
---

**Note on security issues:** if this is a vulnerability (e.g. a crash on a
malformed archive, a path-traversal / ZIP-slip finding, or anything that
could let a crafted archive escape its intended destination), please do
**not** open a public issue — report it privately via
[GitHub Security Advisories](https://github.com/pakkoapp-oss/pakko/security/advisories/new)
instead, per `SECURITY.md`.

## Describe the bug

A clear, concise description of what went wrong.

## Steps to reproduce

1. ...
2. ...
3. ...

## Expected behavior

What you expected to happen instead.

## Environment

- Pakko version (Settings → About, or the installed `.msix` version):
- Windows version (`winver`):
- Component: [ ] GUI app  [ ] Explorer context menu  [ ] `pakko.exe` CLI
- Archive format involved (ZIP / tar / tar.gz / tar.bz2 / tar.xz / tar.zst / 7z / rar):

## Log file / error output

If applicable, attach the relevant portion of
`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\Pakko\logs\pakko.log`
(see `scripts/README.md`/`CLAUDE.md` for how to find `<PackageFamilyName>`),
or the CLI's stderr output. Redact any personal file paths you don't want
to share.

## Additional context

Anything else that might help — screenshots, a sample archive that
reproduces the issue (if it's not sensitive), etc.
