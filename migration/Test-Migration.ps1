$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StorageMigration.psm1') -Force
$testId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$testRoot = Join-Path 'F:\Codex\Migration\SelfTest' $testId
$source = Join-Path $testRoot 'source'
$destination = Join-Path $testRoot 'destination'
$backup = Join-Path 'F:\Codex\Backups' ('self-test-' + $testId)
New-Item -ItemType Directory -Path (Join-Path $source '子目录') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source '子目录\中文 空格.txt'), '仅用于验证迁移，不包含用户数据。', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllBytes((Join-Path $source 'empty.bin'), [byte[]]@())
$result = Move-MigrationTree -Source $source -Destination $destination -Backup $backup -LogPath (Join-Path $testRoot 'copy.log') -AllowedSources @($source)
if ((Get-Item -LiteralPath $source -Force).LinkType -ne 'Junction') { throw '目录链接未建立。' }
[void](Assert-TreeEqual $destination $backup)
$repeat = Move-MigrationTree -Source $source -Destination $destination -Backup $backup -LogPath (Join-Path $testRoot 'copy.log') -AllowedSources @($source)
if ($repeat.Status -ne '已迁移') { throw '幂等检查失败。' }
$mismatchCaught = $false
[IO.File]::AppendAllText((Join-Path $backup 'empty.bin'), '故意破坏测试副本。')
try { [void](Assert-TreeEqual $destination $backup) } catch { $mismatchCaught = $true }
if (-not $mismatchCaught) { throw '未检测到内容不一致。' }
$pathGuardCaught = $false
try { [void](Assert-ExactPath 'C:\' @('C:\')) } catch { $pathGuardCaught = $true }
if (-not $pathGuardCaught) { throw '未阻止磁盘根目录操作。' }
[pscustomobject]@{ State = '自检通过'; Cases = @('中文路径与空文件','逐文件校验','F 盘回滚副本','旧路径目录链接','重复执行保护','损坏副本检测','根目录保护'); TestRoot = $testRoot } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding UTF8
Write-Output "迁移自检通过：$testRoot"
