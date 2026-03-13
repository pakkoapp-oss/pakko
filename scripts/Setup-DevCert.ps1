#Requires -Version 5.1
<#
.SYNOPSIS
    Creates and installs a self-signed developer certificate for local Pakko MSIX signing.
.DESCRIPTION
    Run once before using Deploy.ps1. Generates a code-signing certificate,
    exports it as PakkoDev.cer, and installs it into the machine TrustedPeople store.
    Requires elevation — the script will relaunch itself as Administrator if needed.
.NOTES
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

# Generate self-signed code-signing certificate in the current user store
Write-Host "Generating self-signed certificate..."
$cert = New-SelfSignedCertificate `
    -Subject 'CN=Pakko Dev' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -Type CodeSigningCert `
    -KeyUsage DigitalSignature `
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
Write-Host "Pass this thumbprint to Deploy.ps1 with -Thumbprint, or"
Write-Host "omit it and Deploy.ps1 will locate the certificate automatically."
