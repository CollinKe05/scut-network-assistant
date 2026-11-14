param([switch]$CheckOnly, [switch]$CampusOnly, [switch]$VpnOnly, [switch]$NoPause)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$Host.UI.RawUI.WindowTitle = '华工校园网与 A800'
$settings = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'settings.json') | ConvertFrom-Json

function Test-LocalPort([int]$Port) {
    $client = New-Object Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync('127.0.0.1', $Port)
        return ($task.Wait(1000) -and $client.Connected)
    } catch { return $false } finally { $client.Dispose() }
}

function Open-Termius {
    Write-Host 'A800 主机保持 10.48.12.129:22，用户名 kyx。'
    Write-Host ('在 Termius 中启动原有 Remote Rule：A800 的 ' + $settings.RemoteProxyPort + ' → 电脑 127.0.0.1:' + $settings.LocalProxyPort + '。')
    $termiusWindow = Get-Process -Name Termius -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($termiusWindow) {
        $windowShell = New-Object -ComObject WScript.Shell
        [void]$windowShell.AppActivate([int]$termiusWindow.Id)
    } elseif (Test-Path -LiteralPath $settings.TermiusPath) {
        Start-Process -FilePath $settings.TermiusPath
    } else { throw '未找到 Termius，请检查 settings.json 的 TermiusPath。' }
}

function Find-ExistingSsh {
    $listenPrefix = '127.0.0.1:' + $settings.RemoteProxyPort + ':127.0.0.1:'
    $destination = $settings.ServerUser + '@' + $settings.ServerAddress
    try {
        return Get-CimInstance Win32_Process -Filter "Name='ssh.exe'" -ErrorAction Stop |
            Where-Object { $_.CommandLine -and $_.CommandLine.Contains($listenPrefix) -and $_.CommandLine.Contains($destination) } |
            Select-Object -First 1
    } catch {
        $existingWindow = Get-Process -Name powershell,pwsh -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -ne $PID -and $_.MainWindowTitle -match '^A800 - Campus SSH|^华工校园网与 A800' } |
            Select-Object -First 1
        if ($existingWindow) { return [pscustomobject]@{ ParentProcessId = $existingWindow.Id } }
        return $null
    }
}

function Test-CampusSsh {
    $ErrorActionPreference = 'Continue'
    $script:campusTunnelAddress = (& $script:dockerExe --context desktop-linux exec scut-easyconnect sh -c 'ip -4 -o addr show dev tun0 2>/dev/null' 2>$null) -join ' '
    if ($LASTEXITCODE -ne 0 -or $script:campusTunnelAddress -notmatch '\binet ') {
        $script:campusWaitReason = '校园隧道尚未建立，请在 RealVNC 完成校园认证。'
        return $false
    }
    $script:campusWaitReason = '校园隧道已分配地址；正在等待 VPN 数据通道及 A800 的 SSH 服务连通。'
    & $script:dockerExe --context desktop-linux exec scut-easyconnect ip route replace ($settings.ServerAddress + '/32') dev tun0 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { return $false }
    $probeOutput = & $settings.PythonPath (Join-Path $PSScriptRoot 'ssh-socks-bridge.py') $settings.ServerAddress $settings.SshPort --probe --timeout 5 2>$null
    return ($LASTEXITCODE -eq 0 -and ($probeOutput -join ' ') -match 'SSH-2\.0-')
}

