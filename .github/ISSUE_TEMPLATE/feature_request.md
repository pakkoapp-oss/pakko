---
name: Feature request
about: Suggest an idea or improvement for Pakko
title: "[Feature] "
labels: enhancement
assignees: ''
---

## Problem

What are you trying to do that Pakko doesn't currently support?

## Proposed solution

What would you like to happen?

## Scope check

Pakko is deliberately minimal — see `SPEC.md`'s roadmap and non-goals, and
`README.md`'s "Why Not 7-Zip or WinRAR?" section, before requesting a
feature. In particular, out of scope by design:

- In-place archive mutation (delete/rename/update entries in an existing
  archive)
- Third-party compression libraries (7-Zip, WinRAR, or any bundled
  third-party code)
- Encrypted/password-protected archive creation (no ZIP encryption support
  in `System.IO.Compression`)

If your request falls into one of these, it will likely be declined as
out of scope rather than implemented — but feel free to open the issue
anyway if you think the reasoning above doesn't apply to your case.

## Additional context

Anything else — mockups, links to how another tool does this, etc.
