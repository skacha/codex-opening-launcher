param([switch]$Resume)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StorageMigration.psm1') -Force
$root = 'F:\Codex'
$sourceProject = 'D:\M3u8Downloader_H\download\codex-opening-launcher'
$targetProject = Join-Path $root 'Projects\codex-opening-launcher'
$migrationRoot = Join-Path $root 'Migration'
$planPath = Join-Path $migrationRoot 'plan.json'
$homeSource = Join-Path $env:USERPROFILE '.codex'
if (-not (Test-Path -LiteralPath 'F:\') -or (Get-Volume -DriveLetter F).FileSystem -ne 'NTFS') { throw 'F 盘必须是在线 NTFS 卷。' }
New-Item -ItemType Directory -Path $root -Force | Out-Null
Set-MigrationAcl $homeSource $root
New-Item -ItemType Directory -Path $migrationRoot -Force | Out-Null
if (Test-Path -LiteralPath $planPath) {
    if (-not $Resume) { throw '已有迁移计划，请先检查状态，不重复创建。' }
    $previousPlan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    if ($previousPlan.ProjectSource -ne $sourceProject -or $previousPlan.ProjectDestination -ne $targetProject) { throw '已有计划与本次路径不一致。' }
    $runId = $previousPlan.RunId
} else { $runId = Get-Date -Format 'yyyyMMdd-HHmmss' }
$backupRoot = Join-Path $root ('Backups\' + $runId)
$dataMoves = @(
    [pscustomobject]@{ Name = 'Codex 默认工作文件与会话'; Source = $homeSource; Destination = (Join-Path $root 'Data\home'); Backup = (Join-Path $backupRoot 'home') },
    [pscustomobject]@{ Name = '本地运行时'; Source = (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex'); Destination = (Join-Path $root 'Data\local-runtime'); Backup = (Join-Path $backupRoot 'local-runtime') },
    [pscustomobject]@{ Name = '桌面缓存'; Source = (Join-Path $env:APPDATA 'Codex'); Destination = (Join-Path $root 'Data\roaming'); Backup = (Join-Path $backupRoot 'roaming') },
    [pscustomobject]@{ Name = '启动器日志'; Source = (Join-Path $env:LOCALAPPDATA 'CodexOpeningLauncher'); Destination = (Join-Path $root 'Data\launcher-logs'); Backup = (Join-Path $backupRoot 'launcher-logs') },
    [pscustomobject]@{ Name = '文档与工具运行时'; Source = (Join-Path $env:USERPROFILE '.cache\codex-runtimes'); Destination = (Join-Path $root 'Data\tool-runtimes'); Backup = (Join-Path $backupRoot 'tool-runtimes') },
    [pscustomobject]@{ Name = '桌面日志'; Source = (Join-Path $env:LOCALAPPDATA 'Codex'); Destination = (Join-Path $root 'Data\desktop-logs'); Backup = (Join-Path $backupRoot 'desktop-logs') }
)
$package = Get-AppxPackage -Name OpenAI.Codex
$plan = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString('o'); RunId = $runId; MigrationRoot = $migrationRoot
    UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    CodexHome = (Join-Path $root 'Data\home'); Launcher = (Join-Path $targetProject 'CodexOpening.exe')
    ProjectSource = $sourceProject; ProjectDestination = $targetProject
    OriginalCodexHomeEnvironment = [Environment]::GetEnvironmentVariable('CODEX_HOME','User')
    OriginalPackagePath = $package.InstallLocation; DataMoves = $dataMoves
}
$plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $planPath -Encoding UTF8
$projectResult = Move-MigrationTree -Source $sourceProject -Destination $targetProject -Backup (Join-Path $backupRoot 'project') -LogPath (Join-Path $migrationRoot 'copy.log') -AllowedSources @($sourceProject)
$projectResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $migrationRoot 'project-result.json') -Encoding UTF8

# 更新现有快捷方式。保留原路径链接，可兼容 Windows 尚未刷新的固定项缓存。
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcutRoots = @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'), $targetProject)
$changed = [Collections.Generic.List[object]]::new()
foreach ($shortcutRoot in $shortcutRoots) {
    if (-not (Test-Path -LiteralPath $shortcutRoot)) { continue }
    foreach ($file in @(Get-ChildItem -LiteralPath $shortcutRoot -Filter '*.lnk' -File)) {
        $link = $shortcutShell.CreateShortcut($file.FullName)
        if ($link.TargetPath.StartsWith($sourceProject, [StringComparison]::OrdinalIgnoreCase)) {
            $oldTarget = $link.TargetPath
            $link.TargetPath = $targetProject + $oldTarget.Substring($sourceProject.Length)
            if ($link.WorkingDirectory.StartsWith($sourceProject, [StringComparison]::OrdinalIgnoreCase)) { $link.WorkingDirectory = $targetProject + $link.WorkingDirectory.Substring($sourceProject.Length) }
            $link.IconLocation = $link.IconLocation.Replace($sourceProject, $targetProject)
            $link.Arguments = $link.Arguments.Replace($sourceProject, $targetProject)
            $link.Save()
            $changed.Add([pscustomobject]@{ Path = $file.FullName; Before = $oldTarget; After = $link.TargetPath })
        }
    }
}
$changed.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $migrationRoot 'shortcuts.json') -Encoding UTF8
foreach ($documentName in @('README.md','交付文件.md')) {
    $documentPath = Join-Path $targetProject $documentName
    $body = [IO.File]::ReadAllText($documentPath).Replace($sourceProject, $targetProject)
    [IO.File]::WriteAllText($documentPath, $body, [Text.UTF8Encoding]::new($false))
}
$warmCopies = [Collections.Generic.List[object]]::new()
foreach ($move in $dataMoves) {
    try {
        if (Test-Path -LiteralPath $move.Source) {
            [void](Copy-MigrationTree -Source $move.Source -Destination $move.Destination -LogPath (Join-Path $migrationRoot 'warm-copy.log'))
            $warmCopies.Add([pscustomobject]@{ Source = $move.Source; Destination = $move.Destination; Status = '热复制完成，尚未切换' })
        }
    } catch { $warmCopies.Add([pscustomobject]@{ Source = $move.Source; Destination = $move.Destination; Status = '热复制不完整；退出后重新复制' }) }
}
$warmCopies.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $migrationRoot 'warm-copy.json') -Encoding UTF8
$privateScripts = Join-Path $migrationRoot 'scripts'
New-Item -ItemType Directory -Path $privateScripts -Force | Out-Null
Set-MigrationAcl $homeSource $privateScripts
Get-ChildItem -LiteralPath (Join-Path $targetProject 'migration') -File | Where-Object { $_.Extension -in @('.ps1','.psm1') } | Copy-Item -Destination $privateScripts -Force
$completionLink = $shortcutShell.CreateShortcut((Join-Path $root '完成 Codex 迁移.lnk'))
$completionLink.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$completionLink.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $privateScripts 'Wait-ForExit.ps1') + '"'
$completionLink.WorkingDirectory = $migrationRoot
$completionLink.Description = '等待退出 Codex 后，请求管理员授权完成 F 盘迁移。'
$completionLink.Save()
[pscustomobject]@{ State = '项目已迁移，等待最终切换'; Detail = 'C 盘数据热复制已准备，数据库和商店包尚未切换。'; UpdatedAt = (Get-Date).ToString('o'); Project = $projectResult } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $migrationRoot 'status.json') -Encoding UTF8
Write-Output '启动动画项目已迁移到 F 盘，原位置保留目录链接。C 盘数据仅完成热复制，仍等待退出应用后最终切换。'
