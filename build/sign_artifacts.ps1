param(
    [string]$PfxPath = "",
    [string]$PfxPassword = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$SigntoolPath = ""
)

$ErrorActionPreference = "Stop"

throw @"
代码签名尚未接入真实证书。

预留参数：
- PfxPath
- PfxPassword
- TimestampUrl
- SigntoolPath

建议后续用 signtool.exe 对以下文件签名：
- dist\\setup\\setup.exe
- dist\\launcher\\launcher.exe
- dist\\uninstaller\\uninstall.exe
"@
