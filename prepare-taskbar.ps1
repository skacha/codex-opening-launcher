$ErrorActionPreference = 'Stop'
$launcherPath = Join-Path $PSScriptRoot 'CodexOpening.exe'
if (-not (Test-Path -LiteralPath $launcherPath)) { throw '动画启动器不存在，请先构建。' }
$package = Get-AppxPackage -Name OpenAI.Codex
if (-not $package) { throw '未找到本机 Codex 商店应用。' }
$iconSource = Join-Path $package.InstallLocation 'app\resources\icon-chatgpt.ico'
$iconPath = Join-Path $PSScriptRoot 'assets\codex-app.ico'
if (Test-Path -LiteralPath $iconSource) { Copy-Item -LiteralPath $iconSource -Destination $iconPath -Force }
else { $iconPath = Join-Path $package.InstallLocation 'app\ChatGPT.exe' }
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $PSScriptRoot 'Codex.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $existingLink = $shortcutShell.CreateShortcut($shortcutPath)
    if ($existingLink.TargetPath -ne $launcherPath) { throw '已存在指向其他程序的 Codex 快捷方式，未覆盖。' }
}
$shortcut = $shortcutShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $launcherPath
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Description = '先播放 EVA 启动动画与音乐，然后进入 Codex。'
$shortcut.Save()
$backupFolder = Join-Path $PSScriptRoot 'artifacts\shortcut-backup'
New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
$originalPath = Join-Path $backupFolder 'Codex 原入口.lnk'
if (-not (Test-Path -LiteralPath $originalPath)) {
    $original = $shortcutShell.CreateShortcut($originalPath)
    $original.TargetPath = Join-Path $env:WINDIR 'explorer.exe'
    $original.Arguments = 'shell:AppsFolder\' + $package.PackageFamilyName + '!App'
    $original.IconLocation = "$iconPath,0"
    $original.Description = '恢复使用 Codex 商店应用原入口。'
    $original.Save()
}
# 固定与取消固定通过 Windows 的资源管理器菜单完成，准备文件不代表已固定。
Write-Output "待固定的入口：$shortcutPath"
Write-Output "原入口备份：$originalPath"
