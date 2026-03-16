#Requires -Version 5.1
<#
.SYNOPSIS
    Creates and installs a self-signed developer certificate for local Pakko MSIX signing.
.DESCRIPTION
    Run once before using Deploy.ps1. Removes any existing CN=Pakko Dev certificate,
    generates a new CryptoAPI RSA code-signing certificate, exports it as PakkoDev.cer,
    and installs it into the machine TrustedPeople store.
    Requires elevation — the script will relaunch itself as Administrator if needed.
.NOTES
    Uses -Provider "Microsoft Strong Cryptographic Provider" to generate a CryptoAPI key
    instead of CNG. SignTool requires a CryptoAPI key to sign MSIX files.
    The printed thumbprint is required by Deploy.ps1.
#>

# Relaunch elevated if not already running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)
if (-not $isAdmin) {
    Write-Host "Not running as Administrator — relaunching elevated..." -ForegroundColor Yellow
    $args = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $MyInvocation.MyCommand.Path
    Start-Process powershell -Verb RunAs -ArgumentList $args
    exit
}

$cerPath = Join-Path $PSScriptRoot 'PakkoDev.cer'

# Remove any existing CN=Pakko Dev certificate from CurrentUser\My
Write-Host "Removing any existing CN=Pakko Dev certificates from Cert:\CurrentUser\My..."
Get-ChildItem 'Cert:\CurrentUser\My' |
    Where-Object { $_.Subject -eq 'CN=Pakko Dev' } |
    Remove-Item

# Remove any existing CN=Pakko Dev certificate from LocalMachine\TrustedPeople
Write-Host "Removing any existing CN=Pakko Dev certificates from Cert:\LocalMachine\TrustedPeople..."
Get-ChildItem 'Cert:\LocalMachine\TrustedPeople' |
    Where-Object { $_.Subject -eq 'CN=Pakko Dev' } |
    Remove-Item

# Generate self-signed CryptoAPI code-signing certificate in the current user store.
# -Provider "Microsoft Strong Cryptographic Provider" forces CryptoAPI (not CNG) so
# SignTool can use the key directly when signing MSIX files.
Write-Host "Generating self-signed certificate (CryptoAPI RSA 2048)..."
$cert = New-SelfSignedCertificate `
    -Subject 'CN=Pakko Dev' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -Type CodeSigningCert `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -Provider 'Microsoft Strong Cryptographic Provider' `
    -FriendlyName 'Pakko Dev (local)'

if (-not $cert) {
    Write-Error "Failed to create certificate."
    exit 1
}

# Export the public certificate (.cer) so it can be inspected or shared
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Write-Host "Certificate exported to: $cerPath"

# Install into LocalMachine\TrustedPeople so Windows trusts the MSIX package
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
Write-Host "New certificate generated. Run Deploy.ps1 to build and install."
