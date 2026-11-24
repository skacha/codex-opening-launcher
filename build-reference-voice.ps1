$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
if (-not ('VoiceProcessor' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'VoiceProcessor.cs') }
$voiceRoot = Join-Path $PSScriptRoot 'assets\reference-voice'
New-Item -ItemType Directory -Path $voiceRoot -Force | Out-Null
$synth = [System.Speech.Synthesis.SpeechSynthesizer]::new()
try {
    $selectedVoice = $synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'en-US' } | Select-Object -First 1
    if (-not $selectedVoice) { throw '未安装英文系统语音。' }
    $synth.SelectVoice($selectedVoice.VoiceInfo.Name)
    $synth.Rate = 0; $synth.Volume = 100
    $texts = @('Access permission required.','Identity verification.','Access request received.','Beginning permission read.','Read complete. Verification passed.','Welcome to Codex. Your session is ready.')
    $metrics = @()
    for ($index = 0; $index -lt $texts.Count; $index++) {
        $rawPath = Join-Path $PSScriptRoot ("artifacts\reference-voice-raw-$index.wav")
        $synth.SetOutputToWaveFile($rawPath); $synth.Speak($texts[$index]); $synth.SetOutputToNull()
        $metrics += [VoiceProcessor]::Process($rawPath, (Join-Path $voiceRoot ("cue-$index.wav")))
    }
    [pscustomobject]@{ Voice = $selectedVoice.VoiceInfo.Name; Language = 'en-US'; Texts = $texts; Metrics = $metrics; PostGain = 1.10 } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $voiceRoot '台词.json') -Encoding UTF8
} finally { $synth.Dispose() }
Write-Output '原版六阶段英文机械语音已生成。'
