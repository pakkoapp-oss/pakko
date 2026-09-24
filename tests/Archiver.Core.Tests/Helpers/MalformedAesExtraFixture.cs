namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// T-F194: writes a copy of encrypted_aes256.zip whose FIRST local header's WinZip AES (0x9901)
/// extra record declares <paramref name="declaredSize"/> bytes instead of its real 7. Central
/// directory untouched, so ZipFile.OpenRead still accepts the file — only the raw local-header
/// parse (RawZipEntryLocator) sees the lie.
/// </summary>
internal static class MalformedAesExtraFixture
{
    public static string Create(string directory, ushort declaredSize)
    {
        byte[] bytes = File.ReadAllBytes(FixtureHelper.Archive("encrypted_aes256.zip"));
        int nameLength = BitConverter.ToUInt16(bytes, 26);
        int extraLength = BitConverter.ToUInt16(bytes, 28);
        int extraStart = 30 + nameLength;

        int position = extraStart;
        while (BitConverter.ToUInt16(bytes, position) != 0x9901)
        {
            position += 4 + BitConverter.ToUInt16(bytes, position + 2);
            if (position + 4 > extraStart + extraLength)
                throw new InvalidOperationException("Fixture has no local 0x9901 record.");
        }

        BitConverter.GetBytes(declaredSize).CopyTo(bytes, position + 2);
        string path = Path.Combine(directory, $"malformed_aes_extra_{declaredSize}.zip");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
