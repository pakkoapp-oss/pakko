/*
 * Pakko — Test Fixture Generator
 * ================================
 *
 * Generates binary test fixtures for Archiver.Core.Tests.
 * Fixtures are committed to the repository and used in integration tests
 * to cover scenarios that cannot be reliably reproduced programmatically
 * (corrupted archives, encrypted ZIPs, path traversal, third-party tool output).
 *
 * ── How to run ────────────────────────────────────────────────────────────────
 *
 *   dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
 *
 * Output is written to:
 *   tests/Archiver.Core.Tests/Fixtures/archives/
 *   tests/Archiver.Core.Tests/Fixtures/files/
 *
 * The generator finds the solution root automatically by walking up from the
 * output directory until it finds a *.sln file.
 *
 * ── When to re-run ────────────────────────────────────────────────────────────
 *
 *   - A new fixture scenario is added to this file
 *   - An existing fixture is found to be incorrect
 *   - MANIFEST.sha256 is out of sync with actual files
 *
 * After re-running: verify MANIFEST.sha256 looks correct, then commit.
 * Manual fixtures (see below) are never overwritten by the generator.
 *
 * ── Manual fixtures (not generated automatically) ─────────────────────────────
 *
 * Some fixtures require external tools. Instructions are in *_MANUAL.txt files
 * in the archives/ directory. Create the file, delete the .txt, commit.
 *
 *   encrypted_aes256.zip     — requires 7-Zip:
 *                              7z a -p"testpassword" -mem=AES256 encrypted_aes256.zip compressible.txt
 *
 *   created_by_7zip.zip      — requires 7-Zip:
 *                              7z a created_by_7zip.zip compressible.txt
 *
 *   created_by_winrar.zip    — requires WinRAR:
 *                              Add → select compressible.txt → ZIP format
 *
 *   created_by_macos.zip     — requires macOS:
 *                              zip -r created_by_macos.zip folder/
 *
 *   pakko_integrity_valid.zip    — generate after T-34: run Pakko to archive compressible.txt
 *   pakko_integrity_tampered.zip — generate after T-34: copy valid, flip one byte in manifest
 *
 * ── How tests reference fixtures ──────────────────────────────────────────────
 *
 *   private static string Archive(string name) =>
 *       Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "archives", name);
 *
 *   private static string FixtureFile(string name) =>
 *       Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "files", name);
 *
 * ── Fixture inventory ─────────────────────────────────────────────────────────
 *
 *   files/
 *     compressible.txt                — repeating text, compresses well
 *     incompressible.bin              — random bytes (seed=42), does not compress
 *     unicode_filename_привіт.txt     — unicode filename and content
 *     readme.txt                      — small generic text file
 *
 *   archives/
 *     valid_single_file.zip           — one entry
 *     valid_multiple_files.zip        — 4 entries, flat
 *     valid_nested_folders.zip        — 6 entries, 3-level nesting
 *     valid_unicode_filenames.zip     — unicode entry names
 *     valid_incompressible_content.zip— ZIP_STORED binary
 *     extract_single_root_folder.zip  — T-14: no double-nesting expected
 *     extract_multiple_root_items.zip — T-14: subfolder expected
 *     extract_single_root_file.zip    — T-14: extract directly
 *     corrupted_entry_data.zip        — CRC mismatch on extract
 *     corrupted_central_directory.zip — EOCD signature mangled
 *     encrypted_zipcrypto.zip         — encryption bit set, T-25 target
 *     zipslip_traversal.zip           — path traversal entries, T-14 target
 *     pakko_integrity_valid.zip       — MANUAL, after T-34
 *     pakko_integrity_tampered.zip    — MANUAL, after T-34
 *     encrypted_aes256.zip            — MANUAL, requires 7-Zip
 *     created_by_7zip.zip             — MANUAL
 *     created_by_winrar.zip           — MANUAL
 *     created_by_macos.zip            — MANUAL
 */

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

var solutionRoot = FindSolutionRoot();
var fixturesRoot = Path.Combine(solutionRoot, "tests", "Archiver.Core.Tests", "Fixtures");
var archivesDir  = Path.Combine(fixturesRoot, "archives");
var filesDir     = Path.Combine(fixturesRoot, "files");

