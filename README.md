# app测试版

这个仓库用于承接 Windows 安装器、启动器、版本化运行包和在线更新流程，不直接替换现有 `androgen_rag` 业务仓库。

## 目标

- 安装器是 `setup.exe`
- 安装完成后生成桌面快捷方式
- 快捷方式启动 `launcher.exe`
- `launcher.exe` 负责安装完整性检查、版本切换、更新、启动 Streamlit
- 主应用继续保持 `Python + Streamlit + 多脚本 + 资源目录` 的结构
- 全部模块保留：`SMARTCyp / SyGMa / FPGNN / chemprop / pk_models / kb`
- 支持自定义安装路径

## 关键设计

- 安装目录：用户在安装向导里自定义选择，放稳定不常变的壳层
- 运行包目录：固定放在 `%LOCALAPPDATA%\AndrogenRAG\packages\versions\`
- 用户数据目录：固定放在 `%LOCALAPPDATA%\AndrogenRAG\`
- 更新发生在用户目录，不依赖安装目录可写

这样即使用户把安装器装到自定义路径，后续版本包更新也不会被 `Program Files` 或只读路径卡死。

## 发布脚本

已经提供 [build/publish_release.ps1](D:/androgen_rag/app测试版/build/publish_release.ps1) 对应的本地脚本版本，用于：

- 创建/绑定 GitHub 仓库
- 推送 `app测试版` 与 `app更新源`
- 上传 `setup.exe` 和 bundle zip 到 GitHub Releases
- 重新生成并推送 `stable.json`

默认更新源仓库名是 `app-update-feed`，因为当前 launcher 模板和安装包已经按这个名字写入了 manifest 地址。

## 额外预留

- `assets/branding/`
  - `banner.bmp` 安装器横幅占位图
  - `app.ico` 图标占位入口
- `build/run_local_update_drill.ps1`
  - 本地 `旧版本 -> 新版本` 更新演练脚本
- `build/sign_artifacts.ps1`
  - 签名脚本占位
- `templates/manifest.delta.example.json`
  - 差分更新 manifest 占位示例
