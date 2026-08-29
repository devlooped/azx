#Requires -Version 7
<#
.SYNOPSIS
  Builds an Azure CLI Payload tree for one RID into -OutDir.
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string] $Rid,

    [Parameter(Mandatory)]
    [string] $AzureCliVersion,

    [Parameter(Mandatory)]
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$AzureCliVersion = $AzureCliVersion.Trim()

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$sentinel = Join-Path $OutDir '.payload'
$expected = "$Rid $AzureCliVersion"
if ((Test-Path $sentinel) -and (Get-Content $sentinel -Raw).Trim() -eq $expected) {
    Write-Host "Payload already present: $expected"
    return
}

if (Test-Path $OutDir) {
    Remove-Item $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutDir | Out-Null

$cache = Join-Path ([System.IO.Path]::GetDirectoryName($OutDir)) 'cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
. (Join-Path $PSScriptRoot 'payload.functions.ps1')

function Get-PythonVersionFromSources([string] $SourcesRoot) {
    $buildCmd = Join-Path $SourcesRoot 'build_scripts/windows/scripts/build.cmd'
    if (-not (Test-Path $buildCmd)) {
        throw "build.cmd not found at $buildCmd"
    }
    foreach ($line in Get-Content $buildCmd) {
        if ($line -match 'set PYTHON_VERSION=([0-9]+\.[0-9]+\.[0-9]+)') {
            return $Matches[1]
        }
    }
    throw "PYTHON_VERSION not found in $buildCmd"
}

function Get-PbsTriple([string] $PayloadRid) {
    switch ($PayloadRid) {
        'linux-x64' { 'x86_64-unknown-linux-gnu' }
        'linux-arm64' { 'aarch64-unknown-linux-gnu' }
        'osx-x64' { 'x86_64-apple-darwin' }
        'osx-arm64' { 'aarch64-apple-darwin' }
        default { throw "No PBS triple for $PayloadRid" }
    }
}

function Get-PbsAsset([string] $PythonVersion, [string] $Triple) {
    $pattern = "^cpython-$([regex]::Escape($PythonVersion))\+.*-$([regex]::Escape($Triple))-install_only_stripped\.tar\.gz$"
    try {
        $releases = Invoke-GitHubRestMethod 'https://api.github.com/repos/astral-sh/python-build-standalone/releases?per_page=15'
        foreach ($release in @($releases)) {
            if (-not $release.assets) { continue }
            $asset = @($release.assets | Where-Object { $_.name -match $pattern }) | Select-Object -First 1
            if ($asset) {
                return $asset
            }
        }
        Write-Host 'No PBS asset in GitHub API releases; trying releases.atom'
    }
    catch {
        Write-Host "PBS GitHub API lookup failed: $_"
    }

    # github.com HTML/atom does not consume REST API quota.
    $atomHeaders = @{ 'User-Agent' = 'azx-payload' }
    $atom = Invoke-WebRequest -Uri 'https://github.com/astral-sh/python-build-standalone/releases.atom' -Headers $atomHeaders
    $tags = [regex]::Matches($atom.Content, 'python-build-standalone/releases/tag/([^<"\s]+)') |
        ForEach-Object { [System.Uri]::UnescapeDataString($_.Groups[1].Value) } |
        Select-Object -Unique
    foreach ($tag in $tags) {
        $name = "cpython-$PythonVersion+$tag-$Triple-install_only_stripped.tar.gz"
        $url = "https://github.com/astral-sh/python-build-standalone/releases/download/$tag/$name"
        try {
            $head = Invoke-WebRequest -Uri $url -Method Head -SkipHttpErrorCheck -Headers $atomHeaders
            if ($head.StatusCode -ge 200 -and $head.StatusCode -lt 400) {
                return [pscustomobject]@{
                    name                 = $name
                    browser_download_url = $url
                }
            }
        }
        catch {
            # Tag does not contain this CPython build, or HEAD was not supported.
        }
    }
    throw "No python-build-standalone install_only_stripped asset for CPython $PythonVersion on $Triple."
}

function Write-UnixLauncher([string] $Root) {
    $bin = Join-Path $Root 'bin'
    New-Item -ItemType Directory -Path $bin -Force | Out-Null
    $launcher = Join-Path $bin 'az'
    @'
#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PY="$ROOT/python/bin/python3"
if [[ ! -x "$PY" ]]; then
  PY="$ROOT/python/bin/python"
fi
exec "$PY" -sm azure.cli "$@"
'@ | Set-Content -Path $launcher -Encoding ascii -NoNewline
    if (-not $IsWindows) {
        & chmod +x $launcher
        $pyBin = Join-Path $Root 'python/bin'
        if (Test-Path -LiteralPath $pyBin) {
            Get-ChildItem -LiteralPath $pyBin -File | Where-Object { $_.Extension -ne '.pyc' } | ForEach-Object {
                & chmod +x $_.FullName
            }
        }
    }
}

function Install-Pbs([string] $PythonVersion, [string] $PayloadRid, [string] $DestRoot) {
    $triple = Get-PbsTriple $PayloadRid
    $asset = Get-PbsAsset $PythonVersion $triple
    $tar = Join-Path $cache $asset.name
    Save-Url $asset.browser_download_url $tar
    $tmp = Join-Path $cache "pbs-$PayloadRid-$PythonVersion"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-TarGz $tar $tmp
    $pythonDir = Join-Path $tmp 'python'
    if (-not (Test-Path $pythonDir)) {
        throw "PBS archive did not contain python/: $tar"
    }
    $dest = Join-Path $DestRoot 'python'
    Move-Item $pythonDir $dest
}

function Get-MajorMinor([string] $PythonVersion) {
    if ($PythonVersion -notmatch '^(\d+\.\d+)') {
        throw "Cannot parse major.minor from $PythonVersion"
    }
    return $Matches[1]
}

function Install-CliFromPyPI([string] $PythonExe, [string] $Version) {
    & $PythonExe -m ensurepip --upgrade
    if ($LASTEXITCODE -ne 0) {
        $getPip = Join-Path $cache 'get-pip.py'
        Save-Url 'https://bootstrap.pypa.io/get-pip.py' $getPip
        & $PythonExe $getPip
        if ($LASTEXITCODE -ne 0) { throw "get-pip.py failed with $LASTEXITCODE" }
    }
    & $PythonExe -m pip install --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw "pip upgrade failed with $LASTEXITCODE" }
    Write-Host "pip install azure-cli==$Version"
    & $PythonExe -m pip install "azure-cli==$Version"
    if ($LASTEXITCODE -ne 0) { throw "pip install azure-cli==$Version failed with $LASTEXITCODE" }
}

