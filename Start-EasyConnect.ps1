param([switch]$NoViewer, [switch]$Rebuild)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$dockerExe = (Get-Command docker.exe -ErrorAction SilentlyContinue).Source
if (-not $dockerExe) {
    $dockerExe = Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe'
}
if (-not (Test-Path -LiteralPath $dockerExe)) { throw 'Docker Desktop was not found.' }

function Test-DockerEngine {
    $ErrorActionPreference = 'Continue'
    & $dockerExe --context desktop-linux info --format '{{.ServerVersion}}' 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

if (-not (Test-DockerEngine)) {
    & $dockerExe desktop start
    if ($LASTEXITCODE -ne 0) { throw 'Could not start Docker Desktop.' }
    $engineReady = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if (Test-DockerEngine) { $engineReady = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $engineReady) { throw 'Docker Linux engine is not ready. Open Docker Desktop and retry.' }
}

if (-not (Test-Path -LiteralPath '.env')) {
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $randomBytes = New-Object byte[] 6; $rng.GetBytes($randomBytes) }
    finally { $rng.Dispose() }
    $vncPassword = [Convert]::ToBase64String($randomBytes)
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot '.env'), "VNC_PASSWORD=$vncPassword`r`n", [Text.Encoding]::ASCII)
}
$passwordLine = Get-Content -LiteralPath '.env' | Where-Object { $_ -match '^VNC_PASSWORD=' } | Select-Object -First 1
$vncPassword = ($passwordLine -split '=', 2)[1]
if (-not $vncPassword -or $vncPassword.Length -gt 8) { throw 'VNC_PASSWORD must contain 1 to 8 ASCII characters.' }

# EasyConnect makes some requests to a resolved IP, without a domain/SNI.
# Fake-IP DNS from another proxy makes those requests return HTTP 403.
# Resolve only this public VPN endpoint over HTTPS, then scope the override to the container.
try {
    $dnsReply = & curl.exe --fail --silent --show-error --connect-timeout 5 --max-time 8 'https://dns.alidns.com/resolve?name=sslvpn3.scut.edu.cn&type=A'
    if ($LASTEXITCODE -ne 0) { throw 'Public DNS query failed.' }
    $dnsResult = $dnsReply | ConvertFrom-Json
    $vpnEndpointIp = ($dnsResult.Answer | Where-Object { $_.type -eq 1 -and $_.data -match '^(\d{1,3}\.){3}\d{1,3}$' } | Select-Object -First 1).data
    $parsedIp = $null
    if (-not [Net.IPAddress]::TryParse($vpnEndpointIp, [ref]$parsedIp) -or $vpnEndpointIp -match '^(127\.|10\.|192\.168\.|198\.(18|19)\.)') {
        throw 'Public DNS returned an invalid VPN endpoint.'
    }
    $envPath = Join-Path $PSScriptRoot '.env'
    $envText = [IO.File]::ReadAllText($envPath)
    if ($envText -match '(?m)^SCUT_VPN_IP=.*$') {
        $envText = [regex]::Replace($envText, '(?m)^SCUT_VPN_IP=.*\r?$', "SCUT_VPN_IP=$vpnEndpointIp")
    } else {
        $envText = $envText.TrimEnd() + "`r`nSCUT_VPN_IP=$vpnEndpointIp`r`n"
    }
    [IO.File]::WriteAllText($envPath, $envText, [Text.Encoding]::ASCII)
    Write-Host "VPN endpoint IPv4: $vpnEndpointIp (container override only)"
} catch {
    Write-Warning 'Could not refresh the public VPN endpoint. Using the last configured IP.'
}

$imageExists = & {
    $ErrorActionPreference = 'Continue'
    & $dockerExe --context desktop-linux image inspect scut-easyconnect:7.6.7-local --format '{{.Id}}' 2>$null | Out-Null
    $LASTEXITCODE -eq 0
}
if (-not $imageExists -or $Rebuild) {
    & $dockerExe --context desktop-linux compose --file compose.yaml --env-file .env build
    if ($LASTEXITCODE -ne 0) { throw 'Local compatibility image build failed.' }
}
$containerRunning = & {
    $ErrorActionPreference = 'Continue'
    & $dockerExe --context desktop-linux inspect --format '{{.State.Running}}' scut-easyconnect 2>$null
}
if ($containerRunning -eq 'true' -and -not $Rebuild) {
    Write-Host 'Reusing the running EasyConnect container and its login session.'
} else {
    & $dockerExe --context desktop-linux compose --file compose.yaml --env-file .env up -d --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Container startup failed. Check Docker Desktop and port availability.' }
}

$vncReady = $false
for ($attempt = 0; $attempt -lt 45; $attempt++) {
    $tcp = New-Object Net.Sockets.TcpClient
    try {
        $connect = $tcp.ConnectAsync('127.0.0.1', 5901)
        if ($connect.Wait(1000) -and $tcp.Connected) {
            $stream = $tcp.GetStream()
            $stream.ReadTimeout = 1000
            $banner = New-Object byte[] 12
            $count = $stream.Read($banner, 0, 12)
            if ($count -ge 4 -and [Text.Encoding]::ASCII.GetString($banner, 0, $count).StartsWith('RFB ')) {
                $vncReady = $true
                break
            }
        }
    } catch {} finally { $tcp.Dispose() }
    Start-Sleep -Seconds 1
}
if (-not $vncReady) { throw 'VNC is not ready. Run docker --context desktop-linux logs scut-easyconnect.' }

Write-Host "RealVNC address: 127.0.0.1:5901"
Write-Host "VNC password: $vncPassword (local viewer only; NOT your SCUT password)"
Write-Host 'EasyConnect server: https://sslvpn3.scut.edu.cn'
Write-Host 'After VPN login, run Open-Campus-Browser.cmd.'
if ($NoViewer) { return }

$viewerPaths = @('D:\Program Files\RealVNC\VNC Viewer\vncviewer.exe',
    'C:\Program Files\RealVNC\VNC Viewer\vncviewer.exe',
    'C:\Program Files (x86)\RealVNC\VNC Viewer\vncviewer.exe')
$viewerExe = $viewerPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $viewerExe) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'RealVNC Viewer.lnk'
    if (Test-Path -LiteralPath $shortcutPath) {
        $shortcutShell = New-Object -ComObject WScript.Shell
        $viewerExe = $shortcutShell.CreateShortcut($shortcutPath).TargetPath
    }
}
if ($viewerExe -and (Test-Path -LiteralPath $viewerExe)) {
    Start-Process -FilePath $viewerExe -ArgumentList '127.0.0.1:5901'
} else {
    Write-Host 'Open RealVNC Viewer manually and connect to 127.0.0.1:5901.'
}