Directory.CreateDirectory(archivesDir);
Directory.CreateDirectory(filesDir);

var generated = new List<(string Name, long Size, string Sha256, string Note)>();
var manual    = new List<(string Name, string Reason)>();

Console.WriteLine("Pakko — Test Fixture Generator");
Console.WriteLine($"Output: {fixturesRoot}");
Console.WriteLine();

// ── Plain files ───────────────────────────────────────────────────────────────

Section("Plain files");

var compressible = Path.Combine(filesDir, "compressible.txt");
var sb = new StringBuilder();
for (int i = 0; i < 200; i++)
{
    sb.AppendLine("Це тестовий файл для перевірки компресії у форматі ZIP.");
    sb.AppendLine("The quick brown fox jumps over the lazy dog.");
    sb.AppendLine("Lorem ipsum dolor sit amet, consectetur adipiscing elit.");
}
WriteText(compressible, sb.ToString());
Record(compressible, "repeating text — compresses well");

var incompressible = Path.Combine(filesDir, "incompressible.bin");
var rng = new Random(42); // fixed seed — reproducible
var randomBytes = new byte[32 * 1024];
rng.NextBytes(randomBytes);
File.WriteAllBytes(incompressible, randomBytes);
Record(incompressible, "random bytes (seed=42) — does not compress");

var unicodeFile = Path.Combine(filesDir, "unicode_filename_привіт.txt");
WriteText(unicodeFile, "Файл з юнікодною назвою для тестування.\nHello, 世界!\n");
Record(unicodeFile, "unicode filename and content");

var readme = Path.Combine(filesDir, "readme.txt");
WriteText(readme, "Pakko test fixture — readme\nLine 2\nLine 3\n");
Record(readme);

// ── Valid archives ────────────────────────────────────────────────────────────

Section("Valid archives");

var singleFile = Path.Combine(archivesDir, "valid_single_file.zip");
using (var zip = ZipFile.Open(singleFile, ZipArchiveMode.Create))
{
    zip.CreateEntryFromContent("document.txt", Repeat("Single file content.\n", 50));
}
Record(singleFile, "one entry");

var multipleFiles = Path.Combine(archivesDir, "valid_multiple_files.zip");
using (var zip = ZipFile.Open(multipleFiles, ZipArchiveMode.Create))
{
    zip.CreateEntryFromContent("file1.txt",   Repeat("Content of file 1\n", 30));
    zip.CreateEntryFromContent("file2.txt",   Repeat("Content of file 2\n", 30));
    zip.CreateEntryFromContent("file3.txt",   Repeat("Content of file 3\n", 30));
    zip.CreateEntryFromContent("report.csv",  Repeat("id,name,value\n1,alpha,100\n2,beta,200\n", 20));
}
Record(multipleFiles, "4 entries, flat");

var nestedFolders = Path.Combine(archivesDir, "valid_nested_folders.zip");
using (var zip = ZipFile.Open(nestedFolders, ZipArchiveMode.Create))
{
    zip.CreateEntryFromContent("root.txt",             "Root level file\n");
    zip.CreateEntryFromContent("docs/readme.txt",      Repeat("Docs readme\n", 20));
    zip.CreateEntryFromContent("docs/manual.txt",      Repeat("Manual content\n", 20));
    zip.CreateEntryFromContent("docs/sub/appendix.txt",Repeat("Appendix\n", 20));
    zip.CreateEntryFromContent("src/main.cs",          Repeat("// C# source\nclass Program {}\n", 15));
    zip.CreateEntryFromContent("src/utils.cs",         Repeat("// Utils\nstatic class Utils {}\n", 15));
}
Record(nestedFolders, "6 entries, 3-level nesting");

var unicodeNames = Path.Combine(archivesDir, "valid_unicode_filenames.zip");
using (var zip = ZipFile.Open(unicodeNames, ZipArchiveMode.Create))
{
    zip.CreateEntryFromContent("привіт.txt",          Repeat("Вміст файлу\n", 20));
    zip.CreateEntryFromContent("документи/звіт.txt",  Repeat("Звіт\n", 20));
    zip.CreateEntryFromContent("résumé.txt",          Repeat("CV content\n", 20));
}
Record(unicodeNames, "unicode entry names");

