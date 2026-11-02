# F 盘迁移

## 范围

仅迁移本聊天的启动动画项目和 Codex 默认工作文件；不移动下载视频，也不移动 E 盘的 qa-5 项目。

- 工作项目：`F:\Codex\Projects\codex-opening-launcher`
- Codex 主数据：`F:\Codex\Data\home`
- 本地运行时：`F:\Codex\Data\local-runtime`
- 文档与工具运行时：`F:\Codex\Data\tool-runtimes`
- 桌面缓存：`F:\Codex\Data\roaming`
- 桌面日志：`F:\Codex\Data\desktop-logs`
- 启动器日志：`F:\Codex\Data\launcher-logs`
- 商店版主程序：Windows 包管理器管理的 `F:\WindowsApps`。
- 迁移计划、进度和记录：`F:\Codex\Migration`。
- 受保护的离线执行脚本副本：`F:\Codex\Migration\scripts`。
- F 盘回滚副本：`F:\Codex\Backups`，按迁移批次保存。

旧路径保留 NTFS 目录链接，使已有会话、插件和快捷方式仍可解析；链接本身、Windows 注册信息、系统凭据库和系统共享组件仍由 Windows 保存在系统盘，不能承诺 C 盘字节级零占用。不会更改整机 TEMP/TMP 或其他应用的存储位置。

## 完成迁移

项目迁移可在当前聊天中完成。数据库、运行时和商店包必须在退出 Codex 后处理，避免迁移正在写入的 SQLite 数据库。

后台等待器在所有 Codex 窗口和 CLI 退出后请求 Windows 管理员授权，再执行最终迁移。请在系统授权框中由本人允许；程序不会绕过授权。如果两小时内未退出，或取消了授权，请双击 F 盘的“完成 Codex 迁移”入口重试。

以 `F:\Codex\Migration\status.json` 的 `State` 为准。只有写入“已完成”且商店包实际安装路径位于 F 盘，才表示全部可迁移项目已完成。热复制只是准备副本，不代表已释放 C 盘空间。

## 校验与安全

每个普通数据目录先复制、逐文件 SHA-256 校验，再复制并校验 F 盘回滚副本。随后将源目录改名、建立旧路径到 F 盘的目录链接，最后才清理原磁盘上的旧副本。任一校验失败会保留原数据，不输出配置或凭据内容。目标目录沿用原访问权限并关闭宽泛继承。

商店包仅使用 `Add-AppxVolume` / `Move-AppxPackage`，不接管 WindowsApps 权限、不手工搬动包文件。Windows 包管理器同时负责受管应用数据的迁移。

## 回滚

退出 Codex 后，可将商店包通过 Windows 应用设置移回 C 盘，或在管理员 PowerShell 中使用 `Move-AppxPackage` 指向 C 盘现有卷。

普通数据目录回滚时应先把 F 盘当前数据复制回源路径，不要直接恢复旧快照覆盖迁移后产生的新会话。旧路径是目录链接，不能对它递归删除；只移除链接节点，再恢复目录。需要完整回滚时请让 Codex 检查 `plan.json` 和最新 `status.json` 后执行，防止恢复旧数据覆盖新数据。

迁移代码：`StorageMigration.psm1` 的 `Move-MigrationTree`；后台等待：`Wait-ForExit.ps1`；最终切换：`Complete-Migration.ps1`。
