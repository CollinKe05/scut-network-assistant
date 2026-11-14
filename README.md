# 华工网络助手

Windows 图形界面，用于切换校园直连与 Docker EasyConnect VPN，并管理 IP 网段、域名分流。

- 自动检测 A800 SSH 直连，校内跳过 Docker，校外使用校园 VPN。
- 支持 IPv4 / CIDR、单个域名、域名及子域名；可从完整网址提取域名。
- 每条规则选择直连、校园 VPN 或跟随快捷模式，并可调整优先级。
- 使用已有 Clash Verge TUN，保留原有外网代理；提供 RealVNC 和 Termius 入口。
- EasyConnect 运行在 Docker 容器中，登录验证在 RealVNC 内完成。

## 第一版 EXE

从 **Releases → v1.0.0** 下载 `SCUT-Network.exe`，放在本仓库根目录，然后双击运行。

这是依赖同目录脚本的桌面程序，需要保留项目文件夹。EXE 未包含 Python、Docker、Clash、RealVNC 或 Termius。源码与构建脚本位于 `app/`。

## 配置

本仓库保存个人部署项目的源码；首次部署仍需准备以下配置。现有电脑的配置无需重新填写。

1. 安装 Docker Desktop（Linux 容器）、Clash Verge Rev、Python 3、RealVNC Viewer 和 Termius。
2. 将 `settings.example.json` 复制为 `settings.json`，填写本机 Python、Clash、Termius 路径及 SSH 用户名。不要向文件中写入校园或 SSH 密码。
3. 在 Clash Verge 的全局扩展脚本中使用 `clash-scut.js`，重新加载配置，并开启 TUN、规则模式。
4. 通过图形界面连接。首次校外启动会构建兼容镜像，并自动生成含随机本地 VNC 密码的 `.env`。
5. 在 RealVNC 自行完成校园登录，在 Termius 自行完成 SSH 登录；服务器外网代理继续使用 Termius 的 Remote Rule。

自定义规则保存在 `network-rules.json`。`.env`、`settings.json`、浏览器数据和生成的二进制均被 Git 忽略；仓库不包含密码、订阅链接或登录数据。

详细操作见 [图形界面使用说明](图形界面使用说明.md)、[部署说明](使用说明.md) 和 [Termius 配置](Termius配置.md)。校园 VPN 的可达范围取决于学校授权；Docker 校园代理只支持 TCP。

## 构建与验证

在项目根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File app\Build.ps1
python app\test_network_manager.py
```

使用 Windows 自带的 .NET Framework 编译器生成 `SCUT-Network.exe`，无需安装 .NET SDK。重新构建前请关闭正在运行的助手。

## 上游

容器基于 [docker-easyconnect](https://github.com/docker-easyconnect/docker-easyconnect)，分流使用 [Clash Verge Rev](https://github.com/clash-verge-rev/clash-verge-rev) 与 [mihomo](https://github.com/MetaCubeX/mihomo)。EasyConnect、RealVNC 等第三方软件不包含在本仓库中。
