. (Join-Path $PSScriptRoot 'Common.ps1')
$settings=Get-CampusSettings
Start-Process $settings.ProbeUrl