var incompressibleZip = Path.Combine(archivesDir, "valid_incompressible_content.zip");
using (var zip = ZipFile.Open(incompressibleZip, ZipArchiveMode.Create))
{
    var entry = zip.CreateEntry("binary.bin", CompressionLevel.NoCompression);
    using var stream = entry.Open();
    var rng2 = new Random(99);
    var buf = new byte[8 * 1024];
    rng2.NextBytes(buf);
    stream.Write(buf);
}
Record(incompressibleZip, "NoCompression — incompressible binary content");

// ── Smart extract scenarios (T-14) ───────────────────────────────────────────

Section("Smart extract scenarios (T-14)");

var singleRootFolder = Path.Combine(archivesDir, "extract_single_root_folder.zip");
using (var zip = ZipFile.Open(singleRootFolder, ZipArchiveMode.Create))
{
    zip.CreateEntryFromContent("project/readme.txt",   Repeat("Project readme\n", 10));
    zip.CreateEntryFromContent("project/src/main.cs",  Repeat("// main\n", 10));
    zip.CreateEntryFromContent("project/src/utils.cs", Repeat("// utils\n", 10));
}
Record(singleRootFolder, "T-14: single root folder 'project/' — no double-nesting expected");

var multipleRootItems = Path.Combine(archivesDir, "extract_multiple_root_items.zip");
using (var zip = ZipFile.Open(multipleRootItems, ZipArchiveMode.Create))
{
    zip.CreateEntryFromContent("readme.txt",      Repeat("Readme\n", 10));
    zip.CreateEntryFromContent("main.cs",         Repeat("// main\n", 10));
    zip.CreateEntryFromContent("assets/icon.txt", "icon placeholder\n");
}
Record(multipleRootItems, "T-14: multiple root items — subfolder expected on extract");

var singleRootFile = Path.Combine(archivesDir, "extract_single_root_file.zip");
using (var zip = ZipFile.Open(singleRootFile, ZipArchiveMode.Create))
    zip.CreateEntryFromContent("report.txt", Repeat("Report content\n", 30));
Record(singleRootFile, "T-14: single root file — extract directly");

// ── Corrupted archives ────────────────────────────────────────────────────────

Section("Corrupted archives");

// Build a valid zip in memory, then flip bytes in compressed data
var corruptedEntryData = Path.Combine(archivesDir, "corrupted_entry_data.zip");
{
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = zip.CreateEntry("file.txt", CompressionLevel.Fastest);
        using var s = entry.Open();
        var data = Encoding.UTF8.GetBytes(Repeat("This content will be corrupted.\n", 40));
        s.Write(data);
    }
    var raw = ms.ToArray();
    // Flip bytes after local file header (signature 4 + fixed fields 26 + filename 8 = 38)
    const int offset = 38;
    for (int i = offset + 10; i < Math.Min(offset + 50, raw.Length); i++)
        raw[i] ^= 0xFF;
    File.WriteAllBytes(corruptedEntryData, raw);
}
Record(corruptedEntryData, "local file data corrupted — CRC mismatch expected on extract");

// Build valid zip, then corrupt end-of-central-directory signature
var corruptedCentralDir = Path.Combine(archivesDir, "corrupted_central_directory.zip");
{
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = zip.CreateEntry("file.txt", CompressionLevel.Fastest);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(Repeat("Central directory will be corrupted.\n", 40)));
    }
    var raw = ms.ToArray();
    // EOCD signature: 50 4B 05 06 — find last occurrence and corrupt
    byte[] eocdSig = [0x50, 0x4B, 0x05, 0x06];
    int idx = FindLastSequence(raw, eocdSig);
    if (idx >= 0)
    {
        raw[idx + 2] = 0xFF;
        raw[idx + 3] = 0xFF;
    }
    File.WriteAllBytes(corruptedCentralDir, raw);
}
Record(corruptedCentralDir, "EOCD signature corrupted — archive unreadable");

