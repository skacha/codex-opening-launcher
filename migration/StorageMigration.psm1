Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ExactPath([string]$Path, [string[]]$Allowed) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($Allowed -notcontains $full) { throw "路径不在迁移清单内：$full" }
    if ($full -match '^[A-Za-z]:$') { throw '拒绝对磁盘根目录执行迁移。' }
    return $full
}

function Set-MigrationAcl([string]$Source, [string]$Destination, [bool]$ProtectRoot = $true) {
    $sourceAcl = Get-Acl -LiteralPath $Source
    $sourceItem = Get-Item -LiteralPath $Source -Force
    if ($sourceItem.PSIsContainer) { $acl = [Security.AccessControl.DirectorySecurity]::new() }
    else { $acl = [Security.AccessControl.FileSecurity]::new() }
    $acl.SetSecurityDescriptorSddlForm($sourceAcl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access), [Security.AccessControl.AccessControlSections]::Access)
    # 在目标上保留原有授权，阻断 F 盘根目录的宽泛继承。
    $acl.SetAccessRuleProtection(($ProtectRoot -or $sourceAcl.AreAccessRulesProtected), $true)
    # 仅写访问控制列表，不复制系统审计规则，普通用户不需要审计权限。
    $folder = Get-Item -LiteralPath $Destination -Force
    if ('System.IO.FileSystemAclExtensions' -as [type]) { [IO.FileSystemAclExtensions]::SetAccessControl($folder, $acl) }
    else { $folder.SetAccessControl($acl) }
}

function Get-TreeInventory([string]$Root) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $links = @(Get-ChildItem -LiteralPath $rootPath -Attributes ReparsePoint -Recurse -Force -ErrorAction Stop)
    if ($links.Count -gt 0) { throw "目录中存在未纳入计划的链接，停止迁移：$rootPath" }
    $files = @(Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force -ErrorAction Stop)
    $entries = @{}
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($rootPath.Length + 1)
        $entries[$relative] = [pscustomobject]@{ Length = $file.Length; Hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
    return $entries
}

function Assert-TreeEqual([string]$Source, [string]$Destination) {
    $original = Get-TreeInventory $Source
    $copied = Get-TreeInventory $Destination
    if ($original.Count -ne $copied.Count) { throw "文件数量不一致：$Source" }
    foreach ($name in $original.Keys) {
        if (-not $copied.ContainsKey($name) -or $original[$name].Length -ne $copied[$name].Length -or $original[$name].Hash -ne $copied[$name].Hash) {
            throw "文件内容校验失败：$Source（未输出文件内容）"
        }
    }
    return $original.Count
}

function Copy-MigrationTree([string]$Source, [string]$Destination, [string]$LogPath, [switch]$BackupMode) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Set-MigrationAcl $Source $Destination
    $copyMode = if ($BackupMode) { '/COPY:DATS' } else { '/COPY:DAT' }
    $copyArgs = @($Source, $Destination, '/E', $copyMode, '/DCOPY:DAT', '/XJ', '/R:1', '/W:1', '/NFL', '/NDL', '/NP', '/NJH', '/NJS', ("/LOG+:$LogPath"))
    if ($BackupMode) { $copyArgs += '/B' }
    & robocopy.exe @copyArgs | Out-Null
    $copyExit = $LASTEXITCODE
    if ($copyExit -ge 8) { throw "复制未完成，保留原目录：$Source（robocopy=$copyExit）" }
    if (-not $BackupMode) {
        # 普通用户逐项保留访问规则，不要求复制所有者或系统审计权限。
        $prefix = [IO.Path]::GetFullPath($Source).TrimEnd('\')
        foreach ($entry in @(Get-ChildItem -LiteralPath $Source -Recurse -Force -ErrorAction Stop)) {
            $relative = $entry.FullName.Substring($prefix.Length + 1)
            Set-MigrationAcl $entry.FullName (Join-Path $Destination $relative) $false
        }
    }
    return $copyExit
}

