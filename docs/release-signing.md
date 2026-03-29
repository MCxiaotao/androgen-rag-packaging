# 发布与签名预留

当前安装包已经可以直接分发，但还没有做代码签名。

## 为什么要签名

- 减少 Windows SmartScreen 警告
- 提高 `setup.exe` / `launcher.exe` / `uninstall.exe` 的可信度
- 便于企业环境白名单放行

## 预留文件

- `build/sign_artifacts.ps1`
- `assets/branding/app.ico`

## 未来签名建议

1. 准备代码签名证书 `PFX`
2. 使用 `signtool.exe` 对以下文件签名
   - `dist/setup/setup.exe`
   - `dist/launcher/launcher.exe`
   - `dist/uninstaller/uninstall.exe`
3. 使用时间戳服务，避免证书过期后签名失效

## 当前状态

- 构建脚本已经预留图标入口
- 还未接入真实证书
- `sign_artifacts.ps1` 目前只做占位，不自动签名
