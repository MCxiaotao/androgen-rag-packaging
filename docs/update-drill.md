# 本地更新演练

## 目标

验证 launcher 能否从旧版 bootstrap 自动切到新版 bundle。

## 脚本

- `build/run_local_update_drill.ps1`

## 最近一次验证

- 基线版本：`1.0.0`
- 目标版本：`1.0.1`
- 结果：成功

## 关键观察

- launcher 先从安装目录 bootstrap `1.0.0`
- 再读取 manifest，安装 `1.0.1` 为 pending version
- 启动成功后把 `current.json` 切到 `1.0.1`
- `packages/versions/` 中同时保留 `1.0.0` 和 `1.0.1`

## 参考日志

- `packtmp/update-drill-101/state-root/AndrogenRAG/logs/launcher/launcher.log`
- `packtmp/update-drill-101/state-root/AndrogenRAG/current.json`

## 额外修复

这次演练顺带修复了 Windows 上 bundle 解压时的 `PathTooLongException`：

- 把解压 staging 路径改到更短的临时目录
- 增加跨盘移动失败时的复制兜底
