$ErrorActionPreference = 'Stop'
function Get-CampusSettings {
    $path = Join-Path $PSScriptRoot 'settings.json'
    if (-not (Test-Path -LiteralPath $path)) { throw '请先打开 Campus-Network.exe 完成首次配置向导。' }
    Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json
}
function Get-DockerPath($Settings) {
    if ($Settings.DockerPath -and (Test-Path -LiteralPath $Settings.DockerPath)) { return $Settings.DockerPath }
    $command = Get-Command docker.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($path in @((Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe'), (Join-Path $env:ProgramFiles 'Docker\Docker\resources\bin\docker.exe'))) {
        if (Test-Path -LiteralPath $path) { return $path }
    }
    throw '未找到 Docker Desktop。请在首次配置向导安装后重新检测。'
}
function Confirm-CampusNetworkChange([bool]$Confirmed, [bool]$NonInteractive) {
    if ($Confirmed) { return }
    if ($NonInteractive) { throw '此操作会切换网络路径，必须先明确确认；请在图形界面操作。' }
    $answer = Read-Host '此操作可能中断现有 SSH/代理连接。确认现在操作请输入 YES，其余输入取消'
    if ($answer -cne 'YES') { throw '已取消，现有网络未改动。' }
}
