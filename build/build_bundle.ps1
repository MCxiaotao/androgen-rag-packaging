param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRepo,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$RuntimeDir,

    [string]$SmartCypDir = "",
    [string]$FpgnnRepoDir = "",
    [string]$JavaHomeDir = "",
    [string]$SygmaSitePackagesDir = "",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

function Copy-CleanTree {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    if (!(Test-Path -LiteralPath $Source)) {
        throw "Missing source path: $Source"
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force

    Get-ChildItem -LiteralPath $Destination -Recurse -Force -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "__pycache__" } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Get-ChildItem -LiteralPath $Destination -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.pyc', '.pyo' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Copy-JavaRuntimeSubset {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot
    )
    $entries = @('bin', 'conf', 'legal', 'lib', 'release')
    foreach ($name in $entries) {
        $src = Join-Path $SourceRoot $name
        if (!(Test-Path -LiteralPath $src)) {
            continue
        }
        $dst = Join-Path $DestinationRoot $name
        if ((Get-Item -LiteralPath $src).PSIsContainer) {
            Copy-CleanTree -Source $src -Destination $dst
        } else {
            New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
            Copy-Item -LiteralPath $src -Destination $dst -Force
        }
    }
}

function Copy-SygmaPackage {
    param(
        [Parameter(Mandatory = $true)][string]$SitePackagesDir,
        [Parameter(Mandatory = $true)][string]$RuntimeSitePackagesDir
    )
    $src = Join-Path $SitePackagesDir 'sygma'
    if (Test-Path -LiteralPath $src) {
        Copy-CleanTree -Source $src -Destination (Join-Path $RuntimeSitePackagesDir 'sygma')
    }

    $distInfos = Get-ChildItem -LiteralPath $SitePackagesDir -Directory -Filter 'SyGMa-*.dist-info' -ErrorAction SilentlyContinue
    foreach ($item in $distInfos) {
        Copy-CleanTree -Source $item.FullName -Destination (Join-Path $RuntimeSitePackagesDir $item.Name)
    }
}

function Patch-FpgnnTorchCompatibility {
    param([Parameter(Mandatory = $true)][string]$ToolPy)

    if (!(Test-Path -LiteralPath $ToolPy)) {
        return
    }

    $content = Get-Content -LiteralPath $ToolPy -Raw -Encoding UTF8
    if ($content.Contains('def _torch_load_compat(path):')) {
        return
    }
    $marker = "from fpgnn.model import FPGNN"
    $helper = @"
from fpgnn.model import FPGNN


def _torch_load_compat(path):
    try:
        return torch.load(path, map_location=lambda storage, loc: storage, weights_only=False)
    except TypeError:
        return torch.load(path, map_location=lambda storage, loc: storage)
"@
    $content = $content.Replace($marker, $helper.TrimEnd())
    $content = $content.Replace('state = torch.load(path,map_location=lambda storage, loc: storage)', 'state = _torch_load_compat(path)')
    $content = $content.Replace('state = torch.load(path, map_location=lambda storage, loc: storage)', 'state = _torch_load_compat(path)')
    Set-Content -LiteralPath $ToolPy -Value $content -Encoding UTF8
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot "..\dist\bundles"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$SourceRepo = [System.IO.Path]::GetFullPath($SourceRepo)
$RuntimeDir = [System.IO.Path]::GetFullPath($RuntimeDir)

$bundleRoot = Join-Path $OutputRoot $Version
$stageRoot = Join-Path $bundleRoot "bundle"
$zipPath = Join-Path $OutputRoot ("androgen-rag-bundle-win-x64-" + $Version + ".zip")

if (Test-Path -LiteralPath $bundleRoot) {
    Remove-Item -LiteralPath $bundleRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageRoot "app") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageRoot "vendor") -Force | Out-Null

Copy-CleanTree -Source $RuntimeDir -Destination (Join-Path $stageRoot "runtime")
Copy-CleanTree -Source (Join-Path $SourceRepo "scripts") -Destination (Join-Path $stageRoot "app\scripts")
Copy-CleanTree -Source (Join-Path $SourceRepo "kb") -Destination (Join-Path $stageRoot "app\kb")
Copy-CleanTree -Source (Join-Path $SourceRepo "pk_models") -Destination (Join-Path $stageRoot "app\pk_models")
if (![string]::IsNullOrWhiteSpace($SmartCypDir)) {
    Copy-CleanTree -Source ([System.IO.Path]::GetFullPath($SmartCypDir)) -Destination (Join-Path $stageRoot "vendor\smartcyp")
}

if (![string]::IsNullOrWhiteSpace($FpgnnRepoDir)) {
    $fpgnnDest = Join-Path $stageRoot "vendor\fpgnn"
    Copy-CleanTree -Source ([System.IO.Path]::GetFullPath($FpgnnRepoDir)) -Destination $fpgnnDest
    Patch-FpgnnTorchCompatibility -ToolPy (Join-Path $fpgnnDest 'fpgnn\tool\tool.py')
}

if (![string]::IsNullOrWhiteSpace($JavaHomeDir)) {
    Copy-JavaRuntimeSubset -SourceRoot ([System.IO.Path]::GetFullPath($JavaHomeDir)) -DestinationRoot (Join-Path $stageRoot "vendor\jre")
}

if (![string]::IsNullOrWhiteSpace($SygmaSitePackagesDir)) {
    Copy-SygmaPackage -SitePackagesDir ([System.IO.Path]::GetFullPath($SygmaSitePackagesDir)) -RuntimeSitePackagesDir (Join-Path $stageRoot 'runtime\Lib\site-packages')
}
$versionJson = @{
    version = $Version
    built_at = (Get-Date).ToUniversalTime().ToString("o")
    entry_script = "app/scripts/streamlit_app.py"
    packaging_mode = "main-runtime-plus-vendor"
} | ConvertTo-Json -Depth 4

Set-Content -LiteralPath (Join-Path $stageRoot "version.json") -Value $versionJson -Encoding UTF8

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Bundle directory: $stageRoot"
Write-Host "Bundle zip: $zipPath"
