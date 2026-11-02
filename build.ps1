$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/reference:System.Web.Extensions.dll', '/reference:System.Xaml.dll', ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('/out:' + (Join-Path $PSScriptRoot 'CodexOpening.exe'))) + $references + @((Join-Path $PSScriptRoot 'Launcher.cs'), (Join-Path $PSScriptRoot 'EvaBootScene.cs'), (Join-Path $PSScriptRoot 'DisplayHandoff.cs'), (Join-Path $PSScriptRoot 'BootTimeline.cs'))
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw '启动器编译失败。' }
$artifactPath = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
$synthPath = Join-Path $artifactPath 'BgmSynth.exe'
& $compilerPath /nologo /target:exe ("/out:$synthPath") (Join-Path $PSScriptRoot 'BgmSynth.cs')
if ($LASTEXITCODE -ne 0) { throw '背景音乐生成器编译失败。' }
& $synthPath (Join-Path $PSScriptRoot 'assets\eva-terminal.wav')
if ($LASTEXITCODE -ne 0) { throw '背景音乐生成失败。' }
Write-Output '启动器编译成功。'
& (Join-Path $PSScriptRoot 'build-voice.ps1')
