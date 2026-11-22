. (Join-Path $PSScriptRoot 'Common.ps1')
$settings=Get-CampusSettings
& $settings.PythonPath (Join-Path $PSScriptRoot 'campus-route.py') status --probe
