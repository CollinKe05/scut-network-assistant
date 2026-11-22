param([switch]$NoViewer, [switch]$Rebuild, [switch]$NetworkChangeConfirmed)
. (Join-Path $PSScriptRoot 'Common.ps1')
$settings=Get-CampusSettings
Confirm-CampusNetworkChange $NetworkChangeConfirmed.IsPresent $false
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$dockerExe=Get-DockerPath $settings

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
$serverHost=([Uri]$settings.VpnServerUrl).DnsSafeHost
$envPath=Join-Path $PSScriptRoot '.env'
$envText=[IO.File]::ReadAllText($envPath)
$oldHost=[regex]::Match($envText,'(?m)^VPN_HOST=(\S+)').Groups[1].Value
if($oldHost -ne $serverHost){$envText=[regex]::Replace($envText,'(?m)^VPN_ENDPOINT_IP=.*\r?\n?','')}
foreach($pair in @(@('VPN_HOST',$serverHost),@('VPN_SERVER_URL',$settings.VpnServerUrl))){
    $envText=[regex]::Replace($envText,'(?m)^'+$pair[0]+'=.*\r?\n?','')
    $envText=$envText.TrimEnd()+"`r`n"+$pair[0]+'='+$pair[1]+"`r`n"
}
[IO.File]::WriteAllText($envPath,$envText,[Text.Encoding]::ASCII)

# EasyConnect makes some requests to a resolved IP, without a domain/SNI.
# Fake-IP DNS from another proxy makes those requests return HTTP 403.
# Resolve only this public VPN endpoint over HTTPS, then scope the override to the container.
try {
    $dnsReply = & curl.exe --fail --silent --show-error --connect-timeout 5 --max-time 8 ('https://dns.alidns.com/resolve?name='+[Uri]::EscapeDataString(([Uri]$settings.VpnServerUrl).DnsSafeHost)+'&type=A')
    if ($LASTEXITCODE -ne 0) { throw 'Public DNS query failed.' }
    $dnsResult = $dnsReply | ConvertFrom-Json
    $vpnEndpointIp = ($dnsResult.Answer | Where-Object { $_.type -eq 1 -and $_.data -match '^(\d{1,3}\.){3}\d{1,3}$' } | Select-Object -First 1).data
    $parsedIp = $null
    if (-not [Net.IPAddress]::TryParse($vpnEndpointIp, [ref]$parsedIp) -or $vpnEndpointIp -match '^(127\.|10\.|192\.168\.|198\.(18|19)\.)') {
        throw 'Public DNS returned an invalid VPN endpoint.'
    }
    $envPath = Join-Path $PSScriptRoot '.env'
    $envText = [IO.File]::ReadAllText($envPath)
    if ($envText -match '(?m)^VPN_ENDPOINT_IP=.*$') {
        $envText = [regex]::Replace($envText, '(?m)^VPN_ENDPOINT_IP=.*\r?$', "VPN_ENDPOINT_IP=$vpnEndpointIp")
    } else {
        $envText = $envText.TrimEnd() + "`r`nVPN_ENDPOINT_IP=$vpnEndpointIp`r`n"
    }
    foreach ($pair in @(@('VPN_HOST',([Uri]$settings.VpnServerUrl).DnsSafeHost),@('VPN_SERVER_URL',$settings.VpnServerUrl))) {
        $envText=[regex]::Replace($envText,'(?m)^'+$pair[0]+'=.*\r?\n?','')
        $envText=$envText.TrimEnd()+"`r`n"+$pair[0]+'='+$pair[1]+"`r`n"
    }
    [IO.File]::WriteAllText($envPath, $envText, [Text.Encoding]::ASCII)
    Write-Host "VPN endpoint IPv4: $vpnEndpointIp (container override only)"
} catch {
    $cached=[regex]::Match([IO.File]::ReadAllText($envPath),'(?m)^VPN_ENDPOINT_IP=(\S+)').Groups[1].Value
    if(-not $cached){throw '无法解析当前 VPN 地址，也没有这个服务器的有效缓存。请检查网络、域名及 DNS 后重试；不会使用其它学校的地址。'}
    Write-Warning 'Could not refresh the public VPN endpoint. Using the last configured IP.'
}

$imageExists = & {
    $ErrorActionPreference = 'Continue'
    & $dockerExe --context desktop-linux image inspect campus-easyconnect:7.6.7-local --format '{{.Id}}' 2>$null | Out-Null
    $LASTEXITCODE -eq 0
}
if (-not $imageExists -or $Rebuild) {
    & $dockerExe --context desktop-linux compose --file compose.yaml --env-file .env build
    if ($LASTEXITCODE -ne 0) { throw 'Local compatibility image build failed.' }
}
$containerRunning = & {
    $ErrorActionPreference = 'Continue'
    & $dockerExe --context desktop-linux inspect --format '{{.State.Running}}' campus-easyconnect 2>$null
}
if ($containerRunning -eq 'true') {
    if ($Rebuild) { throw 'VPN 正在运行。请先明确确认并通过图形界面停止，然后再重建；当前会话保持不变。' }
    Write-Host '复用运行中的校园 VPN，会话保持不变。'
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
if (-not $vncReady) { throw 'VNC is not ready. Run docker --context desktop-linux logs campus-easyconnect.' }

Write-Host "RealVNC address: 127.0.0.1:5901"
Write-Host 'VNC 连接密码可在图形界面单独显示；校园密码只在登录页填写。'
Write-Host ('EasyConnect server: '+$settings.VpnServerUrl)
Write-Host 'After VPN login, run Open-Campus-Browser.cmd.'
if ($NoViewer) { return }

if ($settings.VncViewerPath -and (Test-Path -LiteralPath $settings.VncViewerPath)) {
    Start-Process -FilePath $settings.VncViewerPath -ArgumentList '127.0.0.1:5901'
} else { Start-Process 'http://127.0.0.1:6080/' }
