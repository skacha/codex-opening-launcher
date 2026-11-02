$ErrorActionPreference = 'Stop'
$launcherPath = Join-Path $PSScriptRoot 'CodexOpening.exe'
if (-not (Test-Path -LiteralPath $launcherPath)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$desktopPath = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktopPath 'Codex 动画启动.lnk'
$shellObject = New-Object -ComObject WScript.Shell
if (Test-Path -LiteralPath $shortcutPath) {
    $existingShortcut = $shellObject.CreateShortcut($shortcutPath)
    if ($existingShortcut.TargetPath -ne $launcherPath) { throw '同名桌面快捷方式指向其他程序，未覆盖。' }
}
$shortcutObject = $shellObject.CreateShortcut($shortcutPath)
$shortcutObject.TargetPath = $launcherPath
$shortcutObject.WorkingDirectory = $PSScriptRoot
$shortcutObject.Description = '播放自定义图片或视频开场，然后进入 Codex；按 Esc 跳过。'
$shortcutObject.IconLocation = "$launcherPath,0"
$shortcutObject.Save()
$settingsShortcut = $shellObject.CreateShortcut((Join-Path $PSScriptRoot '开场设置.lnk'))
$settingsShortcut.TargetPath = $launcherPath
$settingsShortcut.Arguments = '--settings'
$settingsShortcut.WorkingDirectory = $PSScriptRoot
$settingsShortcut.Save()
Write-Output "已创建桌面入口：$shortcutPath"
& (Join-Path $PSScriptRoot 'prepare-taskbar.ps1')
Write-Output '任务栏入口文件已准备；是否固定必须通过 Windows 固定菜单及固定项实际目标核实。'
