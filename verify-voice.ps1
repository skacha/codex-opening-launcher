param([string]$Report = (Join-Path $PSScriptRoot 'artifacts\reference-manual.report.json'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$result = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
$reference = $result.referenceStep -gt 0
$cueCount = if ($reference) { 6 } else { 5 }
$voicePath = if ($reference) { 'assets\reference-voice\台词.json' } else { 'assets\voice\台词.json' }
$metadata = Get-Content -LiteralPath (Join-Path $root $voicePath) -Raw | ConvertFrom-Json
$settings = Get-Content -LiteralPath (Join-Path $root 'settings.json') -Raw | ConvertFrom-Json
if ($metadata.Language -ne 'en-US' -or $metadata.Metrics.Count -ne $cueCount) { throw '英文语音素材不完整。' }
$spokenSeconds = ($metadata.Metrics | Measure-Object -Property Seconds -Sum).Sum
$sequenceSeconds = [Math]::Max($settings.EvaSeconds, $spokenSeconds + 1.25)
$gap = ($sequenceSeconds - $spokenSeconds) / $cueCount
for ($index = 0; $index -lt $cueCount; $index++) {
    $metric = $metadata.Metrics[$index]
    if (-not $metric.Finite -or $metric.OutputPeak -ge 1 -or $metric.OutputRms -lt $metric.InputRms) { throw '音量或削波校验失败。' }
    if ($gap -lt 0.2) { throw '当前开场时长不足以播完英文台词。' }
}
if ($result.voiceCues -ne $cueCount -or $result.voiceCompleted -ne $cueCount -or -not $result.preview -or $result.launchRequested -or -not $result.userConfirmed -or -not $result.awaitedConfirmation) { throw '完整语音与点击确认预览验证失败。' }
if ($reference -and $result.referenceConfirmations -ne 2) { throw '原版前两步授权交互记录不完整。' }
Write-Output ('英文机械语音电平、无削波、完整播报和授权交互检查通过：'+$cueCount+' 段。')
