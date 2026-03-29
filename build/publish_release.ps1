param(
    [string]$GithubOwner = 'MCxiaotao',
    [string]$PackagingRepoName = 'app测试版',
    [string]$UpdateFeedRepoName = 'app-update-feed',
    [string]$Version = '1.0.0',
    [string]$PythonExe = 'D:\miniconda\envs\admet_clean\python.exe',
    [string]$ProxyUrl = 'http://127.0.0.1:7897',
    [switch]$SkipRepoCreate,
    [switch]$SkipReleaseUpload
)

$ErrorActionPreference = 'Stop'

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
    return [System.IO.Path]::GetFullPath($LocalDir).Replace('\', '/')
}

function Invoke-GitRepo {
    param(
        [Parameter(Mandatory = $true)][string]$LocalDir,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs
    )

    $safeDir = Get-SafeDirectoryValue -LocalDir $LocalDir
    & git -c "safe.directory=$safeDir" -C $LocalDir @GitArgs
}

function Ensure-RemoteRepo {
    param(
        [Parameter(Mandatory = $true)][string]$LocalDir,
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Visibility
    )

    $remotes = Invoke-GitRepo -LocalDir $LocalDir remote
    if ($LASTEXITCODE -eq 0 -and (($remotes | ForEach-Object { $_.ToString().Trim() }) -contains 'origin')) {
        return
    }

    if ($SkipRepoCreate) {
        Invoke-GitRepo -LocalDir $LocalDir remote add origin ("https://github.com/" + $Repo + '.git')
        return
    }

    gh repo view $Repo *> $null
    if ($LASTEXITCODE -ne 0) {
        gh repo create $Repo --$Visibility --source $LocalDir --remote origin --push
        return
    }

    Invoke-GitRepo -LocalDir $LocalDir remote add origin ("https://github.com/" + $Repo + '.git')
    Invoke-GitRepo -LocalDir $LocalDir push -u origin master
}

function Push-Repo {
    param([Parameter(Mandatory = $true)][string]$LocalDir)
    Invoke-GitRepo -LocalDir $LocalDir push -u origin master
}

Write-Host "==> Checking GitHub auth"
gh auth status

Write-Host "==> Ensuring packaging repo: $packagingRepo"
Ensure-RemoteRepo -LocalDir $packagingDir -Repo $packagingRepo -Visibility 'public'
Push-Repo -LocalDir $packagingDir

Write-Host "==> Ensuring update feed repo: $updateFeedRepo"
Ensure-RemoteRepo -LocalDir $updateFeedDir -Repo $updateFeedRepo -Visibility 'public'
Push-Repo -LocalDir $updateFeedDir

Write-Host "==> Regenerating manifest"
& $PythonExe $manifestScript --version $Version --bundle $bundleZip --url $releaseUrl --out $manifestPath --notes 'v1 packaging baseline: setup installer, private runtime, launcher bootstrap, slimmed vendor bundle.'

$dirty = Invoke-GitRepo -LocalDir $updateFeedDir status --porcelain stable.json
if ($dirty) {
    Invoke-GitRepo -LocalDir $updateFeedDir add stable.json
    Invoke-GitRepo -LocalDir $updateFeedDir commit -m ("Update stable manifest for v" + $Version)
    Invoke-GitRepo -LocalDir $updateFeedDir push
}

if (-not $SkipReleaseUpload) {
    Write-Host "==> Publishing release assets"
    gh release view $releaseTag --repo $updateFeedRepo *> $null
    if ($LASTEXITCODE -ne 0) {
        gh release create $releaseTag $setupExe $bundleZip --repo $updateFeedRepo --title $releaseTag --notes 'Windows setup installer and versioned bundle.'
    } else {
        gh release upload $releaseTag $setupExe $bundleZip --repo $updateFeedRepo --clobber
    }
}

Write-Host "Publish flow completed."

