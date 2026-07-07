# Archiver.Core.IntegrationTests fixtures

Most tar-family fixtures used by this test project are generated at test-run time — see
`TarBuilder.cs` (raw USTAR bytes, for malicious/edge-case archives) and
`ExternalTarFixtureBuilder.cs` (shells out to the real system `tar.exe`, for compressed variants
tar.exe can create: gz/bz2/xz/zst/lzma). Neither is committed here since both are reproducible in
CI without a binary blob (T-F50).

## `valid.7z`

Committed as a binary fixture because `tar.exe`/libarchive can only **read** 7z, never create it
(empirically confirmed while implementing T-F50 — `tar.exe --format=7zip` doesn't exist; only
`ustar|pax|cpio|shar` are valid `--format` values for writing). Built via NanaZip's bundled 7-Zip-
compatible CLI (already used for the same purpose in T-F85's manual verification):

```
echo "hello from a real 7z fixture" > seven.txt
NanaZipC.exe a valid.7z seven.txt
```

Single entry `seven.txt`, content `"hello from a real 7z fixture\n"` (29 bytes, LF line ending).
Used by `TarProcessServiceExternalFormatsTests.ExtractAsync_Valid7z_ExtractsFileWithContent`,
gated `[SkipIfFormatUnsupported("7z")]`.

## `valid.rar`

Committed as a binary fixture for the same reason as `valid.7z` — `tar.exe`/libarchive can only
**read** RAR, never create it (proprietary format writer, owned by RARLAB). Previously undocumented
gap (T-F85/T-F86/T-F49); closed by installing WinRAR's official console `Rar.exe` (via
`winget install RARLab.WinRAR`, 40-day trial) purely to generate this one fixture, then
uninstalling it — no RAR-writing tool is used at runtime or shipped with Pakko, this is a one-off
fixture-generation step same as `valid.7z`'s `NanaZipC.exe` use above.

```
echo "hello from a real rar fixture" > rar.txt
"C:\Program Files\WinRAR\Rar.exe" a -ep1 valid.rar rar.txt
```

Single entry `rar.txt`, content `"hello from a real rar fixture\n"` (30 bytes, LF line ending).
Used by `TarProcessServiceExternalFormatsTests.ExtractAsync_ValidRar_ExtractsFileWithContent`,
gated `[SkipIfFormatUnsupported("rar")]`. RAR *routing* logic (pre-existing, unrelated to this
fixture) is also unit-tested elsewhere via magic-byte-crafted fakes (`ExtractionRouterTests`).
