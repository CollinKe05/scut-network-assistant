$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temp=Join-Path ([IO.Path]::GetTempPath()) ('campus-launch-guard-'+[Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $temp)
foreach($file in @('Launch-SCUT.ps1','Common.ps1')){Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $temp $file)}
# An existing file satisfies only the Python-path check. The confirmation guard must
# reject the launch before this fake tool, Docker or Clash can be executed.
@{PythonPath=(Join-Path $PSHOME 'powershell.exe')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $temp 'settings.json') -Encoding UTF8
$output=& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $temp 'Launch-SCUT.ps1') -VpnOnly -NoPause 2>&1
if($LASTEXITCODE -ne 1 -or ($output -join ' ') -match '\[1/4\]'){throw 'Network confirmation guard did not reject the noninteractive launch.'}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /codepage:65001 /target:exe /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll `
    (('/reference:')+(Join-Path $root 'Campus-Network.exe')) (('/out:')+(Join-Path $root 'OnboardingTests.exe')) (Join-Path $PSScriptRoot 'Test-Onboarding.cs')
if($LASTEXITCODE -ne 0){throw 'Test harness compile failed.'}
& (Join-Path $root 'OnboardingTests.exe') (Join-Path $temp 'fresh-user')
if($LASTEXITCODE -ne 0){throw 'Onboarding tests failed.'}
Write-Output 'DISTRIBUTION_GUARD_AND_WIZARD_TESTS_OK'
