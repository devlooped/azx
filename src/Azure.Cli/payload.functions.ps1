#Requires -Version 7
# Download/extract helpers for payload.ps1. Get-SourcesRoot is the sources unpack unit.

function Get-GitHubHeaders {
    $headers = @{ 'User-Agent' = 'azx-payload' }
    if ($env:GH_TOKEN) {
        $headers['Authorization'] = "Bearer $($env:GH_TOKEN)"
    }
    elseif ($env:GITHUB_TOKEN) {
        $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)"
    }
    return $headers
}

function Save-Url([string] $Url, [string] $Dest) {
    if (Test-Path $Dest) {
        Write-Host "Cached $Dest"
        return
    }
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Dest -Headers (Get-GitHubHeaders) -MaximumRedirection 5
}

function Expand-TarGz([string] $Archive, [string] $Dest) {
    New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    & tar -xf $Archive -C $Dest
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed extracting $Archive (exit $LASTEXITCODE)"
    }
}

function Get-SourcesRoot {
    param(
        [Parameter(Mandatory, Position = 0)]
        [string] $Version,
        [string] $CacheDir
    )
    if ([string]::IsNullOrWhiteSpace($CacheDir)) {
        $CacheDir = $cache
    }
    # GitHub zip is not a tar archive; GNU tar on Linux rejects it.
    $archive = Join-Path $CacheDir "azure-cli-$Version-src.tar.gz"
    Save-Url "https://github.com/Azure/azure-cli/archive/refs/tags/azure-cli-$Version.tar.gz" $archive
    $extract = Join-Path $CacheDir "src-$Version"
    $inner = Join-Path $extract "azure-cli-azure-cli-$Version"
    if (-not (Test-Path $inner)) {
        if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
        Expand-TarGz $archive $extract
    }
    if (-not (Test-Path $inner)) {
        throw "Unexpected sources layout under $extract"
    }
    return $inner
}
