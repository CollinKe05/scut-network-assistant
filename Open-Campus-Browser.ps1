$ErrorActionPreference = 'Stop'
$browserPaths = @(
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
)
$browserExe = $browserPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $browserExe) { throw 'Microsoft Edge was not found.' }

$containerInfo = & docker --context desktop-linux inspect --format '{{.State.Running}}' scut-easyconnect 2>$null
if ($LASTEXITCODE -ne 0 -or $containerInfo -ne 'true') { throw 'Start EasyConnect and log in through RealVNC first.' }
$tunnelState = & docker --context desktop-linux exec scut-easyconnect sh -c 'ip -4 addr show dev tun0 2>/dev/null'
if ($LASTEXITCODE -ne 0 -or ($tunnelState -join ' ') -notmatch 'inet ') {
    Write-Warning 'VPN login is not confirmed. Complete the login in RealVNC first.'
}

# An independent profile is required; otherwise an existing Edge process can ignore proxy flags.
$profilePath = Join-Path $PSScriptRoot 'browser-profile'
$browserArgs = @(
    ('--user-data-dir="' + $profilePath + '"'),
    '--proxy-server=socks5://127.0.0.1:1080',
    '--host-resolver-rules="MAP * ~NOTFOUND, EXCLUDE localhost, EXCLUDE 127.0.0.1"',
    '--disable-quic',
    '--no-first-run',
    '--no-default-browser-check',
    '--new-window',
    'https://video.jw.scut.edu.cn/'
)
Start-Process -FilePath $browserExe -ArgumentList $browserArgs
