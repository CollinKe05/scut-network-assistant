param([switch]$NoPause)
. (Join-Path $PSScriptRoot 'Common.ps1')
$settings=Get-CampusSettings
if (-not $settings.ServerAddress -or -not $settings.ServerUser) {throw '请先在向导填写自己的服务器地址和用户名。'}
$ssh=Join-Path $env:WINDIR 'System32\OpenSSH\ssh.exe'
if (-not (Test-Path -LiteralPath $ssh)) {throw '请在 Windows 可选功能中安装 OpenSSH 客户端，或使用 Termius。'}
& $ssh -p $settings.SshPort ($settings.ServerUser+'@'+$settings.ServerAddress)
if($LASTEXITCODE -ne 0 -and -not $NoPause){[void](Read-Host '请查看 SSH 错误；密码仅在 SSH 提示中输入。按回车关闭')}
