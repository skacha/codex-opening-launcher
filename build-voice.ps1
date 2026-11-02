$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
if (-not ('VoiceProcessor' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'VoiceProcessor.cs') }
$voiceRoot = Join-Path $PSScriptRoot 'assets\voice'
New-Item -ItemType Directory -Path $voiceRoot -Force | Out-Null
$synth = [System.Speech.Synthesis.SpeechSynthesizer]::new()
try {
    $voice = $synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'en-US' } | Select-Object -First 1
    if (-not $voice) { throw '未安装英文系统语音。' }
    $synth.SelectVoice($voice.VoiceInfo.Name)
    $synth.Rate = 0
    $synth.Volume = 100
    $texts = @('Codex. Boot sequence initiated.','Command interface. Connection verified.','Workspace link established. Synchronizing.','Synchronization complete. All systems nominal.','Codex online. Awaiting command.')
    $metrics = @()
    for ($index = 0; $index -lt $texts.Count; $index++) {
        $rawPath = Join-Path $PSScriptRoot ("artifacts\voice-raw-$index.wav")
        $synth.SetOutputToWaveFile($rawPath)
        $synth.Speak($texts[$index])
        $synth.SetOutputToNull()
        $metrics += [VoiceProcessor]::Process($rawPath, (Join-Path $voiceRoot ("cue-$index.wav")))
    }
    [pscustomobject]@{ Voice = $voice.VoiceInfo.Name; Language = 'en-US'; Texts = $texts; Metrics = $metrics; PostGain = 1.10; Processing = '轻微降调；38 Hz 调制；短延迟立体声叠声；压缩与归一化后再提高 10% 音量' } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $voiceRoot '台词.json') -Encoding UTF8
} finally { $synth.Dispose() }
Write-Output 'Codex 英文机械风启动语音已生成并归一化。'
