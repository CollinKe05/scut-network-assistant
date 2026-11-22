param([switch]$CheckOnly, [switch]$CampusOnly, [switch]$VpnOnly, [switch]$NoPause, [switch]$NetworkChangeConfirmed)
. (Join-Path $PSScriptRoot 'Common.ps1')
Set-Location -LiteralPath $PSScriptRoot
$settings = Get-CampusSettings
$dockerExe = $null
function Open-CampusClient {
    if ($settings.ServerAddress -and $settings.TermiusPath -and (Test-Path -LiteralPath $settings.TermiusPath)) {
        Write-Host ('SSH 主机：' + $settings.ServerAddress + ':' + $settings.SshPort + '，用户名：' + $settings.ServerUser)
        Write-Host ('如需服务器使用电脑代理，请在 Termius 开启 Remote Rule：服务器 127.0.0.1:' + $settings.RemoteProxyPort + ' → 电脑 127.0.0.1:' + $settings.LocalProxyPort)
        Start-Process -FilePath $settings.TermiusPath
    } else { Write-Host '校园目标可访问。可以打开浏览器使用；服务器配置是可选步骤。' }
}
function Test-CampusTarget {
    $ErrorActionPreference = 'Continue'
    $tunnel = (& $dockerExe --context desktop-linux exec campus-easyconnect sh -c 'ip -4 -o addr show dev tun0 2>/dev/null' 2>$null) -join ' '
    if ($LASTEXITCODE -ne 0 -or $tunnel -notmatch '\binet ') { $script:waitReason='隧道尚未建立，请打开校园登录并完成验证码。'; return $false }
    if ($settings.ServerAddress) {
        $address = $null
        if ([Net.IPAddress]::TryParse($settings.ServerAddress,[ref]$address) -and $address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) {
            & $dockerExe --context desktop-linux exec campus-easyconnect ip route replace ($settings.ServerAddress+'/32') dev tun0 2>$null | Out-Null
        }
        $result = & $settings.PythonPath (Join-Path $PSScriptRoot 'ssh-socks-bridge.py') $settings.ServerAddress $settings.SshPort --probe --timeout 5 2>&1
        if ($LASTEXITCODE -eq 0 -and ($result -join ' ') -match 'SSH-2\.0-') { return $true }
        $script:waitReason='隧道已有地址，但 SSH 尚未响应。可在运行记录查看连接错误，并确认服务器端口与账号访问权限。'
        Write-Host (($result | ForEach-Object { $_.ToString() }) -join ' ')
        return $false
    }
    $code = & curl.exe --proxy http://127.0.0.1:8888 --noproxy 'localhost,127.0.0.1' --connect-timeout 4 --max-time 6 --head --silent --output NUL --write-out '%{http_code}' $settings.ProbeUrl 2>$null
    if ($LASTEXITCODE -eq 0 -and $code -match '^[2-4][0-9][0-9]$') { Write-Host ('校园测试网址响应 HTTP '+$code+'；网站登录权限请在浏览器确认。'); return $true }
    $script:waitReason='隧道已有地址，测试网址尚无响应，客户端可能仍在初始化；长时间未通请检查登录会话或更换测试网址。'
    return $false
}
try {
    if ($CampusOnly -and $VpnOnly) { throw '不能同时选择直连与 VPN。' }
    if (-not (Test-Path -LiteralPath $settings.PythonPath)) { throw 'Python 未配置，请回到首次配置向导重新检测。' }
    if ($CheckOnly) { Write-Output 'LAUNCHER_FILES_OK'; return }
    Confirm-CampusNetworkChange $NetworkChangeConfirmed.IsPresent $NoPause.IsPresent
    Write-Host '[1/4] 检查 Clash 分流配置……'
    $stateText = & $settings.PythonPath (Join-Path $PSScriptRoot 'network-manager.py') status
    $state = $stateText | ConvertFrom-Json
    if (-not $state.clash) { throw '请先打开 Clash，按照向导导入配置并启用校园配置。' }
    if (-not $state.tun -or $state.mode -ne 'rule') { throw '请先在 Clash 启用 TUN，并切换到规则模式。' }
    if (-not $VpnOnly) {
        Write-Host '[2/4] 检查目标直连……'
        & $settings.PythonPath (Join-Path $PSScriptRoot 'campus-route.py') direct --probe
        $result = $LASTEXITCODE
        if ($result -eq 0) { Write-Host '测试目标可以直接访问，已选择直连。'; Open-CampusClient; return }
        if ($result -ne 2) { throw '分流配置未就绪，请按向导完成 Clash 配置。' }
        if ($CampusOnly) { throw '已选择直连，但测试目标未响应。不会自动启动 VPN。' }
    }
    Write-Host '[2/4] 启动或复用 Docker VPN……'
    & $settings.PythonPath (Join-Path $PSScriptRoot 'campus-route.py') vpn
    if ($LASTEXITCODE -ne 0) { throw '校园分流组未就绪，请先在向导配置 Clash。' }
    & (Join-Path $PSScriptRoot 'Start-EasyConnect.ps1') -NoViewer -NetworkChangeConfirmed
    $dockerExe=Get-DockerPath $settings
    Write-Host '[3/4] 验证校园目标访问。需要认证时请点击“校园登录”。'
    $deadline=(Get-Date).AddMinutes([int]$settings.LoginWaitMinutes)
    while ((Get-Date) -lt $deadline) {
        if (Test-CampusTarget) { Write-Host '[4/4] 连通检查通过。'; Open-CampusClient; return }
        Write-Host ($script:waitReason+' '+(Get-Date -Format 'HH:mm:ss'))
        Start-Sleep -Seconds 8
    }
    throw ('等待超时。'+$script:waitReason+' 请检查学校是否授权访问该目标。')
} catch {
    Write-Host ('操作未完成：'+$_.Exception.Message)
    if ($NoPause) { exit 1 }
    [void](Read-Host '按回车关闭')
    exit 1
}
