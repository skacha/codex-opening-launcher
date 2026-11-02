param([string]$Ffmpeg = 'D:\M3u8Downloader_H\ffmpeg.exe', [ValidateRange(0,120)][double]$StartSeconds = 0, [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Name = 'cinematic')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Ffmpeg)) { throw '请指定已安装的 ffmpeg.exe 路径。' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$artifactPath = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
$references = @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$sources = @('Launcher.cs','EvaBootScene.cs','DisplayHandoff.cs','BootTimeline.cs','MotionPreview.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$exporter = Join-Path $artifactPath 'MotionPreview.exe'
& $compilerPath /nologo /target:exe /platform:x64 /optimize+ /main:CodexOpening.MotionPreview /reference:System.Web.Extensions.dll /reference:System.Xaml.dll ("/out:$exporter") @references @sources
if ($LASTEXITCODE -ne 0) { throw '动效导出器编译失败。' }
& $exporter $PSScriptRoot $Ffmpeg (Join-Path $artifactPath ($Name + '-preview.mp4')) ($StartSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
if ($LASTEXITCODE -ne 0) { throw '动效视频导出失败。' }
