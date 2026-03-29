# 干净机器安装验证清单

目标：验证最终给终端用户分发的 `setup.exe`、`launcher.exe`、在线更新和卸载链条。

## 安装前

- 确认机器上没有旧版 `AndrogenRAG`
- 清理 `%LOCALAPPDATA%\AndrogenRAG`
- 清理桌面快捷方式和开始菜单残留

## 安装

- 下载 `setup.exe`
- 打开安装器，确认欢迎页正常
- 在组件页勾选/取消桌面快捷方式与开始菜单项，确认状态正确
- 选择自定义安装路径，确认目录可写
- 安装完成页显示安装目录、用户数据目录、启动程序、卸载程序

## 启动

- 双击桌面快捷方式启动
- 从开始菜单启动
- 直接运行安装目录下的 `launcher.exe`
- 确认浏览器成功打开本地页面
- 确认 `%LOCALAPPDATA%\AndrogenRAG\logs\launcher\launcher.log` 有启动记录

## 更新

- 先安装旧版
- 发布新版 bundle 和 `stable.json`
- 再次运行 `launcher.exe`
- 确认 `%LOCALAPPDATA%\AndrogenRAG\packages\versions\` 下出现新版本目录
- 确认 `current.json` 切到新版本
- 确认旧版本仍保留，便于回滚

## 卸载

- 从开始菜单里的“卸载 Androgen RAG”执行卸载
- 从安装目录中的 `uninstall.exe` 执行卸载
- 检查“保留用户数据”和“删除用户数据”两个分支
- 确认快捷方式被删除
- 确认注册表卸载项被删除

## 回归点

- 自定义安装路径不是 `Program Files` 时也能正常运行
- 安装目录放在中文路径下时仍可正常启动
- 首次启动没有依赖系统 Python / conda
- 更新失败时至少不会破坏当前可运行版本