// ── Encrypted archives ────────────────────────────────────────────────────────

Section("Encrypted archives");

// Minimal valid ZIP structure with encryption bit (bit 0) set in general purpose flag
// Same detection target as production code in T-25
var encryptedZipCrypto = Path.Combine(archivesDir, "encrypted_zipcrypto.zip");
{
    const string filename = "file.txt";
    var fnBytes = Encoding.ASCII.GetBytes(filename);

    // Local file header
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

    // Local file header signature
    bw.Write(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
    bw.Write((ushort)20);          // version needed
    bw.Write((ushort)0x0001);      // general purpose bit flag — bit 0 = encrypted
    bw.Write((ushort)8);           // compression method (deflate)
    bw.Write((ushort)0);           // last mod time
    bw.Write((ushort)0);           // last mod date
    bw.Write(0x12345678u);         // CRC-32 (fake)
    bw.Write(32u);                 // compressed size
    bw.Write(28u);                 // uncompressed size
    bw.Write((ushort)fnBytes.Length);
    bw.Write((ushort)0);           // extra field length
    bw.Write(fnBytes);
    bw.Write(new byte[32]);        // fake encrypted data

    long localHeaderEnd = ms.Position;

    // Central directory header
    bw.Write(new byte[] { 0x50, 0x4B, 0x01, 0x02 });
    bw.Write((ushort)20);          // version made by
    bw.Write((ushort)20);          // version needed
    bw.Write((ushort)0x0001);      // general purpose bit flag
    bw.Write((ushort)8);           // compression method
    bw.Write((ushort)0);           // mod time
    bw.Write((ushort)0);           // mod date
    bw.Write(0x12345678u);         // CRC-32
    bw.Write(32u);                 // compressed size
    bw.Write(28u);                 // uncompressed size
    bw.Write((ushort)fnBytes.Length);
    bw.Write((ushort)0);           // extra length
    bw.Write((ushort)0);           // comment length
    bw.Write((ushort)0);           // disk start
    bw.Write((ushort)0);           // internal attrs
    bw.Write(0u);                  // external attrs
    bw.Write(0u);                  // local header offset
    bw.Write(fnBytes);

    long cdSize = ms.Position - localHeaderEnd;

    // End of central directory
    bw.Write(new byte[] { 0x50, 0x4B, 0x05, 0x06 });
    bw.Write((ushort)0);           // disk number
    bw.Write((ushort)0);           // disk with CD
    bw.Write((ushort)1);           // entries on this disk
    bw.Write((ushort)1);           // total entries
    bw.Write((uint)cdSize);        // CD size
    bw.Write((uint)localHeaderEnd);// CD offset
    bw.Write((ushort)0);           // comment length

    File.WriteAllBytes(encryptedZipCrypto, ms.ToArray());
}
Record(encryptedZipCrypto, "ZipCrypto encryption flag set — T-25 detection target");

// AES-256 — manual
WriteManual(archivesDir, "encrypted_aes256.zip",
    "7-Zip: 7z a -p\"testpassword\" -mem=AES256 encrypted_aes256.zip ..\\..\\files\\compressible.txt\n" +
    "Password for tests: testpassword");
manual.Add(("encrypted_aes256.zip", "requires 7-Zip CLI"));

// ── Third-party tool archives (manual) ───────────────────────────────────────

Section("Third-party archives (manual instructions)");

WriteManual(archivesDir, "created_by_7zip.zip",
    "7-Zip: 7z a created_by_7zip.zip ..\\..\\files\\compressible.txt");
manual.Add(("created_by_7zip.zip", "requires 7-Zip"));

WriteManual(archivesDir, "created_by_winrar.zip",
    "WinRAR: open WinRAR → Add → select compressible.txt → ZIP format → OK");
manual.Add(("created_by_winrar.zip", "requires WinRAR"));

WriteManual(archivesDir, "created_by_macos.zip",
    "macOS Terminal: zip -r created_by_macos.zip folder/\n" +
    "Transfer the resulting file to this directory.");
manual.Add(("created_by_macos.zip", "requires macOS"));

Console.WriteLine("  --  created_by_7zip.zip, created_by_winrar.zip, created_by_macos.zip  (see *_MANUAL.txt)");

// ── Security fixtures ─────────────────────────────────────────────────────────

Section("Security fixtures");

// ZIP slip — entries with path traversal in name
var zipSlip = Path.Combine(archivesDir, "zipslip_traversal.zip");
{
    using var ms = new MemoryStream();
    // We must write raw bytes — ZipArchive sanitizes entry names
    // Strategy: write valid zip with normal entries, then patch entry names in raw bytes
    // IMPORTANT: placeholder names must be exactly the same byte length as target names
    //   "../traversal_attempt.txt"        = 24 bytes → placeholder = "XXXXXXXXXXXXXXXXXXXX.txt"
    //   "subdir/../../deep_traversal.txt" = 31 bytes → placeholder = "subdir/XXXXXXXXXXXXXXXXXXXX.txt"
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        // Normal entry
        zip.CreateEntryFromContent("normal.txt", "Normal file — should extract fine\n");
        // Placeholders — exact same byte length as the traversal paths below
        zip.CreateEntryFromContent("XXXXXXXXXXXXXXXXXXXX.txt",       "Path traversal attempt — should be blocked\n");
        zip.CreateEntryFromContent("subdir/XXXXXXXXXXXXXXXXXXXX.txt","Deep traversal attempt — should be blocked\n");
    }

    var raw = ms.ToArray();

    // Patch placeholder names to traversal paths (lengths verified above)
    PatchEntryName(raw, "XXXXXXXXXXXXXXXXXXXX.txt",       "../traversal_attempt.txt");
    PatchEntryName(raw, "subdir/XXXXXXXXXXXXXXXXXXXX.txt","subdir/../../deep_traversal.txt");

    File.WriteAllBytes(zipSlip, raw);
}
Record(zipSlip, "ZIP slip: path traversal entries — T-14 ZIP slip protection target");

