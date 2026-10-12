# Archiver.Core.IntegrationTests fixtures

Most tar-family fixtures used by this test project are generated at test-run time — see
`TarBuilder.cs` (raw USTAR bytes, for malicious/edge-case archives) and
`ExternalTarFixtureBuilder.cs` (shells out to the real system `tar.exe`, for compressed variants
tar.exe can create: gz/bz2/xz/zst/lzma). Neither is committed here since both are reproducible in
CI without a binary blob (T-F50).

## `valid.7z`

Committed as a binary fixture so that the reader is tested on another program's archive.
(Until 2026-10-12 this section said `tar.exe --format=7zip` doesn't exist. That was read off
`tar --help`, which lists only `ustar|pax|cpio|shar`; the format works and writes a real 7z —
see `docs/DECISIONS.md`'s T-F342 entry.) Built via NanaZip's bundled 7-Zip-compatible CLI
(already used for the same purpose in T-F85's manual verification):

```
echo "hello from a real 7z fixture" > seven.txt
NanaZipC.exe a valid.7z seven.txt
```

Single entry `seven.txt`, content `"hello from a real 7z fixture\n"` (29 bytes, LF line ending).
Used by `TarProcessServiceExternalFormatsTests.ExtractAsync_Valid7z_ExtractsFileWithContent`,
gated `[SkipIfFormatUnsupported("7z")]`.

## `valid.rar`

Committed as a binary fixture because `tar.exe`/libarchive can only **read** RAR, never create
it (proprietary format writer, owned by RARLAB). Previously undocumented
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

## `encrypted.7z` / `encrypted_headers.7z` / `encrypted.rar` / `encrypted_headers.rar`

T-F113: password-protected fixtures, password `testpassword` in all four, single entry
`entry.txt` (`"hello from an encrypted archive fixture\n"`). `encrypted.*` encrypts only file
*data* (names/sizes still readable via `tar -tf`/`-tvf`); `encrypted_headers.*` encrypts the
archive's own headers too (nothing readable without the password, including the filename).
Built the same one-off way as `valid.7z`/`valid.rar` above (NanaZip's `NanaZipC.exe` for 7z,
WinRAR's console `Rar.exe` for RAR — reinstalled via `winget install RARLab.WinRAR`, used, then
uninstalled again the same round; no RAR-writing tool is used at runtime or shipped with Pakko):

```
echo hello from an encrypted archive fixture > entry.txt
NanaZipC.exe a -ptestpassword -mhe=off encrypted.7z entry.txt
NanaZipC.exe a -ptestpassword -mhe=on encrypted_headers.7z entry.txt
"C:\Program Files\WinRAR\Rar.exe" a -ep1 -ptestpassword encrypted.rar entry.txt
"C:\Program Files\WinRAR\Rar.exe" a -ep1 -hptestpassword encrypted_headers.rar entry.txt
```

Used by `ArchiveFormatDetectorTests` (RAR-only, byte-level `IsEncryptedRar` assertions) and
`TarSandboxedServiceEncryptedFormatsTests` (all four, asserting the clean "password-protected"
message comes back from `ExtractAsync`/`ListEntriesAsync` instead of raw libarchive stderr).
Exact libarchive stderr text and RAR5 byte offsets are recorded in `DECISIONS.md`'s T-F113 entry.
