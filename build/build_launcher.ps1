param(
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot "..\dist\launcher"
}

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$source = Join-Path $projectRoot "launcher\Launcher.cs"
$target = Join-Path $OutputRoot "launcher.exe"
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (!(Test-Path -LiteralPath $csc)) {
    throw "C# compiler not found: $csc"
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

& $csc /nologo /target:exe /out:$target /reference:System.Web.Extensions.dll /reference:System.IO.Compression.FileSystem.dll $source

Write-Host "Launcher exe written to: $target"
