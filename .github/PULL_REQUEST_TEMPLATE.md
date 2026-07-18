## Summary

What does this PR change, and why?

## Related issue / task

Closes #... (if applicable). If this addresses a `T-Fxx` task tracked in
`TASKS.md`, name it here.

## Testing

- [ ] `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` passes
      (run with no path argument, from the repo root — all projects must
      stay green)
- [ ] `dotnet test --filter "Category=Slow"` passes, if this touches
      Zip64-adjacent code or compression/extraction performance
- [ ] `Archiver.ShellExtension.Tests.exe` passes, if this touches
      `Archiver.ShellExtension` (C++, built separately — see
      `CLAUDE.md`'s Build Commands)
- [ ] Manually verified on-device, if this touches shell-triggered or UI
      behavior (context menu, extraction, MSIX packaging) — `dotnet test`
      alone does not cover this; see `CONTRIBUTING.md`

## Checklist

- [ ] I've read `CONTRIBUTING.md` and `CLAUDE.md`'s hard constraints
      relevant to the area I changed
- [ ] No new NuGet packages added to `Archiver.Core` (zero dependencies,
      see `CLAUDE.md`)
- [ ] Docs updated if this changes a public signature, DI wiring, XAML
      structure, a security-relevant behavior, or the roadmap/task list —
      see `CLAUDE.md`'s Documentation Map for which file owns what
