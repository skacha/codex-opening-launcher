param([string]$PlanPath = 'F:\Codex\Migration\plan.json')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StorageMigration.psm1') -Force
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
$statePath = Join-Path $plan.MigrationRoot 'status.json'
$results = [Collections.Generic.List[object]]::new()
function Set-State([string]$State, [string]$Detail) {
    [pscustomobject]@{ State = $State; Detail = $Detail; UpdatedAt = (Get-Date).ToString('o'); Results = @($results.ToArray()) } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding UTF8
}
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Set-State '需要管理员授权' '请运行 F 盘的“完成 Codex 迁移”入口。'; exit 2 }
if ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne $plan.UserSid) { Set-State '需要原账户授权' '必须使用当前 Codex 所属账户的管理员授权，不能切换到其他管理员账户。'; exit 2 }
if (@(Get-Process ChatGPT,codex,CodexOpening -ErrorAction SilentlyContinue).Count -gt 0) { Set-State '等待退出 Codex' '会话或启动器仍在运行，未切换数据库。'; exit 3 }
$serviceName = 'CodexSandboxService.OpenAI.Codex'
try {
    Set-State '迁移中' '正在关闭沙箱服务并切换数据目录。'
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') { Stop-Service -Name $serviceName -ErrorAction Stop }
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(codex|codex-code-mode-host|codex-computer-use|codex-computer-use-swift)$' })
        if ($processes.Count -gt 0) { Start-Sleep -Seconds 1 }
    } while ($processes.Count -gt 0 -and (Get-Date) -lt $deadline)
    if ($processes.Count -gt 0) {
        # 主窗口和所有 CLI 已退出，只清理本账户运行时目录内的残留辅助进程。
        foreach ($process in $processes) {
            $processPath = $process.Path
            $runtimeSource = @($plan.DataMoves | Where-Object { $_.Name -eq '本地运行时' })[0].Source
            $runtimeTarget = @($plan.DataMoves | Where-Object { $_.Name -eq '本地运行时' })[0].Destination
            if ($process.ProcessName -eq 'codex' -or -not $processPath -or
                (-not $processPath.StartsWith($runtimeSource + '\', [StringComparison]::OrdinalIgnoreCase) -and -not $processPath.StartsWith($runtimeTarget + '\', [StringComparison]::OrdinalIgnoreCase))) {
                throw '仍存在未确认归属的 Codex 进程，请退出后重试。'
            }
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
    }
    # 文档与浏览器辅助进程可能以 node / pwsh 等通用名称运行；仅在所有聊天和 CLI 退出后按专属运行时路径清理。
    $runtimeRoots = @($plan.DataMoves | Where-Object { $_.Name -eq '本地运行时' -or $_.Name -eq '文档与工具运行时' } | ForEach-Object { $_.Source.TrimEnd('\') + '\' })
    foreach ($orphan in @(Get-Process -ErrorAction SilentlyContinue)) {
        if ($orphan.Id -eq $PID) { continue }
        try { $orphanPath = $orphan.Path } catch { continue }
        if (-not $orphanPath) { continue }
        foreach ($runtimeRoot in $runtimeRoots) {
            if ($orphanPath.StartsWith($runtimeRoot, [StringComparison]::OrdinalIgnoreCase)) { Stop-Process -Id $orphan.Id -Force -ErrorAction Stop; break }
        }
    }
    $allowed = @($plan.DataMoves | ForEach-Object { $_.Source })
    foreach ($move in $plan.DataMoves) {
        if (Test-Path -LiteralPath $move.Source) {
            $result = Move-MigrationTree -Source $move.Source -Destination $move.Destination -Backup $move.Backup -LogPath (Join-Path $plan.MigrationRoot 'copy.log') -AllowedSources $allowed -BackupMode
            $results.Add($result); Set-State '迁移中' ("已处理：" + $move.Name)
        }
    }
    [Environment]::SetEnvironmentVariable('CODEX_HOME', $plan.CodexHome, 'User')
    $env:CODEX_HOME = $plan.CodexHome
    $package = Get-AppxPackage -Name OpenAI.Codex
    if (-not $package) { throw '未找到 Codex 商店包。' }
    if (-not $package.InstallLocation.StartsWith('F:\', [StringComparison]::OrdinalIgnoreCase)) {
        Set-State '迁移中' '通过 Windows 包管理器迁移主程序及其受管应用数据。'
        $volume = Get-AppxVolume | Where-Object { $_.PackageStorePath -eq 'F:\WindowsApps' }
        if (-not $volume) { $volume = Add-AppxVolume -Path 'F:\WindowsApps' -ErrorAction Stop }
        Move-AppxPackage -Package $package.PackageFullName -Volume $volume -ErrorAction Stop
    }
    $package = Get-AppxPackage -Name OpenAI.Codex
    if (-not $package.InstallLocation.StartsWith('F:\', [StringComparison]::OrdinalIgnoreCase)) { throw '商店包实际安装位置仍不在 F 盘。' }
    $results.Add([pscustomobject]@{ Source = $plan.OriginalPackagePath; Destination = $package.InstallLocation; Status = 'Windows 商店包迁移完成' })
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { Start-Service -Name $serviceName -ErrorAction Stop }
    Set-State '已完成' '工作项目、Codex 主程序、会话数据、缓存和运行时已迁移到 F 盘；旧数据路径保留目录链接。'
    exit 0
} catch {
    Set-State '未全部完成' $_.Exception.Message
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { Start-Service -Name $serviceName -ErrorAction SilentlyContinue }
    exit 1
}
