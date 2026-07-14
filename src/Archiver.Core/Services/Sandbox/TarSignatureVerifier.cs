using System.Runtime.InteropServices;

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
/// Only checks embedded (PKCS#7-in-PE) Authenticode signatures, not Windows catalog-based
/// signing (many smaller system binaries, e.g. notepad.exe, are verified against a separate
/// .cat file instead of an embedded signature — confirmed via
/// <c>Get-AuthenticodeSignature</c>'s <c>SignatureType</c> field). tar.exe carries a real
/// embedded signature (confirmed on this machine), which is the only file this class ever checks,
/// so catalog support is out of scope — do not "fix" a future catalog-signed target by assuming
/// this class already handles it.
/// </summary>
internal static class TarSignatureVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    // The signer's Organization (O=) attribute — checked rather than the Common Name (CN=), since
    // Microsoft signs system binaries under several different CNs (e.g. "Microsoft Windows") but
    // consistently under this Organization. Confirmed empirically against the real tar.exe on this
    // machine: `CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US`.
    private const string ExpectedOrganization = "Microsoft Corporation";

    public static bool Verify(string filePath)
        => VerifyIntegrity(filePath) && VerifySignerOrganization(filePath);

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

            var trustData = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = fileInfoPtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
            };

            Guid action = WintrustActionGenericVerifyV2;
            int result = NativeMethods.WinVerifyTrust(InvalidHandleValue, ref action, ref trustData);

            // WTD_STATEACTION_CLOSE must run regardless of the verify result — hWVTStateData is
            // a documented easy leak otherwise.
            trustData.dwStateAction = WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(InvalidHandleValue, ref action, ref trustData);

            return result == 0; // ERROR_SUCCESS
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    private static bool VerifySignerOrganization(string filePath)
    {
        const uint CERT_QUERY_OBJECT_FILE = 1;
        const uint CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED = 1 << 10;
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
            CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED, CERT_QUERY_FORMAT_FLAG_BINARY, 0,
            out uint encodingType, IntPtr.Zero, IntPtr.Zero,
            out IntPtr certStore, out IntPtr msg, IntPtr.Zero))
        {
            return false;
        }

        try
        {
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
                        var nameBuffer = new char[512];
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
            NativeMethods.CryptMsgClose(msg);
        }
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
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

    private static class NativeMethods
    {
        [DllImport("wintrust.dll", SetLastError = true)]
        public static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptQueryObject(
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

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptMsgGetParam(
            IntPtr hCryptMsg, uint dwParamType, uint dwIndex, IntPtr pvData, ref uint pcbData);

        [DllImport("crypt32.dll", SetLastError = true)]
        public static extern IntPtr CertFindCertificateInStore(
            IntPtr hCertStore, uint dwCertEncodingType, uint dwFindFlags, uint dwFindType,
            IntPtr pvFindPara, IntPtr pPrevCertContext);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint CertGetNameStringW(
            IntPtr pCertContext, uint dwType, uint dwFlags, IntPtr pvTypePara,
            [Out] char[] pszNameString, uint cchNameString);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CertFreeCertificateContext(IntPtr pCertContext);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CertCloseStore(IntPtr hCertStore, uint dwFlags);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptMsgClose(IntPtr hCryptMsg);
    }
}