function Remove-ValidatedTree([string]$Path, [string]$ExactAllowed) {
    $checked = Assert-ExactPath $Path @($ExactAllowed)
    $item = Get-Item -LiteralPath $checked -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '拒绝递归删除链接。' }
    if (@(Get-ChildItem -LiteralPath $checked -Attributes ReparsePoint -Recurse -Force).Count -gt 0) { throw '待清理备份中出现链接，拒绝删除。' }
    Remove-Item -LiteralPath $checked -Recurse -Force
}

function Move-MigrationTree {
    param([string]$Source, [string]$Destination, [string]$Backup, [string]$LogPath, [string[]]$AllowedSources, [switch]$BackupMode)
    $sourcePath = Assert-ExactPath $Source $AllowedSources
    $destinationPath = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    $backupPath = [IO.Path]::GetFullPath($Backup).TrimEnd('\')
    if (-not $destinationPath.StartsWith('F:\Codex\', [StringComparison]::OrdinalIgnoreCase) -or -not $backupPath.StartsWith('F:\Codex\Backups\', [StringComparison]::OrdinalIgnoreCase)) { throw '目标或备份路径不在指定的 F 盘目录。' }
    $sourceItem = Get-Item -LiteralPath $sourcePath -Force
    if ($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        if ($sourceItem.LinkType -eq 'Junction' -and [IO.Path]::GetFullPath([string]$sourceItem.Target).TrimEnd('\') -eq $destinationPath) {
            return [pscustomobject]@{ Source = $sourcePath; Destination = $destinationPath; Status = '已迁移'; Files = $null }
        }
        throw '原目录已是其他链接，未覆盖。'
    }
    # 关闭应用后清除热复制目标中的旧文件，防止复活已删除的 SQLite WAL。
    if (Test-Path -LiteralPath $destinationPath) { Remove-ValidatedTree $destinationPath $destinationPath }
    [void](Copy-MigrationTree $sourcePath $destinationPath $LogPath -BackupMode:$BackupMode)
    $fileCount = Assert-TreeEqual $sourcePath $destinationPath
    if (Test-Path -LiteralPath $backupPath) { throw '备份目标已存在，拒绝覆盖。' }
    [void](Copy-MigrationTree $sourcePath $backupPath $LogPath -BackupMode:$BackupMode)
    [void](Assert-TreeEqual $sourcePath $backupPath)
    $retired = $sourcePath + '.codex-f-migration-retired'
    if (Test-Path -LiteralPath $retired) { throw '检测到未处理的旧目录，停止切换。' }
    [void](Assert-ExactPath $retired @($sourcePath + '.codex-f-migration-retired'))
    Rename-Item -LiteralPath $sourcePath -NewName ([IO.Path]::GetFileName($retired))
    try {
        New-Item -ItemType Junction -Path $sourcePath -Target $destinationPath | Out-Null
        $newLink = Get-Item -LiteralPath $sourcePath -Force
        if ($newLink.LinkType -ne 'Junction') { throw '目录链接验证失败。' }
    } catch {
        if (Test-Path -LiteralPath $sourcePath) {
            $failedLink = Get-Item -LiteralPath $sourcePath -Force
            if ($failedLink.LinkType -eq 'Junction') { [IO.Directory]::Delete($sourcePath) }
        }
        Rename-Item -LiteralPath $retired -NewName ([IO.Path]::GetFileName($sourcePath))
        throw
    }
    try { Remove-ValidatedTree $retired $retired }
    catch { throw "目标已经迁移，但旧副本未能清理：$retired" }
    return [pscustomobject]@{ Source = $sourcePath; Destination = $destinationPath; Backup = $backupPath; Status = '迁移并校验完成'; Files = $fileCount }
}

Export-ModuleMember -Function Assert-ExactPath,Set-MigrationAcl,Get-TreeInventory,Assert-TreeEqual,Copy-MigrationTree,Move-MigrationTree
