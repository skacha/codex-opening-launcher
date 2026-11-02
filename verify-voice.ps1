param([string]$Report = (Join-Path $PSScriptRoot 'artifacts\confirmation-preview.report.json'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$metadata = Get-Content -LiteralPath (Join-Path $root 'assets\voice\台词.json') -Raw | ConvertFrom-Json
$settings = Get-Content -LiteralPath (Join-Path $root 'settings.json') -Raw | ConvertFrom-Json
if ($metadata.Language -ne 'en-US' -or $metadata.Metrics.Count -ne 5) { throw '英文语音素材不完整。' }
$spokenSeconds = ($metadata.Metrics | Measure-Object -Property Seconds -Sum).Sum
$sequenceSeconds = [Math]::Max($settings.EvaSeconds, $spokenSeconds + 1.25)
$gap = ($sequenceSeconds - $spokenSeconds) / 5
for ($index = 0; $index -lt 5; $index++) {
    $metric = $metadata.Metrics[$index]
    if (-not $metric.Finite -or $metric.OutputPeak -ge 1 -or $metric.OutputRms -lt $metric.InputRms) { throw '音量或削波校验失败。' }
    if ($gap -lt 0.2) { throw '当前开场时长不足以播完英文台词。' }
}
$scene = [IO.File]::ReadAllText((Join-Path $root 'EvaBootScene.cs'))
if ($scene -match '(?m)^\s*(Label|CenterLabel|CenterAt|Center)\([^\r\n]*"[^"\r\n]*[\u4e00-\u9fff]') { throw '启动画面存在未转换的中文标签。' }
$result = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
if ($result.voiceCues -ne 5 -or $result.voiceCompleted -ne 5 -or -not $result.preview -or $result.launchRequested -or -not $result.userConfirmed -or -not $result.awaitedConfirmation) { throw '完整语音与点击确认预览验证失败。' }
Write-Output '五段英文语音、机械效果电平、无削波、阶段时长及英文画面检查通过。'