// ── Pakko integrity fixtures (T-34) ──────────────────────────────────────────

Section("Pakko integrity fixtures (after T-34)");

WriteAfterTask(archivesDir, "pakko_integrity_valid.zip", "T-34",
    "Run Pakko to archive files/compressible.txt.\n" +
    "The resulting ZIP will contain PAKKO-INTEGRITY-V1 manifest in the ZIP comment.\n" +
    "Copy here and rename to pakko_integrity_valid.zip");

WriteAfterTask(archivesDir, "pakko_integrity_tampered.zip", "T-34",
    "1. Copy pakko_integrity_valid.zip\n" +
    "2. Open with hex editor (e.g. HxD)\n" +
    "3. Find 'PAKKO-INTEGRITY-V1' in the file\n" +
    "4. Change one hex digit in any SHA-256 hash on the lines below\n" +
    "5. Save as pakko_integrity_tampered.zip");

manual.Add(("pakko_integrity_valid.zip",    "generate after T-34 implementation"));
manual.Add(("pakko_integrity_tampered.zip", "generate after T-34 implementation"));
Console.WriteLine("  --  pakko_integrity_valid.zip, pakko_integrity_tampered.zip  (after T-34)");

// ── MANIFEST.sha256 ───────────────────────────────────────────────────────────

Section("Writing MANIFEST.sha256");

var manifest = new StringBuilder();
manifest.AppendLine("# Pakko Test Fixtures — SHA-256 Manifest");
manifest.AppendLine("# Generated by Archiver.Core.Tests.GenerateFixtures");
manifest.AppendLine("# DO NOT edit manually — re-run generator if fixtures change");
manifest.AppendLine();
foreach (var (name, _, hash, note) in generated)
    manifest.AppendLine(note.Length > 0 ? $"{hash}  {name}  # {note}" : $"{hash}  {name}");
manifest.AppendLine();
manifest.AppendLine("# Manual fixtures — not generated:");
foreach (var (name, reason) in manual)
    manifest.AppendLine($"# MISSING: {name}  — {reason}");

