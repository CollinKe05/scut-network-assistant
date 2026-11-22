$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Split-Path -Parent $PSScriptRoot
$payload=Join-Path $PSScriptRoot 'payload.zip'
$files=@('Common.ps1','Launch-SCUT.ps1','Start-EasyConnect.ps1','Check-Campus.ps1','Open-Campus-Browser.ps1','Connect-Server.ps1','campus-route.py','network-manager.py','ssh-socks-bridge.py','compose.yaml','Dockerfile','compat-ps.sh','.dockerignore','settings.example.json','network-rules.json','clash-scut.js','README.md','图形界面使用说明.md','Termius配置.md')
$stream=[IO.File]::Open($payload,[IO.FileMode]::Create)
$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($name in $files) {
        $path=Join-Path $root $name
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$path,$name,[IO.Compression.CompressionLevel]::Optimal)
    }
} finally {$zip.Dispose();$stream.Dispose()}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    (('/win32manifest:')+(Join-Path $PSScriptRoot 'app.manifest')) `
    (('/win32icon:')+(Join-Path $PSScriptRoot 'network.ico')) `
    (('/resource:')+$payload+',Campus.Payload.zip') `
    (('/out:')+(Join-Path $root 'Campus-Network.exe')) `
    (Join-Path $PSScriptRoot 'NetworkAssistant.cs') `
    (Join-Path $PSScriptRoot 'SetupWizard.cs') `
    (Join-Path $PSScriptRoot 'RuntimePack.cs')
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
Write-Output 'BUILD_OK: Campus-Network.exe (self-extracting runtime)'
