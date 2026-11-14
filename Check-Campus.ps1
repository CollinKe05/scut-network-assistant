$ErrorActionPreference = 'Stop'
& docker --context desktop-linux inspect --format 'Container running: {{.State.Running}}' scut-easyconnect
if ($LASTEXITCODE -ne 0) { throw 'Start EasyConnect first.' }
& docker --context desktop-linux exec scut-easyconnect sh -c 'ip -4 addr show dev tun0 2>/dev/null'
if ($LASTEXITCODE -ne 0) { Write-Warning 'VPN tunnel is not ready. Complete the login in RealVNC.' }

foreach ($targetUrl in @('https://video.jw.scut.edu.cn/')) {
    Write-Host "`nSOCKS5 -> $targetUrl"
    & curl.exe --proxy socks5h://127.0.0.1:1080 --noproxy 'localhost,127.0.0.1' --connect-timeout 8 --max-time 20 --silent --show-error --head $targetUrl
    if ($LASTEXITCODE -ne 0) { Write-Warning "Request failed (curl exit code $LASTEXITCODE)." }
    Write-Host "`nHTTP proxy -> $targetUrl"
    & curl.exe --proxy http://127.0.0.1:8888 --noproxy 'localhost,127.0.0.1' --connect-timeout 8 --max-time 20 --silent --show-error --head $targetUrl
    if ($LASTEXITCODE -ne 0) { Write-Warning "Request failed (curl exit code $LASTEXITCODE)." }
}

$pythonExe = 'C:\Users\19653\miniforge3\python.exe'
if (Test-Path -LiteralPath $pythonExe) {
    Write-Host "`nA800 SSH -> 10.48.12.129:22"
    & docker --context desktop-linux exec scut-easyconnect ip route replace 10.48.12.129/32 dev tun0
    if ($LASTEXITCODE -eq 0) {
        & $pythonExe (Join-Path $PSScriptRoot 'ssh-socks-bridge.py') 10.48.12.129 22 --probe --timeout 10
    }
}
