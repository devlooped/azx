#Requires -Version 7
<#
.SYNOPSIS
  Stamp or assert Unix execute bits on Payload launchers inside a nupkg.
#>
param(
    [string] $Nupkg,
    [switch] $Assert,
    [string] $Destination
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Test-UnixExecuteEntry([string] $Name) {
    $n = $Name.Replace('\', '/')
    if ($n.EndsWith('/')) {
        return $false
    }
    $slash = $n.LastIndexOf('/')
    $leaf = if ($slash -lt 0) { $n } else { $n.Substring($slash + 1) }
    if ($leaf -eq 'azx') {
        return $true
    }
    if ($n -eq 'az/bin/az' -or $n.EndsWith('/az/bin/az')) {
        return $true
    }
    if ($leaf.EndsWith('.pyc')) {
        return $false
    }
    return $n -match '(^|/)python/bin/[^/]+$'
}

function Get-ZipUnixMode([int] $ExternalAttributes) {
    return ($ExternalAttributes -shr 16) -band 0xFFFF
}

function Test-ZipUnixExecute([int] $ExternalAttributes) {
    return ((Get-ZipUnixMode $ExternalAttributes) -band [Convert]::ToInt32('111', 8)) -ne 0
}

function Set-NupkgUnixExecuteBits([string] $Path) {
    $unixExec = [Convert]::ToInt32('100755', 8) -shl 16
    $zip = [System.IO.Compression.ZipFile]::Open(
        [System.IO.Path]::GetFullPath($Path),
        [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $stamped = 0
        foreach ($entry in $zip.Entries) {
            if (Test-UnixExecuteEntry $entry.FullName) {
                $entry.ExternalAttributes = $unixExec
                $stamped++
            }
        }
        Write-Host "Stamped $stamped executable entries in $Path"
    }
    finally {
        $zip.Dispose()
    }
}

function Assert-NupkgUnixExecuteBits([string] $Path) {
    $name = [System.IO.Path]::GetFileName($Path)
    if ($name -match '\.win-') {
        Write-Host "Skipping unix execute-bit assert for $name"
        return
    }

    $zip = [System.IO.Compression.ZipFile]::OpenRead([System.IO.Path]::GetFullPath($Path))
    try {
        $checked = 0
        $missing = [System.Collections.Generic.List[string]]::new()
        $hasAz = $false
        $hasPython = $false
        foreach ($entry in $zip.Entries) {
            $n = $entry.FullName.Replace('\', '/')
            if (Test-UnixExecuteEntry $n) {
                $checked++
                if (-not (Test-ZipUnixExecute $entry.ExternalAttributes)) {
                    $missing.Add("$n mode=$(Get-ZipUnixMode $entry.ExternalAttributes)")
                }
            }
            if ($n -eq 'az/bin/az' -or $n.EndsWith('/az/bin/az')) {
                $hasAz = $true
            }
            if ($n -match '(^|/)python/bin/python3$' -or $n -match '(^|/)python/bin/python$') {
                $hasPython = $true
            }
        }
        if (-not $hasAz) {
            throw "nupkg $name has no az/bin/az"
        }
        if (-not $hasPython) {
            throw "nupkg $name has no python/bin/python3"
        }
        if ($missing.Count -gt 0) {
            throw "nupkg $name missing unix +x on:`n$($missing -join "`n")"
        }
        Write-Host "Asserted $checked executable entries in $name"
    }
    finally {
        $zip.Dispose()
    }
}

function Expand-NupkgWithUnixModes([string] $Path, [string] $Dest) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $destFull = [System.IO.Path]::GetFullPath($Dest)
    if (Test-Path -LiteralPath $destFull) {
        Remove-Item -LiteralPath $destFull -Recurse -Force
    }
    New-Item -ItemType Directory -Path $destFull | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($full, $destFull)

    if ($IsWindows) {
        return
    }

    $zip = [System.IO.Compression.ZipFile]::OpenRead($full)
    try {
        foreach ($entry in $zip.Entries) {
            $n = $entry.FullName.Replace('\', '/')
            if ($n.EndsWith('/')) {
                continue
            }
            $mode = Get-ZipUnixMode $entry.ExternalAttributes
            if (($mode -band [Convert]::ToInt32('111', 8)) -eq 0) {
                continue
            }
            $target = Join-Path $destFull $n
            if (Test-Path -LiteralPath $target) {
                [System.IO.File]::SetUnixFileMode($target, [System.IO.UnixFileMode]($mode -band 0xFFF))
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

if (-not [string]::IsNullOrWhiteSpace($Nupkg)) {
    if (-not [string]::IsNullOrWhiteSpace($Destination)) {
        Expand-NupkgWithUnixModes $Nupkg $Destination
        return
    }
    if (-not $Assert) {
        Set-NupkgUnixExecuteBits $Nupkg
    }
    Assert-NupkgUnixExecuteBits $Nupkg
}
