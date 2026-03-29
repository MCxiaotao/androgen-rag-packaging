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

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot "..\dist\setup"
}
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$distRoot = Split-Path -Parent $OutputRoot
$launcherDist = Join-Path $distRoot 'launcher'
$uninstallerDist = Join-Path $distRoot 'uninstaller'
$bundleDist = Join-Path $distRoot 'bundles'
$payloadRoot = Join-Path $OutputRoot 'payload_root'
$payloadZip = Join-Path $OutputRoot 'payload.zip'
$payloadJson = Join-Path $OutputRoot 'setup_payload.json'
$setupExe = Join-Path $OutputRoot 'setup.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

& (Join-Path $projectRoot 'build\build_launcher.ps1') -OutputRoot $launcherDist
& (Join-Path $projectRoot 'build\build_uninstaller.ps1') -OutputRoot $uninstallerDist
& (Join-Path $projectRoot 'build\build_bundle.ps1') -SourceRepo $SourceRepo -Version $Version -RuntimeDir $RuntimeDir -SmartCypDir $SmartCypDir -FpgnnRepoDir $FpgnnRepoDir -JavaHomeDir $JavaHomeDir -SygmaSitePackagesDir $SygmaSitePackagesDir -OutputRoot $bundleDist
if (Test-Path -LiteralPath $OutputRoot) {
    Get-ChildItem -LiteralPath $OutputRoot -Force | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $payloadRoot "bootstrap\$Version") -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $launcherDist 'launcher.exe') -Destination (Join-Path $payloadRoot 'launcher.exe') -Force
Copy-Item -LiteralPath (Join-Path $uninstallerDist 'uninstall.exe') -Destination (Join-Path $payloadRoot 'uninstall.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'config\launcher.template.json') -Destination (Join-Path $payloadRoot 'launcher.json') -Force
$bundlePayloadDir = Join-Path (Join-Path $bundleDist $Version) 'bundle'
Get-ChildItem -LiteralPath $bundlePayloadDir -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payloadRoot "bootstrap\$Version") -Recurse -Force
}
$setupPayload = @{
    app_id = 'AndrogenRAG'
    display_name = 'Androgen RAG'
    publisher = 'MCxiaotao'
    bootstrap_version = $Version
    shortcut_name = 'Androgen RAG'
    default_install_dir = [System.IO.Path]::Combine($env:LOCALAPPDATA, 'Programs', 'AndrogenRAG')
    payload_archive = 'payload.zip'
}
$setupPayload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $payloadJson -Encoding UTF8

Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadZip -CompressionLevel Optimal
& $csc /nologo /target:winexe /out:$setupExe /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /resource:$payloadZip,payload.zip /resource:$payloadJson,setup_payload.json (Join-Path $projectRoot 'installer\SetupBootstrap.cs')

Write-Host "Setup exe written to: $setupExe"
