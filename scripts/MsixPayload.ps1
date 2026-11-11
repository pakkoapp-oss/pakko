#Requires -Version 7.0
# T-F492: what a Native AOT package of Pakko must and must not carry. Dot-sourced by Deploy.ps1
# and CI-Build-Msix.ps1. A .NET 11 toolchain once packaged the whole JIT runtime beside the AOT
# exe, and once compiled the exe without Archiver.Messages' 36 locales; both installed and ran.

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# A uk-UA message from Archiver.Messages, as the AOT exe stores it (the .resources format writes UTF-8).
function Get-LocalizedMessageMarker([string] $RepoRoot) {
    $resx = Join-Path $RepoRoot 'src\Archiver.Messages\Resources\CoreMessages.uk-UA.resx'
    $value = ([xml][System.IO.File]::ReadAllText($resx)).root.data |
        Where-Object { $_.value.Length -ge 30 -and $_.value -notmatch '\{' } |
        Select-Object -First 1 -ExpandProperty value
    if (-not $value) { throw "No uk-UA message without a placeholder in $resx." }
    return [System.Text.Encoding]::UTF8.GetBytes($value)
}

# Latin-1 maps every byte to one char, so an ordinal string search is a byte search (PowerShell
# cannot call the Span overloads).
function Test-ContainsByteSequence([byte[]] $Haystack, [byte[]] $Needle) {
    $latin1 = [System.Text.Encoding]::Latin1
    return $latin1.GetString($Haystack).IndexOf($latin1.GetString($Needle), [System.StringComparison]::Ordinal) -ge 0
}

# Throws when the package (a flat .msix, or one inner .msix of a bundle) is not the native one.
function Assert-NativeAotPayload([System.IO.Compression.ZipArchive] $Package, [byte[]] $LocalizedMarker) {
    foreach ($entry in $Package.Entries) {
        $leaf = $entry.Name
        if ($leaf -in @('coreclr.dll', 'clrjit.dll', 'System.Private.CoreLib.dll') -or
            $leaf.EndsWith('.resources.dll') -or $leaf.EndsWith('.pdb')) {
            throw "The package carries $($entry.FullName) - only the native exes belong in an AOT package (T-F355, T-F492)."
        }
    }
    $app = $Package.GetEntry('Archiver.App.exe')
    if (-not $app) { throw 'The package has no Archiver.App.exe.' }
    $buffer = New-Object System.IO.MemoryStream
    try {
        $stream = $app.Open()
        try { $stream.CopyTo($buffer) } finally { $stream.Dispose() }
        if (-not (Test-ContainsByteSequence $buffer.ToArray() $LocalizedMarker)) {
            throw "Archiver.App.exe has no uk-UA message of Archiver.Messages - its satellite assemblies were not compiled in (T-F492)."
        }
    } finally { $buffer.Dispose() }
}

# Applies Assert-NativeAotPayload to a .msix, or to every inner .msix of a .msixbundle.
function Assert-NativeAotPackageFile([string] $Path, [string] $RepoRoot) {
    $marker = Get-LocalizedMessageMarker $RepoRoot
    $outer = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        if ($Path.EndsWith('.msix')) {
            Assert-NativeAotPayload $outer $marker
            return
        }
        $inners = @($outer.Entries | Where-Object { $_.FullName -like '*.msix' -and $_.FullName -notlike '*scale-*' -and $_.FullName -notlike '*language-*' })
        if ($inners.Count -eq 0) { throw "No architecture .msix inside $Path." }
        foreach ($inner in $inners) {
            $buffer = New-Object System.IO.MemoryStream
            try {
                $innerStream = $inner.Open()
                try { $innerStream.CopyTo($buffer) } finally { $innerStream.Dispose() }
                $buffer.Position = 0
                $innerZip = New-Object System.IO.Compression.ZipArchive($buffer, [System.IO.Compression.ZipArchiveMode]::Read)
                try { Assert-NativeAotPayload $innerZip $marker } finally { $innerZip.Dispose() }
            } finally { $buffer.Dispose() }
        }
    } finally {
        $outer.Dispose()
    }
}
