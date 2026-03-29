param(
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot "..\dist\uninstaller"
}

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$source = Join-Path $projectRoot "installer\UninstallBootstrap.cs"
$target = Join-Path $OutputRoot "uninstall.exe"
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (!(Test-Path -LiteralPath $csc)) {
    throw "C# compiler not found: $csc"
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

& $csc /nologo /target:winexe /out:$target /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source

Write-Host "Uninstaller exe written to: $target"
