using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Verifies C:\Windows\System32\tar.exe carries a valid Authenticode signature with Microsoft as
/// the signing organization, before every sandboxed launch. Explicitly documented as low-value
/// against a real attacker (TOCTOU between check and launch; anyone able to swap the binary can
/// do worse) — included because it's nearly free, not because it's load-bearing (see TASKS.md's
/// T-F52 entry). Uses WinVerifyTrust for real cryptographic integrity verification (not managed
/// X509Certificate2/X509Certificate.CreateFromSignedFile, which only extracts the embedded
/// certificate blob without verifying it against the file's actual bytes — and would also hit
/// .NET 8's X509Certificate2 constructor obsoletions, SYSLIB0057).
///
/// Accepts either kind of Authenticode signing Windows uses for tar.exe (T-F401): an embedded
/// (PKCS#7-in-PE) signature, as on Windows 11 24H2 and later, or a Windows catalog (.cat) that
/// lists the file's hash, as on Windows 10, Windows 11 up to 23H2 and Windows Server 2022, whose
/// tar.exe has no embedded signature at all. In both cases WinVerifyTrust verifies the signature
/// over the file's bytes, and the Organization is read from the signature that vouched for it:
/// the file's own for an embedded one, the catalog's for a catalog.
/// </summary>
internal static partial class TarSignatureVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    // DRIVER_ACTION_VERIFY: the catalog database of operating system components.
    private static readonly Guid DriverActionVerify = new("F750E6C3-38EE-11D1-85E5-00C04FC295EE");

    // The signer's Organization (O=) attribute — checked rather than the Common Name (CN=), since
    // Microsoft signs system binaries under several different CNs (e.g. "Microsoft Windows") but
    // consistently under this Organization. Confirmed empirically against the real tar.exe on this
    // machine: `CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US`.
    private const string ExpectedOrganization = "Microsoft Corporation";

    // A catalog member is looked up by SHA-256 first; older catalogs list SHA-1 hashes only, and a
    // null algorithm name gives the system default (SHA-1).
    private static readonly string?[] CatalogHashAlgorithms = ["SHA256", null];

    // Large enough for every hash a catalog uses (SHA-512 is 64 bytes).
    private const int MaxHashLength = 64;

    public static bool Verify(string filePath)
        => VerifyEmbedded(filePath) || VerifyCatalog(filePath);

    private static bool VerifyEmbedded(string filePath)
        => VerifyIntegrity(filePath) && VerifySignerOrganization(filePath, CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED);

    private static bool VerifyIntegrity(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };

        IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);
            return RunWinVerifyTrust(WTD_CHOICE_FILE, fileInfoPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    private static bool VerifyCatalog(string filePath)
    {
        SafeFileHandle file;
        try
        {
            file = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        using (file)
        {
            return CatalogHashAlgorithms.Any(algorithm => VerifyCatalogMember(filePath, file, algorithm));
        }
    }

    private static bool VerifyCatalogMember(string filePath, SafeFileHandle file, string? hashAlgorithm)
    {
        Guid subsystem = DriverActionVerify;
        if (!NativeMethods.CryptCATAdminAcquireContext2(out IntPtr catAdmin, ref subsystem, hashAlgorithm, IntPtr.Zero, 0))
            return false;

        try
        {
            byte[] hash = new byte[MaxHashLength];
            uint hashLength = (uint)hash.Length;
            if (!NativeMethods.CryptCATAdminCalcHashFromFileHandle2(catAdmin, file, ref hashLength, hash, 0)
                || hashLength == 0 || hashLength > hash.Length)
            {
                return false;
            }

            IntPtr catInfo = NativeMethods.CryptCATAdminEnumCatalogFromHash(catAdmin, hash, hashLength, 0, IntPtr.Zero);
            if (catInfo == IntPtr.Zero)
                return false;

            try
            {
                string? catalogPath = CatalogFilePath(catInfo);
                return catalogPath is not null
                    && VerifyCatalogIntegrity(catalogPath, filePath, hash, hashLength, catAdmin)
                    && VerifySignerOrganization(catalogPath, CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED);
            }
            finally
            {
                _ = NativeMethods.CryptCATAdminReleaseCatalogContext(catAdmin, catInfo, 0);
            }
        }
        finally
        {
            _ = NativeMethods.CryptCATAdminReleaseContext(catAdmin, 0);
        }
    }

    // CATALOG_INFO: a DWORD cbStruct followed by WCHAR wszCatalogFile[MAX_PATH].
    private static string? CatalogFilePath(IntPtr catInfo)
    {
        const int MaxPath = 260;
        const int Size = sizeof(uint) + MaxPath * sizeof(char);
        IntPtr buffer = Marshal.AllocHGlobal(Size);
        try
        {
            Marshal.WriteInt32(buffer, 0, Size);
            if (!NativeMethods.CryptCATCatalogInfoFromContext(catInfo, buffer, 0))
                return null;
            string path = Marshal.PtrToStringUni(buffer + sizeof(uint), MaxPath);
            int end = path.IndexOf('\0');
            return end > 0 ? path[..end] : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool VerifyCatalogIntegrity(
        string catalogPath, string filePath, byte[] hash, uint hashLength, IntPtr catAdmin)
    {
        IntPtr hashPtr = Marshal.AllocHGlobal((int)hashLength);
        IntPtr catalogInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_CATALOG_INFO>());
        bool structureWritten = false;
        try
        {
            Marshal.Copy(hash, 0, hashPtr, (int)hashLength);
            var catalogInfo = new WINTRUST_CATALOG_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_CATALOG_INFO>(),
                dwCatalogVersion = 0,
                pcwszCatalogFilePath = catalogPath,
                pcwszMemberTag = Convert.ToHexString(hash, 0, (int)hashLength),
                pcwszMemberFilePath = filePath,
                // Optional: the hash computed from the open handle is passed below instead.
                hMemberFile = IntPtr.Zero,
                pbCalculatedFileHash = hashPtr,
                cbCalculatedFileHash = hashLength,
                pcCatalogContext = IntPtr.Zero,
                hCatAdmin = catAdmin,
            };
            Marshal.StructureToPtr(catalogInfo, catalogInfoPtr, fDeleteOld: false);
            structureWritten = true;
            return RunWinVerifyTrust(WTD_CHOICE_CATALOG, catalogInfoPtr);
        }
        finally
        {
            if (structureWritten)
                Marshal.DestroyStructure<WINTRUST_CATALOG_INFO>(catalogInfoPtr);
            Marshal.FreeHGlobal(catalogInfoPtr);
            Marshal.FreeHGlobal(hashPtr);
        }
    }

    private static bool RunWinVerifyTrust(uint unionChoice, IntPtr unionInfo)
    {
        var trustData = new WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            dwUIChoice = WTD_UI_NONE,
            fdwRevocationChecks = WTD_REVOKE_NONE,
            dwUnionChoice = unionChoice,
            pFile = unionInfo,
            dwStateAction = WTD_STATEACTION_VERIFY,
            dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
        };

        Guid action = WintrustActionGenericVerifyV2;
        int result = NativeMethods.WinVerifyTrust(InvalidHandleValue, ref action, ref trustData);

        // WTD_STATEACTION_CLOSE must run regardless of the verify result — hWVTStateData is
        // a documented easy leak otherwise.
        trustData.dwStateAction = WTD_STATEACTION_CLOSE;
        _ = NativeMethods.WinVerifyTrust(InvalidHandleValue, ref action, ref trustData);

        return result == 0; // ERROR_SUCCESS
    }

    private static bool VerifySignerOrganization(string filePath, uint expectedContentFlags)
    {
        const uint CERT_QUERY_OBJECT_FILE = 1;
        const uint CERT_QUERY_FORMAT_FLAG_BINARY = 2;
        const uint CMSG_SIGNER_CERT_INFO_PARAM = 7;
        // CERT_FIND_SUBJECT_CERT = CERT_COMPARE_SUBJECT_CERT(11) << CERT_COMPARE_SHIFT(16).
        // An earlier draft of this constant (0x00070000, CERT_COMPARE_NAME_STR_A instead of
        // CERT_COMPARE_SUBJECT_CERT) misinterpreted the CERT_INFO buffer as a plain string
        // pointer, silently returning CRYPT_E_NOT_FOUND instead of the signer's own certificate —
        // found empirically by testing against the real tar.exe on this machine.
        const uint CERT_FIND_SUBJECT_CERT = 11 << 16;
        const uint CERT_NAME_ATTR_TYPE = 3;

        if (!NativeMethods.CryptQueryObject(
            CERT_QUERY_OBJECT_FILE, filePath,
            expectedContentFlags, CERT_QUERY_FORMAT_FLAG_BINARY, 0,
            out uint encodingType, IntPtr.Zero, IntPtr.Zero,
            out IntPtr certStore, out IntPtr msg, IntPtr.Zero))
        {
            return false;
        }

        try
        {
            if (msg == IntPtr.Zero)
                return false;

            uint certInfoSize = 0;
            if (!NativeMethods.CryptMsgGetParam(msg, CMSG_SIGNER_CERT_INFO_PARAM, 0, IntPtr.Zero, ref certInfoSize))
                return false;

            IntPtr certInfoBuffer = Marshal.AllocHGlobal((int)certInfoSize);
            try
            {
                if (!NativeMethods.CryptMsgGetParam(msg, CMSG_SIGNER_CERT_INFO_PARAM, 0, certInfoBuffer, ref certInfoSize))
                    return false;

                IntPtr certContext = NativeMethods.CertFindCertificateInStore(
                    certStore, encodingType, 0, CERT_FIND_SUBJECT_CERT, certInfoBuffer, IntPtr.Zero);
                if (certContext == IntPtr.Zero)
                    return false;

                try
                {
                    IntPtr oidPtr = Marshal.StringToHGlobalAnsi("2.5.4.10"); // szOID_ORGANIZATION_NAME
                    try
                    {
                        char[] nameBuffer = new char[512];
                        uint written = NativeMethods.CertGetNameStringW(
                            certContext, CERT_NAME_ATTR_TYPE, 0, oidPtr, nameBuffer, (uint)nameBuffer.Length);

                        string organization = written > 1 ? new string(nameBuffer, 0, (int)written - 1) : string.Empty;
                        return string.Equals(organization, ExpectedOrganization, StringComparison.Ordinal);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(oidPtr);
                    }
                }
                finally
                {
                    NativeMethods.CertFreeCertificateContext(certContext);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(certInfoBuffer);
            }
        }
        finally
        {
            NativeMethods.CertCloseStore(certStore, 0);
            if (msg != IntPtr.Zero)
                NativeMethods.CryptMsgClose(msg);
        }
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);

    // CERT_QUERY_CONTENT_FLAG_* = 1 << CERT_QUERY_CONTENT_*. A catalog is a signed CTL, but asked
    // for as a CTL CryptQueryObject returns no message handle (measured, T-F401); asked for as
    // PKCS#7 signed data it returns the message whose signer this class reads.
    private const uint CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED = 1 << 8;
    private const uint CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED = 1 << 10;

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_CHOICE_CATALOG = 2;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_CATALOG_INFO // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public uint cbStruct;
        public uint dwCatalogVersion;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszCatalogFilePath;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberTag;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberFilePath;
        public IntPtr hMemberFile;
        public IntPtr pbCalculatedFileHash;
        public uint cbCalculatedFileHash;
        public IntPtr pcCatalogContext;
        public IntPtr hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("wintrust.dll", SetLastError = true)]
        public static partial int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        [LibraryImport("wintrust.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptCATAdminAcquireContext2(
            out IntPtr phCatAdmin, ref Guid pgSubsystem, string? pwszHashAlgorithm, IntPtr pStrongHashPolicy, uint dwFlags);

        [LibraryImport("wintrust.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptCATAdminCalcHashFromFileHandle2(
            IntPtr hCatAdmin, SafeFileHandle hFile, ref uint pcbHash, [Out] byte[] pbHash, uint dwFlags);

        [LibraryImport("wintrust.dll", SetLastError = true)]
        public static partial IntPtr CryptCATAdminEnumCatalogFromHash(
            IntPtr hCatAdmin, [In] byte[] pbHash, uint cbHash, uint dwFlags, IntPtr phPrevCatInfo);

        [LibraryImport("wintrust.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptCATCatalogInfoFromContext(IntPtr hCatInfo, IntPtr psCatInfo, uint dwFlags);

        [LibraryImport("wintrust.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);

        [LibraryImport("wintrust.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);

        [LibraryImport("crypt32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptQueryObject(
            uint dwObjectType,
            string pvObject,
            uint dwExpectedContentTypeFlags,
            uint dwExpectedFormatTypeFlags,
            uint dwFlags,
            out uint pdwMsgAndCertEncodingType,
            IntPtr pdwContentType,
            IntPtr pdwFormatType,
            out IntPtr phCertStore,
            out IntPtr phMsg,
            IntPtr ppvContext);

        [LibraryImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptMsgGetParam(
            IntPtr hCryptMsg, uint dwParamType, uint dwIndex, IntPtr pvData, ref uint pcbData);

        [LibraryImport("crypt32.dll", SetLastError = true)]
        public static partial IntPtr CertFindCertificateInStore(
            IntPtr hCertStore, uint dwCertEncodingType, uint dwFindFlags, uint dwFindType,
            IntPtr pvFindPara, IntPtr pPrevCertContext);

        [LibraryImport("crypt32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        public static partial uint CertGetNameStringW(
            IntPtr pCertContext, uint dwType, uint dwFlags, IntPtr pvTypePara,
            [Out] char[] pszNameString, uint cchNameString);

        [LibraryImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CertFreeCertificateContext(IntPtr pCertContext);

        [LibraryImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CertCloseStore(IntPtr hCertStore, uint dwFlags);

        [LibraryImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptMsgClose(IntPtr hCryptMsg);
    }
}