var manifestPath = Path.Combine(archivesDir, "MANIFEST.sha256");
File.WriteAllText(manifestPath, manifest.ToString(), Encoding.UTF8);
Console.WriteLine($"  OK  MANIFEST.sha256  ({generated.Count} entries)");

// ── Summary ───────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine($"Generated : {generated.Count} fixtures");
Console.WriteLine($"Manual    : {manual.Count} fixtures remaining");
Console.WriteLine();
Console.WriteLine("Manual fixtures needed:");
foreach (var (name, reason) in manual)
    Console.WriteLine($"  - {name}: {reason}");
Console.WriteLine();
Console.WriteLine($"Output    : {fixturesRoot}");

// ── Helpers ───────────────────────────────────────────────────────────────────

static string FindSolutionRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (dir.GetFiles("*.sln").Length > 0)
            return dir.FullName;
        dir = dir.Parent;
    }
    // Fallback: assume running from project root
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",".."));
}

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine($"=== {title} ===");
}

static void WriteText(string path, string content)
    => File.WriteAllText(path, content, Encoding.UTF8);

static string Repeat(string s, int times)
{
    var sb = new StringBuilder(s.Length * times);
    for (int i = 0; i < times; i++) sb.Append(s);
    return sb.ToString();
}

void Record(string path, string note = "")
{
    var info = new FileInfo(path);
    var hash = ComputeSha256(path);
    generated.Add((info.Name, info.Length, hash, note));
    Console.WriteLine($"  OK  {info.Name,-50}  {info.Length,8:N0} bytes  {note}");
}

static string ComputeSha256(string path)
{
    using var sha = SHA256.Create();
    using var fs  = File.OpenRead(path);
    return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
}

static int FindLastSequence(byte[] haystack, byte[] needle)
{
    for (int i = haystack.Length - needle.Length; i >= 0; i--)
    {
        bool found = true;
        for (int j = 0; j < needle.Length; j++)
            if (haystack[i + j] != needle[j]) { found = false; break; }
        if (found) return i;
    }
    return -1;
}

static void PatchEntryName(byte[] raw, string oldName, string newName)
{
    var oldBytes = Encoding.UTF8.GetBytes(oldName);
    var newBytes = Encoding.UTF8.GetBytes(newName);
    if (oldBytes.Length != newBytes.Length)
        throw new InvalidOperationException("Patch names must be same byte length");

    int start = 0;
    while (true)
    {
        int idx = FindSequence(raw, oldBytes, start);
        if (idx < 0) break;
        newBytes.CopyTo(raw, idx);
        start = idx + oldBytes.Length;
    }
}

static int FindSequence(byte[] haystack, byte[] needle, int startAt = 0)
{
    for (int i = startAt; i <= haystack.Length - needle.Length; i++)
    {
        bool found = true;
        for (int j = 0; j < needle.Length; j++)
            if (haystack[i + j] != needle[j]) { found = false; break; }
        if (found) return i;
    }
    return -1;
}

static void WriteManual(string dir, string zipName, string instructions)
{
    var notePath = Path.Combine(dir, zipName + "_MANUAL.txt");
    File.WriteAllText(notePath,
        $"MANUAL FIXTURE REQUIRED\n" +
        $"=======================\n" +
        $"Target file : {zipName}\n\n" +
        $"{instructions}\n\n" +
        $"Delete this .txt file after creating the real .zip\n",
        Encoding.UTF8);
}

static void WriteAfterTask(string dir, string zipName, string task, string instructions)
{
    var notePath = Path.Combine(dir, zipName + $"_AFTER_{task}.txt");
    File.WriteAllText(notePath,
        $"GENERATE AFTER {task} IS IMPLEMENTED\n" +
        $"{new string('=', 40)}\n" +
        $"Target file : {zipName}\n\n" +
        $"{instructions}\n\n" +
        $"Delete this .txt file after creating the real .zip\n",
        Encoding.UTF8);
}

// Extension to create zip entry from string content
static class ZipArchiveExtensions
{
    public static void CreateEntryFromContent(this ZipArchive zip, string entryName, string content,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(entryName, level);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
