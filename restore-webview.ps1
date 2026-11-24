$ErrorActionPreference = 'Stop'
# 固定官方 SDK 版本和摘要；所有下载、解压和运行缓存均保存在项目所在磁盘。
$sdkVersion = '1.0.3650.58'
$sdkHash = '911A472128C82AC8BAA0C486C23342CC9DD6E7DC50D754E676726642CA065C60'
$sdkRoot = Join-Path $PSScriptRoot 'artifacts\webview2-sdk'
$packagePath = Join-Path $PSScriptRoot 'artifacts\webview2-sdk.nupkg'
$corePath = Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.Core.dll'
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'))) {
    New-Item -ItemType Directory -Path (Split-Path $packagePath) -Force | Out-Null
    if (-not (Test-Path -LiteralPath $packagePath)) {
        Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg" -OutFile $packagePath
    }
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne $sdkHash) { throw 'WebView2 SDK 文件摘要不匹配。' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $sdkRoot)
}
Copy-Item -LiteralPath (Join-Path $sdkRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll') -Destination $corePath
Copy-Item -LiteralPath (Join-Path $sdkRoot 'lib\net462\Microsoft.Web.WebView2.Wpf.dll') -Destination (Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.Wpf.dll')
Copy-Item -LiteralPath (Join-Path $sdkRoot 'runtimes\win-x64\native\WebView2Loader.dll') -Destination (Join-Path $PSScriptRoot 'WebView2Loader.dll')