function Install-CliFromSources([string] $PythonExe, [string] $SourcesRoot) {
    & $PythonExe -m ensurepip --upgrade
    if ($LASTEXITCODE -ne 0) {
        $getPip = Join-Path $cache 'get-pip.py'
        Save-Url 'https://bootstrap.pypa.io/get-pip.py' $getPip
        & $PythonExe $getPip
        if ($LASTEXITCODE -ne 0) { throw "get-pip.py failed with $LASTEXITCODE" }
    }
    & $PythonExe -m pip install --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw "pip upgrade failed with $LASTEXITCODE" }

    $src = Join-Path $SourcesRoot 'src'
    foreach ($pkg in @('azure-cli-telemetry', 'azure-cli-core', 'azure-cli')) {
        Write-Host "pip install --no-deps $pkg"
        & $PythonExe -m pip install --no-deps (Join-Path $src $pkg)
        if ($LASTEXITCODE -ne 0) { throw "pip install $pkg failed with $LASTEXITCODE" }
    }

    $requirements = Join-Path $src 'azure-cli/requirements.py3.Linux.txt'
    Write-Host "pip install --only-binary=:all: -r $requirements"
    & $PythonExe -m pip install --only-binary=:all: -r $requirements
    if ($LASTEXITCODE -ne 0) { throw "pip install requirements failed with $LASTEXITCODE" }
}

