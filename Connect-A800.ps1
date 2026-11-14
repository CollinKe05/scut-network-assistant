param(
    [string]$ServerUser = 'kyx',
    [string]$ServerAddress = '10.48.12.129',
    [int]$SshPort = 22,
    [int]$LocalProxyPort = 7897,
    [int]$RemoteProxyPort = 47897,
    [switch]$TunnelOnly,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
$Host.UI.RawUI.WindowTitle = 'A800 - Campus SSH + local proxy tunnel'

$pythonPaths = @('C:\Users\19653\miniforge3\python.exe')
$pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
if ($pythonCommand) { $pythonPaths += $pythonCommand.Source }
$pythonExe = $pythonPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $pythonExe) { throw 'Python 3 is required for the SSH SOCKS bridge.' }
$sshExe = Join-Path $env:WINDIR 'System32\OpenSSH\ssh.exe'
if (-not (Test-Path -LiteralPath $sshExe)) { throw 'Windows OpenSSH Client was not found.' }

# Route this explicitly requested campus server only inside the VPN container.
# Some SCUT resource lists omit private lab-server routes.
$serverIp = $null
if ([Net.IPAddress]::TryParse($ServerAddress, [ref]$serverIp) -and $serverIp.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) {
    & docker --context desktop-linux exec scut-easyconnect ip route replace ($ServerAddress + '/32') dev tun0
    if ($LASTEXITCODE -ne 0) { throw 'Campus VPN tunnel is not ready. Log in through RealVNC, then retry.' }
}

$bridgePath = Join-Path $PSScriptRoot 'ssh-socks-bridge.py'
$proxyCommand = '"' + $pythonExe.Replace('\', '/') + '" "' + $bridgePath.Replace('\', '/') + '" %h %p'
$forwardSpec = "127.0.0.1:${RemoteProxyPort}:127.0.0.1:${LocalProxyPort}"
$sshArgs = @(
    '-o', ('ProxyCommand=' + $proxyCommand),
    '-o', 'ExitOnForwardFailure=yes',
    '-o', 'ServerAliveInterval=30',
    '-o', 'ServerAliveCountMax=3',
    '-o', 'ForwardAgent=no',
    '-o', 'ConnectTimeout=40',
    '-R', $forwardSpec,
    '-p', [string]$SshPort
)

Write-Host "Campus SSH: $ServerUser@$ServerAddress`:$SshPort through 127.0.0.1:1080"
Write-Host "Reverse proxy: server 127.0.0.1:$RemoteProxyPort -> this PC 127.0.0.1:$LocalProxyPort"
Write-Host 'Enter your SSH password only at the SSH password prompt below. Input is invisible.'
Write-Host 'No password is written to this script or sent to the chat.'
Write-Host 'Keep this window open while server programs use the reverse proxy.'

if ($TunnelOnly) {
    $sshArgs += @('-N', "$ServerUser@$ServerAddress")
} else {
    $remoteShell = 'export http_proxy=http://127.0.0.1:' + $RemoteProxyPort +
        ' https_proxy=http://127.0.0.1:' + $RemoteProxyPort +
        ' all_proxy=socks5h://127.0.0.1:' + $RemoteProxyPort +
        ' HTTP_PROXY=http://127.0.0.1:' + $RemoteProxyPort +
        ' HTTPS_PROXY=http://127.0.0.1:' + $RemoteProxyPort +
        ' ALL_PROXY=socks5h://127.0.0.1:' + $RemoteProxyPort +
        ' no_proxy=localhost,127.0.0.1,::1 NO_PROXY=localhost,127.0.0.1,::1; ' +
        'echo; echo Checking the reverse proxy from A800...; ' +
        'if curl --connect-timeout 6 --max-time 15 -I https://example.com; ' +
        'then echo Reverse proxy check succeeded.; else echo Reverse proxy check failed.; fi; ' +
        'echo; echo Proxy configured for this shell via SSH reverse tunnel.; exec ${SHELL:-/bin/bash} -l'
    $sshArgs += @('-t', "$ServerUser@$ServerAddress", $remoteShell)
}

& $sshExe @sshArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nSSH exited with code $LASTEXITCODE. Read the SSH error above."
    Write-Host 'Remote port forwarding failure: the server port is busy or the SSH server does not allow remote forwarding.'
    if (-not $NoPause) { Read-Host 'Press Enter to close this window' }
}
