# Pakko — Code Signing Policy

> **Status: draft, prepared for the SignPath Foundation application (T-F124/T-F10).** Linked from
> `README.md`; not yet submitted as part of an actual application. See `TASKS.md`'s T-F124/T-F10
> entries for the full plan.

Pakko's release binaries — the WinUI 3 desktop app's MSIX package (`Archiver.App`, bundling the
satellite `Archiver.Shell.exe` and the `Archiver.ShellExtension.dll` COM shell extension) and the
standalone command-line tool `pakko.exe` (published separately via `scripts/Publish-Cli.ps1`) —
are code-signed via [SignPath.io](https://signpath.io), using a certificate provided by
[SignPath Foundation](https://signpath.org) for qualifying open source projects.

## Artifacts covered

| Artifact | Produced by |
|---|---|
| `Archiver.App` MSIX package | `dotnet publish` via `Deploy.ps1` / `.github/workflows/build.yml` |
| `pakko.exe` (standalone CLI) | `scripts/Publish-Cli.ps1` |

## Build process

Every signed artifact is built from this repository's own source — no third-party or
pre-compiled binaries are ever signed. Builds run through the project's public, auditable GitHub
Actions workflow (`.github/workflows/build.yml`), triggered on tagged releases. Each release is
signed only after an explicit, manual approval step (see "Team & roles" below) — signing is never
fully automatic.

See `SECURITY.md`'s "CI Signing Secret" section for the current interim signing model (a
self-signed `CN=Pakko Dev` dev certificate, pending this SignPath Foundation application) and
`TASKS.md`'s T-F10 entry for the full migration plan once SignPath access is granted.

## Team & roles

Pakko is currently maintained by a single person. Per SignPath Foundation's requirement that
every project define Author/Reviewer/Approver roles — including solo-maintained projects, where
one person holds all three — those roles are:

- **Author** (trusted to modify source code in this repository without additional review):
  Paul R ([@pakkoapp-oss](https://github.com/pakkoapp-oss))
- **Reviewer** (reviews every change proposed by anyone other than the author — e.g. pull
  requests — before it is merged): Paul R ([@pakkoapp-oss](https://github.com/pakkoapp-oss))
- **Approver** (must give explicit approval before any signing request for a release is
  submitted): Paul R ([@pakkoapp-oss](https://github.com/pakkoapp-oss))

All SignPath and GitHub account access for the above uses multi-factor authentication.

## Privacy

Pakko collects no data, makes no network requests, and has no telemetry, analytics, crash
reporting, or update checks. See the full
[Privacy Policy](https://pakkoapp-oss.github.io/pakko/) for details.

## Reporting a concern

Report a signing- or security-related concern via a
[GitHub Security Advisory](https://github.com/pakkoapp-oss/pakko/security/advisories/new) — the
same private-disclosure channel documented in `SECURITY.md`.
