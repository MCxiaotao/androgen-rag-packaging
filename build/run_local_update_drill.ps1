param(
    [Parameter(Mandatory = $true)]
    [string]$LauncherExe,

    [Parameter(Mandatory = $true)]
    [string]$LauncherTemplate,

    [Parameter(Mandatory = $true)]
    [string]$BootstrapBundleDir,

    [Parameter(Mandatory = $true)]
    [string]$OldVersion,

    [Parameter(Mandatory = $true)]
    [string]$NewBundleZip,

    [Parameter(Mandatory = $true)]
    [string]$NewVersion,

    [Parameter(Mandatory = $true)]
    [string]$PythonExe,

    [string]$WorkspaceRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = Join-Path $PSScriptRoot "..\..\packtmp\update-drill"
}

$WorkspaceRoot = [System.IO.Path]::GetFullPath($WorkspaceRoot)
$LauncherExe = [System.IO.Path]::GetFullPath($LauncherExe)
$LauncherTemplate = [System.IO.Path]::GetFullPath($LauncherTemplate)
$BootstrapBundleDir = [System.IO.Path]::GetFullPath($BootstrapBundleDir)
$NewBundleZip = [System.IO.Path]::GetFullPath($NewBundleZip)
$PythonExe = [System.IO.Path]::GetFullPath($PythonExe)

$installRoot = Join-Path $WorkspaceRoot "install"
$stateRoot = Join-Path $WorkspaceRoot "state-root"
$manifestPath = Join-Path $WorkspaceRoot "stable.local.json"
$launcherJsonPath = Join-Path $installRoot "launcher.json"

if (Test-Path -LiteralPath $WorkspaceRoot) {
    Remove-Item -LiteralPath $WorkspaceRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $installRoot "bootstrap\$OldVersion") -Force | Out-Null
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null

Copy-Item -LiteralPath $LauncherExe -Destination (Join-Path $installRoot "launcher.exe") -Force
Copy-Item -LiteralPath $LauncherTemplate -Destination $launcherJsonPath -Force
Get-ChildItem -LiteralPath $BootstrapBundleDir -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $installRoot "bootstrap\$OldVersion") -Recurse -Force
}

$launcherJson = Get-Content -LiteralPath $launcherJsonPath -Raw | ConvertFrom-Json
$launcherJson.bootstrap_version = $OldVersion
$launcherJson.open_browser = $false
$launcherJson.default_port = 8519
$launcherJson.manifest_url = ([System.Uri]::new($manifestPath)).AbsoluteUri
$launcherJson | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $launcherJsonPath -Encoding UTF8

$bundleUri = ([System.Uri]::new($NewBundleZip)).AbsoluteUri
& $PythonExe (Join-Path (Split-Path -Parent $PSScriptRoot) "build\make_release_manifest.py") --version $NewVersion --bundle $NewBundleZip --url $bundleUri --out $manifestPath --notes "Local update drill $OldVersion -> $NewVersion"

$env:APP_STATE_ROOT = $stateRoot
try {
    & (Join-Path $installRoot "launcher.exe")
    if ($LASTEXITCODE -ne 0) {
        throw "Launcher exited with code $LASTEXITCODE"
    }
}
finally {
    Remove-Item Env:APP_STATE_ROOT -ErrorAction SilentlyContinue
}

$currentJson = Join-Path $stateRoot "AndrogenRAG\current.json"
if (!(Test-Path -LiteralPath $currentJson)) {
    throw "Missing current.json after update drill: $currentJson"
}

$state = Get-Content -LiteralPath $currentJson -Raw | ConvertFrom-Json
if ($state.current_version -ne $NewVersion) {
    throw "Update drill failed. Expected current_version=$NewVersion, actual=$($state.current_version)"
}

Write-Host "Local update drill succeeded: $OldVersion -> $NewVersion"
Write-Host "State file: $currentJson"
