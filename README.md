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
