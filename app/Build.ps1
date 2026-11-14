$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    (('/win32manifest:') + (Join-Path $PSScriptRoot 'app.manifest')) `
    (('/win32icon:') + (Join-Path $PSScriptRoot 'network.ico')) `
    (('/out:') + (Join-Path $root 'SCUT-Network.exe')) `
    (Join-Path $PSScriptRoot 'NetworkAssistant.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'BUILD_OK: SCUT-Network.exe'
