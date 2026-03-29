# 差分更新占位

v1 当前使用“静态 manifest + 整包下载 + 本地切换版本”。

差分更新的目标是：

- 不再下载整个 bundle
- 只下载从旧版本到新版本变化的补丁

## 当前已预留

- `Launcher.cs` 中的 `ManifestDelta` 类型
- `make_release_manifest.py` 的可选参数：
  - `--delta-base-version`
  - `--delta-url`
  - `--delta-bundle`

## 当前行为

- 如果 manifest 广播了 `windows.delta`
- launcher 会记录日志
- 但仍然回退到整包下载

## 以后再做

1. 选定差分算法
2. 构建机生成差分包
3. launcher 校验 base version
4. 应用补丁后校验 SHA256
5. 失败时自动回退到 full bundle