function Open-VncViewer {
    $viewerCandidates = @('D:\Program Files\RealVNC\VNC Viewer\vncviewer.exe',
        'C:\Program Files\RealVNC\VNC Viewer\vncviewer.exe',
        'C:\Program Files (x86)\RealVNC\VNC Viewer\vncviewer.exe')
    $viewer = $viewerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $viewer) {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'RealVNC Viewer.lnk'
        if (Test-Path -LiteralPath $shortcutPath) {
            $shortcutShell = New-Object -ComObject WScript.Shell
            $viewer = $shortcutShell.CreateShortcut($shortcutPath).TargetPath
        }
    }
    if (-not $viewer -or -not (Test-Path -LiteralPath $viewer)) { throw '找不到 RealVNC Viewer，请先打开已安装的 Viewer。' }
    $currentViewer = Get-Process -Name vncviewer -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -match '127\.0\.0\.1.*5901' } | Select-Object -First 1
    if ($currentViewer) {
        $windowShell = New-Object -ComObject WScript.Shell
        [void]$windowShell.AppActivate([int]$currentViewer.Id)
    } else {
        Start-Process -FilePath $viewer -ArgumentList '127.0.0.1:5901'
    }
}

$launcherMutex = New-Object Threading.Mutex($false, 'Local\SCUT-EasyConnect-A800-Launcher')
$ownsMutex = $false
try {
    if ($CampusOnly -and $VpnOnly) { throw 'CampusOnly 与 VpnOnly 不能同时指定。' }
    if ($CheckOnly) {
        foreach ($fileName in @('Start-EasyConnect.ps1', 'Connect-A800.ps1', 'ssh-socks-bridge.py', 'campus-route.py', 'clash-scut.js', 'compose.yaml', '.env')) {
            if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $fileName))) { throw ('缺少文件：' + $fileName) }
        }
        if (-not (Test-Path -LiteralPath $settings.PythonPath)) { throw '找不到配置中的 Python 路径。' }
        Write-Output 'LAUNCHER_FILES_OK'
        if (Find-ExistingSsh) { Write-Output 'EXISTING_A800_SESSION_FOUND' }
        return
    }

    $existingSsh = if ($settings.ClientMode -ne 'Termius') { Find-ExistingSsh } else { $null }
    if ($existingSsh) {
        Write-Host 'A800 登录或反向代理窗口已经打开，正在切换到现有窗口。' -ForegroundColor Green
        $windowShell = New-Object -ComObject WScript.Shell
        [void]$windowShell.AppActivate([int]$existingSsh.ParentProcessId)
        return
    }

    $ownsMutex = $launcherMutex.WaitOne(0)
    if (-not $ownsMutex) {
        Write-Host '启动流程正在另一个窗口中运行，请在该窗口或 RealVNC 中完成登录。'
        return
    }

    Write-Host '[1/4] 检查你现有的 Clash 代理……' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $settings.PythonPath)) { throw '找不到配置中的 Python，请检查 settings.json 的 PythonPath。' }
    if (-not (Test-LocalPort $settings.LocalProxyPort)) {
        if (Test-Path -LiteralPath $settings.ClashPath) {
            Start-Process -FilePath $settings.ClashPath -WindowStyle Hidden
            for ($attempt = 0; $attempt -lt 20; $attempt++) {
                if (Test-LocalPort $settings.LocalProxyPort) { break }
                Start-Sleep -Seconds 1
            }
        }
        if (-not (Test-LocalPort $settings.LocalProxyPort)) {
            throw ('本地代理端口 ' + $settings.LocalProxyPort + ' 未开启。请启动 Clash；端口变化时修改 settings.json。')
        }
    }

    if (-not $VpnOnly -and $settings.ClientMode -eq 'Termius') {
        Write-Host '[2/4] 先检查校园网直连……' -ForegroundColor Cyan
        & $settings.PythonPath (Join-Path $PSScriptRoot 'campus-route.py') direct --probe
        $directResult = $LASTEXITCODE
        if ($directResult -eq 1) { throw '校园分流切换失败，请检查 Clash 是否正常运行。' }
        if ($directResult -eq 0) {
            Write-Host '校园 SSH 已直连，不需要启动 Docker 或登录 VPN。' -ForegroundColor Green
            Open-Termius
            return
        }
        if ($CampusOnly) { throw '已切为校园直连，但未收到 A800 SSH 响应。请检查校园网络及服务器状态。' }
        Write-Host '校园 SSH 暂不能直连，继续启动 Docker VPN。'
    }

    & $settings.PythonPath (Join-Path $PSScriptRoot 'campus-route.py') vpn
    if ($LASTEXITCODE -ne 0) { throw '无法切换到 Docker 校园分流。' }
    Write-Host '[2/4] 启动或复用 Docker 校园 VPN……' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'Start-EasyConnect.ps1') -NoViewer
    $script:dockerExe = (Get-Command docker.exe -ErrorAction SilentlyContinue).Source
    if (-not $script:dockerExe) { $script:dockerExe = Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe' }

    Write-Host '[3/4] 检查校园连接……' -ForegroundColor Cyan
    $campusReady = Test-CampusSsh
    if (-not $campusReady) {
        if ($script:campusTunnelAddress -notmatch '\binet ') {
            $vncPasswordLine = Get-Content -LiteralPath (Join-Path $PSScriptRoot '.env') | Where-Object { $_ -match '^VNC_PASSWORD=' } | Select-Object -First 1
            $vncPassword = ($vncPasswordLine -split '=', 2)[1]
            Write-Host ('请在 RealVNC 中连接 127.0.0.1:5901，VNC 密码：' + $vncPassword) -ForegroundColor Yellow
            Write-Host '完成校园认证后，此窗口会继续检查数据通道，无需反复输入验证码。' -ForegroundColor Yellow
            Open-VncViewer
        } else {
            Write-Host '校园隧道已有地址，暂时不需要重新输入验证码。' -ForegroundColor Yellow
        }
        $deadline = (Get-Date).AddMinutes([int]$settings.LoginWaitMinutes)
        $nextNotice = Get-Date
        while ((Get-Date) -lt $deadline) {
            $campusReady = Test-CampusSsh
            if ($campusReady) { break }
            if ((Get-Date) -ge $nextNotice) {
                Write-Host ($script:campusWaitReason + ' ' + (Get-Date -Format 'HH:mm:ss'))
                $nextNotice = (Get-Date).AddSeconds(30)
            }
            Start-Sleep -Seconds 5
        }
    }
    if (-not $campusReady) { throw ('等待超时。最后状态：' + $script:campusWaitReason) }

    if ($settings.ClientMode -eq 'Termius') {
        Write-Host '[4/4] 校园 SSH 已连通，打开 Termius。' -ForegroundColor Green
        Write-Host 'Clash TUN 已配置全电脑校园分流，Termius 主机保持原来的地址即可，不必另设代理。'
        Write-Host '保留你原来的 Port Forwarding 配置；不会再自动建立 PowerShell 反向隧道。'
        Open-Termius
        return
    }

    Write-Host '[4/4] 登录 A800，建立反向代理。' -ForegroundColor Green
    Write-Host '服务器密码直接输在下面的 SSH 提示中；输入时不显示字符。'
    Write-Host ('服务器代理：127.0.0.1:' + $settings.RemoteProxyPort + ' → 你电脑的 Clash：127.0.0.1:' + $settings.LocalProxyPort)
    Write-Host '保持此窗口和 Clash 运行，即可在服务器中使用代理。'
    & (Join-Path $PSScriptRoot 'Connect-A800.ps1') -ServerUser $settings.ServerUser -ServerAddress $settings.ServerAddress -SshPort $settings.SshPort -LocalProxyPort $settings.LocalProxyPort -RemoteProxyPort $settings.RemoteProxyPort -NoPause
} catch {
    Write-Host ('启动未完成：' + $_.Exception.Message) -ForegroundColor Red
    if ($CheckOnly) { throw }
    if ($NoPause) { exit 1 }
    [void](Read-Host '按回车关闭；修正后可再次双击桌面入口')
} finally {
    if ($ownsMutex) { $launcherMutex.ReleaseMutex() }
    $launcherMutex.Dispose()
}
