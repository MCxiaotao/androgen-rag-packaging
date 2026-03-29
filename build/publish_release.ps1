param(
    [string]$GithubOwner = 'MCxiaotao',
    [string]$PackagingRepoName = 'androgen-rag-packaging',
    [string]$UpdateFeedRepoName = 'app-update-feed',
    [string]$Version = '1.0.0',
    [string]$PythonExe = 'D:\miniconda\envs\admet_clean\python.exe',
    [string]$ProxyUrl = 'http://127.0.0.1:7897',
    [switch]$SkipRepoCreate,
    [switch]$SkipReleaseUpload
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
$packagingDir = $projectRoot
$updateFeedDir = Join-Path $workspaceRoot 'app更新源'
$setupExe = Join-Path $projectRoot 'dist\setup\setup.exe'
$bundleZip = Join-Path $projectRoot ("dist\bundles\androgen-rag-bundle-win-x64-$Version.zip")
$manifestPath = Join-Path $updateFeedDir 'stable.json'
$manifestScript = Join-Path $projectRoot 'build\make_release_manifest.py'
$releaseTag = 'v' + $Version
$releaseUrl = "https://github.com/$GithubOwner/$UpdateFeedRepoName/releases/download/$releaseTag/androgen-rag-bundle-win-x64-$Version.zip"
$packagingRepo = "$GithubOwner/$PackagingRepoName"
$updateFeedRepo = "$GithubOwner/$UpdateFeedRepoName"

if (!(Test-Path -LiteralPath $setupExe)) { throw "Missing setup.exe: $setupExe" }
if (!(Test-Path -LiteralPath $bundleZip)) { throw "Missing bundle zip: $bundleZip" }
if (!(Test-Path -LiteralPath $manifestScript)) { throw "Missing manifest generator: $manifestScript" }
if (!(Test-Path -LiteralPath $updateFeedDir)) { throw "Missing update feed repo dir: $updateFeedDir" }
if (!(Test-Path -LiteralPath $PythonExe)) { throw "Missing Python exe: $PythonExe" }

if ($ProxyUrl) {
    $env:HTTPS_PROXY = $ProxyUrl
    $env:HTTP_PROXY = $ProxyUrl
}

function Get-SafeDirectoryValue {
    param([Parameter(Mandatory = $true)][string]$LocalDir)
    return [System.IO.Path]::GetFullPath($LocalDir).Replace('\\', '/')
}

function Invoke-GitRepo {
    param(
        [Parameter(Mandatory = $true)][string]$LocalDir,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs
    )

    $safeDir = Get-SafeDirectoryValue -LocalDir $LocalDir
    $cmdArgs = @('-c', "safe.directory=$safeDir")
    if ($script:ProxyUrl) {
        $cmdArgs += @('-c', "http.proxy=$script:ProxyUrl", '-c', "https.proxy=$script:ProxyUrl")
    }
    $cmdArgs += @('-C', $LocalDir)
    $cmdArgs += $GitArgs
    & git @cmdArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git failed in ${LocalDir}: git $($GitArgs -join ' ')"
    }
}

function Test-GhRepoExists {
    param([Parameter(Mandatory = $true)][string]$Repo)
    try {
        & gh repo view $Repo --json nameWithOwner 1>$null 2>$null | Out-Null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Test-GhReleaseExists {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Tag
    )
    try {
        & gh release view $Tag --repo $Repo 1>$null 2>$null | Out-Null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Ensure-RemoteRepo {
    param(
        [Parameter(Mandatory = $true)][string]$LocalDir,
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Visibility
    )

    $remoteNames = (& git -c ("safe.directory=" + (Get-SafeDirectoryValue -LocalDir $LocalDir)) -C $LocalDir remote 2>$null)
    if ($LASTEXITCODE -eq 0 -and (($remoteNames | ForEach-Object { $_.ToString().Trim() }) -contains 'origin')) {
        return
    }

    $remoteUrl = "https://github.com/$Repo.git"

    if ($SkipRepoCreate) {
        Invoke-GitRepo -LocalDir $LocalDir remote add origin $remoteUrl
        return
    }

    if (-not (Test-GhRepoExists -Repo $Repo)) {
        & gh repo create $Repo --$Visibility
        if ($LASTEXITCODE -ne 0) {
            throw "gh repo create failed for $Repo"
        }
    }

    Invoke-GitRepo -LocalDir $LocalDir remote add origin $remoteUrl
}

function Push-Repo {
    param([Parameter(Mandatory = $true)][string]$LocalDir)
    Invoke-GitRepo -LocalDir $LocalDir push -u origin master
}

Write-Host "==> Checking GitHub auth"
& gh auth status
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub auth is not available in this shell. Run gh auth login in your own PowerShell first.'
}

Write-Host "==> Ensuring packaging repo: $packagingRepo"
Ensure-RemoteRepo -LocalDir $packagingDir -Repo $packagingRepo -Visibility 'public'
Push-Repo -LocalDir $packagingDir

Write-Host "==> Ensuring update feed repo: $updateFeedRepo"
Ensure-RemoteRepo -LocalDir $updateFeedDir -Repo $updateFeedRepo -Visibility 'public'
Push-Repo -LocalDir $updateFeedDir

Write-Host "==> Regenerating manifest"
& $PythonExe $manifestScript --version $Version --bundle $bundleZip --url $releaseUrl --out $manifestPath --notes 'v1 packaging baseline: setup installer, private runtime, launcher bootstrap, slimmed vendor bundle.'
if ($LASTEXITCODE -ne 0) {
    throw 'Manifest generator failed.'
}

$dirty = (& git -c ("safe.directory=" + (Get-SafeDirectoryValue -LocalDir $updateFeedDir)) -C $updateFeedDir status --porcelain stable.json)
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to inspect update feed repo status.'
}
if ($dirty) {
    Invoke-GitRepo -LocalDir $updateFeedDir add stable.json
    Invoke-GitRepo -LocalDir $updateFeedDir commit -m ("Update stable manifest for v" + $Version)
    Invoke-GitRepo -LocalDir $updateFeedDir push
}

if (-not $SkipReleaseUpload) {
    Write-Host "==> Publishing release assets"
    if (-not (Test-GhReleaseExists -Repo $updateFeedRepo -Tag $releaseTag)) {
        & gh release create $releaseTag $setupExe $bundleZip --repo $updateFeedRepo --title $releaseTag --notes 'Windows setup installer and versioned bundle.'
        if ($LASTEXITCODE -ne 0) {
            throw "gh release create failed for $updateFeedRepo $releaseTag"
        }
    } else {
        & gh release upload $releaseTag $setupExe $bundleZip --repo $updateFeedRepo --clobber
        if ($LASTEXITCODE -ne 0) {
            throw "gh release upload failed for $updateFeedRepo $releaseTag"
        }
    }
}

Write-Host 'Publish flow completed.'




