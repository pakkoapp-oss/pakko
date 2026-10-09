---
paths:
  - "**/*.cpp"
  - "**/*.h"
  - "**/*.ps1"
  - "**/*.cs"
  - "**/*.resw"
  - "**/*.md"
---

# Non-ASCII text in code and tool parameters

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **Non-ASCII glyphs (ellipsis, em-dash, Cyrillic) in C++/PowerShell string literals**: never write
  the literal character — full rule + `\uXXXX` escape pattern, and its one `/utf-8`
  exception (`Localization.cpp`'s table), are in `CONVENTIONS.md`. Shipped
  three times already (T-F64, T-F76, T-F63) — check every new string literal.
  **Fixing an already-corrupted literal is not exempt:** typing the `\uXXXX` escape as Edit-tool
  replacement text silently re-decodes to the same literal glyph (confirmed T-F105) — the Edit
  reports `old_string`/`new_string` identical instead of erroring. Build the escape from raw char
  codes (`[char]0x5C + "u2026"`) and write via `System.IO.File`/byte-level replacement instead.
  **Not limited to C++/PowerShell:** the same corruption hit C# (an icon-font PUA glyph in
  `ArchiveEntryViewModel.cs`'s `Icon` property, T-F110) and Markdown prose (`TASKS.md`, T-F110) —
  any Edit/Write call whose params contain a raw `\uXXXX` escape or a raw PUA/icon-font glyph
  (Segoe MDL2/Fluent, e.g. codepoint U+E890) risks silent corruption regardless of file type. Use a
  throwaway Python script via the `py` launcher, building the exact bytes with `chr(0xEXXX)`,
  for any edit touching such content.
  **Before concluding a Write/Edit call corrupted non-ASCII text, verify via actual bytes/
  codepoints, not by eyeballing terminal output.** Git Bash's console can visually render
  correctly-encoded UTF-8 (e.g. via `cat -A`, or a Python `print()`) as mangled/replacement-looking
  characters even when the file on disk is byte-perfect — confirmed a false alarm (T-F128) where a
  real ellipsis (U+2026) looked corrupted in `print(repr(...))` output but was proven correct via
  `ord()` on the parsed string. Plain `Write` calls with direct Unicode text (Cyrillic, CJK, RTL)
  for a brand-new file worked correctly across 36 locale files in this harness — the corruption
  risk documented above is real but narrower than "any non-ASCII in a tool param": it's
  specifically Edit `old_string` matching against complex scripts, and literal `\uXXXX` escape
  sequences getting re-decoded, not plain direct-Unicode `Write` calls for new content.
- **Editing `Localization.cpp`'s per-locale table:** an Edit `old_string` containing a full
  complex-script field (confirmed with Devanagari, T-F03) can silently fail to match even though
  `Read` shows it identical to the file — likely invisible normalization variance. Don't retype the
  translated text as a match target; use a `py` script that anchors on the line's ASCII locale tag
  (e.g. finds `{ L"hi-IN",`) and inserts/edits by string index instead.
  **`py -3` heredocs from the Bash tool silently no-op on a `/tmp/...` path** — native Windows
  Python doesn't resolve Git-Bash's `/tmp`, so a script reports success but writes nothing.
  Use a full Windows-style path (e.g. this session's scratchpad dir) instead.
  **Plain `python` (no `py -3`) fails outright via the Bash tool** — exit code ~49, no real
  error text, even for a trivial script. Always invoke `py -3 <script.py>`, never bare `python`.
  **`py -3 -c "..."` one-liners with an embedded Windows backslash path are fragile** — produced
  `SyntaxError: unterminated string literal`. Always write the script to a real `.py` file
  (Write tool or a heredoc to a Windows-style scratchpad path) and run `py -3 script.py`, never a
  `-c` one-liner with a literal Windows path inside it.
