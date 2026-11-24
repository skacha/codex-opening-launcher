param([string]$Report = (Join-Path $PSScriptRoot 'artifacts\confirmation-handoff.report.json'))
$ErrorActionPreference = 'Stop'
$result = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
if ($result.preview -or -not $result.launchRequested -or -not $result.launchSucceeded) { throw '不是有效的正式启动验证。' }
if (-not $result.sameMonitor -or $result.splashDevice -ne $result.displayDevice) { throw '动画与应用显示器不一致。' }
if ($result.introFinishedAtMs -le 0 -or $result.launchAtMs -lt $result.introFinishedAtMs) { throw '应用早于动画结束启动。' }
$cueCount = if ($result.referenceStep -gt 0) { 6 } else { 5 }
if ($result.voiceCues -ne $cueCount -or $result.voiceCompleted -ne $cueCount) { throw '启动语音未完整播完。' }
if (-not $result.userConfirmed -or -not $result.awaitedConfirmation) { throw '缺少真实用户确认交互。' }
if ($result.referenceStep -gt 0 -and $result.referenceConfirmations -ne 2) { throw '缺少原版两步授权记录。' }
Write-Output ('启动顺序、同屏交接和完整语音验证通过：' + $result.displayDevice)
