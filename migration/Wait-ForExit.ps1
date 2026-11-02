param([string]$PlanPath = 'F:\Codex\Migration\plan.json')
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
$statusPath = Join-Path $plan.MigrationRoot 'status.json'
function Set-WaitState([string]$State, [string]$Detail) {
    [pscustomobject]@{ State = $State; Detail = $Detail; UpdatedAt = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
}
$owned = $false
$mutex = [Threading.Mutex]::new($true, 'Local\Codex-F-Storage-Migration', [ref]$owned)
if (-not $owned) { $mutex.Dispose(); exit 0 }
try {
    Set-WaitState '等待退出 Codex' '工作项目已迁到 F 盘；退出所有 Codex 窗口后将请求 Windows 管理员授权，完成数据库与主程序切换。'
    $deadline = (Get-Date).AddHours(2)
    while (@(Get-Process ChatGPT,codex,CodexOpening -ErrorAction SilentlyContinue).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { Set-WaitState '等待手动继续' '两小时内未退出；请双击“完成 Codex 迁移”继续。'; exit 4 }
        Start-Sleep -Seconds 3
    }
    Set-WaitState '等待管理员授权' '请在 Windows 授权窗口允许迁移操作。'
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + (Join-Path $PSScriptRoot 'Complete-Migration.ps1') + '"'), '-PlanPath', ('"' + $PlanPath + '"'))
    $child = Start-Process -FilePath $windowsPowerShell -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ($child.ExitCode -ne 0) { exit $child.ExitCode }
    # 由未提升权限的等待器重新打开动画入口，避免让聊天应用以管理员权限运行。
    Start-Process -FilePath $plan.Launcher -WorkingDirectory ([IO.Path]::GetDirectoryName($plan.Launcher)) -WindowStyle Normal
} catch { Set-WaitState '等待手动继续' '管理员授权未完成；请双击“完成 Codex 迁移”重试。'; exit 1 }
finally { if ($owned) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
