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

## RAR — not present, not obtainable on this machine

No RAR-capable encoder is installed here (7-Zip/NanaZip can only *read* RAR — proprietary format,
owned by WinRAR). Same gap already documented in T-F85/T-F86/T-F49. RAR *routing* logic is unit-
tested elsewhere (`ExtractionRouterTests`, magic-byte-crafted fakes); a real end-to-end RAR
extraction needs either a real `.rar` sourced elsewhere or a machine with WinRAR installed.
