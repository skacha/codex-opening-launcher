$ErrorActionPreference = 'Stop'
$artifactPath = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
$launcherPath = Join-Path $PSScriptRoot 'CodexOpening.exe'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixturePath = Join-Path $artifactPath 'TestFixtures.exe'
& $compilerPath /nologo /reference:System.Drawing.dll ("/out:$fixturePath") (Join-Path $PSScriptRoot 'TestFixtures.cs')
if ($LASTEXITCODE -ne 0) { throw '测试素材生成器编译失败。' }
& $fixturePath $artifactPath
$selfTest = Start-Process -FilePath $launcherPath -ArgumentList @('--self-test', '--output', ('"' + $artifactPath + '"')) -Wait -PassThru -WindowStyle Hidden
if ($selfTest.ExitCode -ne 0) { throw "配置自检失败：$($selfTest.ExitCode)" }
$render = Start-Process -FilePath $launcherPath -ArgumentList @('--render', '--output', ('"' + (Join-Path $artifactPath 'preview.png') + '"')) -Wait -PassThru -WindowStyle Hidden
if ($render.ExitCode -ne 0) { throw '开场渲染失败。' }

function Test-Playback([string]$Name, [string]$Mode, [string]$MediaPath, [string[]]$Expected, [bool]$Music = $true) {
    $testConfigPath = Join-Path $artifactPath "$Name.json"
    $testReportPath = Join-Path $artifactPath "$Name.report.json"
    @{ Enabled = $true; Mode = $Mode; MediaPath = $MediaPath; ImageSeconds = 1; EvaSeconds = 1; BgmEnabled = $Music; BgmVolume = 0.22; FadeSeconds = 0.1; Volume = 0; Fit = 'contain'; AppId = 'OpenAI.Codex_2p2nqsd0c76g0!App' } | ConvertTo-Json | Set-Content -LiteralPath $testConfigPath -Encoding UTF8
    if (Test-Path -LiteralPath $testReportPath) { Remove-Item -LiteralPath $testReportPath }
    $testProcess = Start-Process -FilePath $launcherPath -ArgumentList @('--preview', '--windowed', '--auto-confirm', '--config', ('"' + $testConfigPath + '"'), '--report', ('"' + $testReportPath + '"')) -PassThru -WindowStyle Hidden
    if (-not $testProcess.WaitForExit(18000)) { $testProcess.Kill(); throw "$Name 未按时关闭。" }
    if ($testProcess.ExitCode -ne 0) { throw "$Name 进程异常：$($testProcess.ExitCode)" }
    $report = Get-Content -LiteralPath $testReportPath -Raw | ConvertFrom-Json
    if ($report.launchRequested) { throw "$Name 预览不应打开 Codex。" }
    if ($Expected -notcontains $report.outcome) { throw "$Name 结果不符：$($report.outcome)" }
    if ($Mode -eq 'eva' -and $Music -and (-not $report.bgmOpened -or -not $report.bgmProgressed)) { throw "$Name 背景音乐未能完成解码并推进播放：$($report.bgmStatus)" }
    if (($Mode -ne 'eva' -or -not $Music) -and $report.bgmOpened) { throw "$Name 不应播放 EVA 背景音乐。" }
    [pscustomobject]@{ 场景 = $Name; 结果 = $report.outcome; 耗时毫秒 = $report.elapsedMs }
}
$results = @(
    Test-Playback 'builtin' 'builtin' '' @('图片动画播完')
    Test-Playback 'eva' 'eva' '' @('EVA动画播完')
    Test-Playback 'eva-muted' 'eva' '' @('EVA动画播完') $false
    Test-Playback 'image' 'image' (Join-Path $artifactPath '中文 素材 #1.png') @('图片动画播完')
    Test-Playback 'missing' 'image' (Join-Path $artifactPath '不存在.png') @('素材不存在')
)
[IO.File]::WriteAllText((Join-Path $artifactPath 'broken.mp4'), 'invalid media')
$results += Test-Playback 'invalid-video' 'video' (Join-Path $artifactPath 'broken.mp4') @('视频解码失败', '视频加载超时')
$results += Test-Playback 'video' 'video' (Join-Path $artifactPath 'motion.avi') @('视频播完')
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactPath 'verification.json') -Encoding UTF8
$results | Format-Table -AutoSize
Write-Output '配置自检、渲染和播放场景均通过；预览没有触发 Codex 启动。'
