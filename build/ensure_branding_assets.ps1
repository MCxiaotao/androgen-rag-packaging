param(
    [string]$BrandingRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BrandingRoot)) {
    $BrandingRoot = Join-Path $PSScriptRoot "..\assets\branding"
}

$BrandingRoot = [System.IO.Path]::GetFullPath($BrandingRoot)
$bannerPath = Join-Path $BrandingRoot "banner.bmp"
$readmePath = Join-Path $BrandingRoot "README.md"

New-Item -ItemType Directory -Path $BrandingRoot -Force | Out-Null

if (!(Test-Path -LiteralPath $readmePath)) {
    @"
# Branding Assets

- `banner.bmp`: 安装器顶部横幅占位图。替换成你自己的品牌横幅即可。
- `app.ico`: 可选。放在这里后，`launcher.exe`、`uninstall.exe` 和 `setup.exe` 会自动尝试带上这个图标。

建议尺寸：

- `banner.bmp`: 640 x 180 左右
- `app.ico`: 包含 16/32/48/256 多尺寸
"@ | Set-Content -LiteralPath $readmePath -Encoding utf8
}

if (!(Test-Path -LiteralPath $bannerPath)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 640, 180
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(245, 247, 250))
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.Rectangle 0, 0, 640, 180),
            [System.Drawing.Color]::FromArgb(33, 99, 235),
            [System.Drawing.Color]::FromArgb(17, 24, 39),
            0.0
        )
        try {
            $graphics.FillRectangle($brush, 0, 0, 640, 180)
        }
        finally {
            $brush.Dispose()
        }

        $titleFont = New-Object System.Drawing.Font "Segoe UI", 24, ([System.Drawing.FontStyle]::Bold)
        $subFont = New-Object System.Drawing.Font "Segoe UI", 10
        $whiteBrush = [System.Drawing.Brushes]::White
        $mutedBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 255, 255, 255))
        try {
            $graphics.DrawString("Androgen RAG", $titleFont, $whiteBrush, 24, 40)
            $graphics.DrawString("Branding Placeholder - replace assets\\branding\\banner.bmp", $subFont, $mutedBrush, 26, 92)
        }
        finally {
            $titleFont.Dispose()
            $subFont.Dispose()
            $mutedBrush.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    $bmp.Save($bannerPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

Write-Host "Branding assets ready under: $BrandingRoot"
