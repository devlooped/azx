#Requires -Version 7
# Download/extract helpers for payload.ps1. Get-SourcesRoot is the sources unpack unit.

function Get-GitHubAuthTokens {
    # In GitHub Actions prefer GITHUB_TOKEN: per-repo quota, unlike a shared GH_TOKEN PAT.
    $tokens = [System.Collections.Generic.List[string]]::new()
    $order = if ($env:GITHUB_ACTIONS -eq 'true') {
        @($env:GITHUB_TOKEN, $env:GH_TOKEN)
    }
    else {
        @($env:GH_TOKEN, $env:GITHUB_TOKEN)
    }
    foreach ($t in $order) {
        if (-not [string]::IsNullOrWhiteSpace($t) -and -not $tokens.Contains($t)) {
            $tokens.Add($t)
        }
    }
    return $tokens
}

function Get-GitHubHeaders {
    $headers = @{ 'User-Agent' = 'azx-payload' }
    $token = @(Get-GitHubAuthTokens) | Select-Object -First 1
    if ($token) {
        $headers['Authorization'] = "Bearer $token"
    }
    return $headers
}

function Invoke-GitHubRestMethod([string] $Uri) {
    $attempts = [System.Collections.Generic.List[string]]::new()
    foreach ($t in @(Get-GitHubAuthTokens)) {
        $attempts.Add($t)
    }
    $attempts.Add('') # unauthenticated last; public REST still has an IP quota

    $errors = [System.Collections.Generic.List[string]]::new()
    foreach ($token in $attempts) {
        $headers = @{ 'User-Agent' = 'azx-payload' }
        if ($token) {
            $headers['Authorization'] = "Bearer $token"
        }
        try {
            return Invoke-RestMethod -Uri $Uri -Headers $headers
        }
        catch {
            $status = $null
            try { $status = [int]$_.Exception.Response.StatusCode } catch { }
            $msg = [string]$_
            $errors.Add($msg)
            $retry = ($status -in 401, 403, 429) -or ($msg -match 'rate limit') -or ($msg -match 'Bad credentials')
            if ($retry) {
                Write-Host "GitHub API rejected request$(if ($null -ne $status) { " ($status)" }); trying next credentials"
                continue
            }
            throw
        }
    }
    throw "GitHub API failed for $Uri. $($errors -join ' | ')"
}

function Save-Url([string] $Url, [string] $Dest) {
    if (Test-Path $Dest) {
        Write-Host "Cached $Dest"
        return
    }
    Write-Host "Downloading $Url"
    $params = @{ Uri = $Url; OutFile = $Dest; MaximumRedirection = 5 }
    # Azure Blob rejects a GitHub Bearer token (AuthenticationFailed). Only GitHub needs it.
    if ($Url -match '://(api\.)?github\.com/' -or $Url -match '://.*\.githubusercontent\.com/') {
        $params['Headers'] = Get-GitHubHeaders
    }
    Invoke-WebRequest @params
}

function Expand-TarGz([string] $Archive, [string] $Dest) {
    New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    & tar -xf $Archive -C $Dest
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed extracting $Archive (exit $LASTEXITCODE)"
    }
}

function Expand-Zip([string] $Archive, [string] $Dest) {
    New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    # GNU tar cannot unpack zip; Expand-Archive is the cross-platform zip path (win-x64 on ubuntu).
    Expand-Archive -LiteralPath $Archive -DestinationPath $Dest -Force
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

function Get-CaseCollidingDuplicates([string[]] $RelativePaths) {
    $dupes = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $RelativePaths -or $RelativePaths.Count -eq 0) {
        return @()
    }
    $seen = @{}
    foreach ($p in $RelativePaths) {
        $key = $p.Replace('\', '/').ToLowerInvariant()
        if ($seen.ContainsKey($key)) {
            $dupes.Add($p)
        }
        else {
            $seen[$key] = $true
        }
    }
    return @($dupes)
}

function Remove-UnusedPythonShare([string] $Root) {
    $share = Join-Path $Root 'python/share'
    if (Test-Path -LiteralPath $share) {
        Write-Host "Removing unused $share"
        Remove-Item -LiteralPath $share -Recurse -Force
    }
}

function Repair-CaseCollisions([string] $Root) {
    if (-not (Test-Path -LiteralPath $Root)) {
        return
    }
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([char]'\', [char]'/')
    $files = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force)
    $rel = foreach ($f in $files) {
        $f.FullName.Substring($rootFull.Length).TrimStart('\', '/')
    }
    foreach ($d in @(Get-CaseCollidingDuplicates $rel)) {
        $path = Join-Path $rootFull $d
        Write-Host "Removing case-colliding payload file $d"
        Remove-Item -LiteralPath $path -Force
    }
}
