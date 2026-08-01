#Requires -Version 5.1
<#
.SYNOPSIS
    Creates and installs a self-signed certificate whose Subject matches Pakko's reserved
    Partner Center Publisher identity, for building Store-submission MSIX packages.
.DESCRIPTION
    Setup-DevCert.ps1's "CN=Pakko Dev" certificate is for everyday local sideloading only.
    dotnet publish's MSIX packaging pipeline rewrites the built package's Identity/Publisher
    to match whatever certificate signs it (by PackageCertificateThumbprint) -- so a package
    signed with "CN=Pakko Dev" ships with Publisher="CN=Pakko Dev", not the reserved Partner
    Center identity, and Partner Center rejects it (confirmed 2026-08-01, T-F129).

    This script creates a second, separate certificate whose Subject is literally the
    reserved Publisher string, so a build signed with -Thumbprint <this cert's thumbprint>
    comes out with the correct Identity/Publisher and can still be locally installed and
    smoke-tested before upload (Partner Center/Microsoft re-signs for real distribution --
    the local signature only needs to make packaging/sideloading succeed).

    Run once before building a Store-submission package. Removes any existing certificate
    with the same Subject, generates a new CryptoAPI RSA code-signing certificate, exports
    it as PakkoStore.cer, and installs it into the machine TrustedPeople store.
    Requires elevation -- the script will relaunch itself as Administrator if needed.
.NOTES
    Uses -Provider "Microsoft Strong Cryptographic Provider" to generate a CryptoAPI key
    instead of CNG. SignTool requires a CryptoAPI key to sign MSIX files.
    The printed thumbprint is required by Deploy.ps1's -Thumbprint parameter.
#>

# Relaunch elevated if not already running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)
if (-not $isAdmin) {
    Write-Host "Not running as Administrator - relaunching elevated..." -ForegroundColor Yellow
    $relaunchArgs = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $MyInvocation.MyCommand.Path
    Start-Process powershell -Verb RunAs -ArgumentList $relaunchArgs
    exit
}

# Must match Package.appxmanifest's Identity/Publisher exactly (the reserved Partner Center value).
$subject = 'CN=EF3EC84C-8287-4FC3-BB4F-FCCEBA116BCE'
$cerPath = Join-Path $PSScriptRoot 'PakkoStore.cer'

Write-Host "Removing any existing '$subject' certificates from Cert:\CurrentUser\My..."
Get-ChildItem 'Cert:\CurrentUser\My' |
    Where-Object { $_.Subject -eq $subject } |
    Remove-Item

Write-Host "Removing any existing '$subject' certificates from Cert:\LocalMachine\TrustedPeople..."
Get-ChildItem 'Cert:\LocalMachine\TrustedPeople' |
    Where-Object { $_.Subject -eq $subject } |
    Remove-Item

Write-Host "Generating self-signed certificate (CryptoAPI RSA 2048)..."
$cert = New-SelfSignedCertificate `
    -Subject $subject `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -Type CodeSigningCert `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -Provider 'Microsoft Strong Cryptographic Provider' `
    -FriendlyName 'Pakko Store (local)'

if (-not $cert) {
    Write-Error "Failed to create certificate."
    exit 1
}

Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Write-Host "Certificate exported to: $cerPath"

$store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
    'TrustedPeople', 'LocalMachine'
)
$store.Open('ReadWrite')
$store.Add($cert)
$store.Close()
Write-Host "Certificate installed into Cert:\LocalMachine\TrustedPeople"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Certificate thumbprint (copy this):" -ForegroundColor Cyan
Write-Host " $($cert.Thumbprint)" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Build a Store-submission package with:"
Write-Host "  .\Deploy.ps1 -Thumbprint $($cert.Thumbprint) -SkipVersionBump"