function Install-CliMacWheels([string] $HostPython, [string] $SitePackages, [string] $Version, [string] $PayloadRid, [string] $MajorMinor) {
    $platform = if ($PayloadRid -eq 'osx-arm64') { 'macosx_11_0_arm64' } else { 'macosx_10_15_x86_64' }
    $pyTag = $MajorMinor.Replace('.', '')
    $wheels = Join-Path $cache "wheels-$PayloadRid-$Version"
    New-Item -ItemType Directory -Path $wheels -Force | Out-Null
    New-Item -ItemType Directory -Path $SitePackages -Force | Out-Null
    foreach ($abi in @("cp$pyTag", 'abi3', 'none')) {
        Write-Host "pip download azure-cli==$Version --platform $platform --abi $abi"
        & $HostPython -m pip download `
            "azure-cli==$Version" `
            -d $wheels `
            --platform $platform `
            --python-version $MajorMinor `
            --implementation cp `
            --abi $abi `
            --only-binary=:all:
        # Some abis have no matching artifacts; keep going.
    }
    if (-not (Get-ChildItem $wheels -File -ErrorAction SilentlyContinue)) {
        throw "No macOS wheels downloaded for azure-cli $Version ($platform)."
    }
    Write-Host "pip install --target $SitePackages --no-index --find-links $wheels azure-cli==$Version"
    & $HostPython -m pip install `
        "azure-cli==$Version" `
        --target $SitePackages `
        --no-index `
        --find-links $wheels
    if ($LASTEXITCODE -ne 0) { throw "macOS wheel install failed with $LASTEXITCODE" }
}

function Resolve-HostPython {
    foreach ($candidate in @('python3', 'python')) {
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    throw 'python3 is required to assemble osx Payloads.'
}

function Resolve-PbsPython([string] $Root) {
    foreach ($name in @('python3', 'python')) {
        $path = Join-Path $Root "python/bin/$name"
        if (Test-Path $path) { return $path }
    }
    throw "PBS python not found under $Root"
}

switch ($Rid) {
    'win-x64' {
        $zip = Join-Path $cache "azure-cli-$AzureCliVersion-x64.zip"
        Save-Url "https://azcliprod.blob.core.windows.net/zip/azure-cli-$AzureCliVersion-x64.zip" $zip
        Expand-Zip $zip $OutDir
        $azCmd = Join-Path $OutDir 'bin/az.cmd'
        if (-not (Test-Path $azCmd)) {
            $nested = Get-ChildItem $OutDir -Directory | Select-Object -First 1
            if ($nested -and (Test-Path (Join-Path $nested.FullName 'bin/az.cmd'))) {
                Get-ChildItem $nested.FullName -Force | ForEach-Object {
                    Move-Item $_.FullName (Join-Path $OutDir $_.Name) -Force
                }
                Remove-Item $nested.FullName -Recurse -Force
            }
        }
        if (-not (Test-Path (Join-Path $OutDir 'bin/az.cmd'))) {
            throw "Windows zip did not contain bin/az.cmd"
        }
    }
    { $_ -in 'linux-x64', 'linux-arm64' } {
        $os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        $needArm = $Rid -eq 'linux-arm64'
        # $IsLinux/$IsWindows/$IsMacOS are read-only automatic variables (case-insensitive).
        $isArm = $arch -eq [System.Runtime.InteropServices.Architecture]::Arm64
        if (-not $IsLinux -or ($needArm -xor $isArm)) {
            throw "linux Payload for $Rid must be built on a matching linux runner (got $os $arch)."
        }
        $sources = Get-SourcesRoot $AzureCliVersion
        $pythonVersion = Get-PythonVersionFromSources $sources
        Write-Host "PYTHON_VERSION=$pythonVersion"
        Install-Pbs $pythonVersion $Rid $OutDir
        $py = Resolve-PbsPython $OutDir
        Install-CliFromSources $py $sources
        Write-UnixLauncher $OutDir
    }
    { $_ -in 'osx-x64', 'osx-arm64' } {
        $sources = Get-SourcesRoot $AzureCliVersion
        $pythonVersion = Get-PythonVersionFromSources $sources
        Write-Host "PYTHON_VERSION=$pythonVersion"
        Install-Pbs $pythonVersion $Rid $OutDir
        if ($IsMacOS) {
            $py = Resolve-PbsPython $OutDir
            Install-CliFromPyPI $py $AzureCliVersion
        }
        else {
            $majorMinor = Get-MajorMinor $pythonVersion
            $site = Join-Path $OutDir "python/lib/python$majorMinor/site-packages"
            $hostPy = Resolve-HostPython
            Install-CliMacWheels $hostPy $site $AzureCliVersion $Rid $majorMinor
        }
        Write-UnixLauncher $OutDir
    }
}

Remove-UnusedPythonShare $OutDir
Repair-CaseCollisions $OutDir
Set-Content -Path $sentinel -Value $expected -Encoding ascii
Write-Host "Payload ready: $OutDir"
