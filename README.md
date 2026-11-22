# 校园网络助手

Windows 图形界面的 EasyConnect Docker 启动与校园分流助手。v1.1 提供从零开始的首次配置向导；默认只保留学校域名 `scut.edu.cn`，不包含任何人的服务器 IP、SSH 用户名、本机路径或登录数据。

## 新用户从这里开始

1. 打开仓库 **Releases → v1.1.0**，下载 **Campus-Network.exe**。
2. 双击 EXE。它会将自带脚本展开到当前用户的 `%LOCALAPPDATA%\CampusNetworkAssistant\1.1.0`，自动进入“首次配置向导”。**无需安装 Git，也不必自己创建 JSON。**
3. 按向导依次完成下列步骤。

| 步骤 | 需要做什么 | 完成标志 |
| --- | --- | --- |
| 准备电脑 | Windows 10/11 64 位，按 Docker 官方说明准备 WSL 2 和硬件虚拟化 | 电脑符合 Docker 要求 |
| 安装依赖 | 安装 Docker Desktop、Python 3、Clash Verge Rev；安装完重新检测，路径不常见时手动选 EXE | 三项必需依赖均找到 |
| 填写信息 | 填校园 VPN、学校域名、验证网址；服务器与用户名可选 | 配置保存到本机数据目录 |
| 配置 Clash | 新用户拖入向导生成的 YAML；已有外网订阅的用户使用扩展脚本，保留原订阅 | 校园组、规则模式、TUN 和代理端口通过检查 |
| 登录与验证 | 确认后启动 VPN，浏览器或 RealVNC 内自行完成账号与验证码，再检查网址 / SSH | 目标响应；网站授权仍需网页登录确认 |
| SSH 与完成 | 按自己填写的主机、端口、用户名配置 Termius 或 OpenSSH；需要时再配 Remote Rule | 向导完成，之后使用连接面板 |

RealVNC、Termius 都是可选项：没有 RealVNC 时使用浏览器登录容器；不需要服务器时可以只访问校园网页。校园网络分流本身**不需要外网订阅**，也不提供外网节点。

安装软件、切换网络与停止 VPN 均由用户明确点击并确认。程序不会检测到新镜像就自动替换正在运行的容器，也不会自行重启电脑。

## 软件安装

向导提供官方链接以及适用软件的 WinGet 按钮。WinGet 不是必需条件；提示找不到 WinGet 时，从官方页面下载安装即可。用户自行在安装器确认条款、管理员权限与重启时机。安装后点击“重新检测路径”；必要时选择程序 EXE。

- [Docker Desktop / Windows](https://docs.docker.com/desktop/setup/install/windows-install/)：安装后切换为 Linux 容器，打开 Docker 等待引擎启动。WSL 或虚拟化未启用时按官方文档处理。
- [Python / Windows](https://www.python.org/downloads/windows/)：安装 64 位 Python 3，向导支持常规安装目录和已有 Conda 环境，也可手选 `python.exe`。
- [Clash Verge Rev](https://www.clashverge.dev/install.html)：校园分流需要规则模式、服务支持和 TUN。向导提供无外网订阅的本地校园配置；[官方文档说明支持拖拽导入本地配置](https://www.clashverge.dev/guide/profile.html)。
- [RealVNC Viewer（可选）](https://www.realvnc.com/en/connect/download/viewer/)：如果没有适用的直接连接授权，可直接使用向导中的浏览器登录，不必购买 RealVNC。
- [Termius（可选）](https://termius.com/download/windows)：需要服务器时安装；也可以使用 Windows 自带 OpenSSH 客户端。

首次拉取 Docker 镜像需要访问 Docker Hub，并可能耗时较长；失败时保留运行记录检查 Docker 的网络访问。不要向未知第三方提交校园账号。

## 自定义规则

“网段与域名”页保留。发布版的默认规则**只有学校域名**，没有作者个人网段。用户可以自行添加 IPv4 / CIDR、单域名或域名及子域名，选择直连、校园 VPN 或跟随快捷模式；保存前有网络变更确认。学校 VPN 的可达范围取决于学校授权，Docker SOCKS5 通道仅支持 TCP。

## 隐私与配置

校园密码、验证码只在容器登录页填写，SSH 密码只在 SSH 客户端填写。助手不会要求这些密码，也不会上传本机配置。运行记录保留在窗口内。

`settings.json`、`.env` 与浏览器数据不进入 Git 和内置发布包。Git 中只有公开学校地址、空服务器信息的示例配置、域名默认规则和源码。旧版 v1.0.0 保留原状；v1.1 使用独立数据目录，不自动迁移旧版配置或覆盖旧程序。

## 构建与测试

在 Windows 源码根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File app\Build.ps1
python app\test_network_manager.py
python app\test_distribution.py
```

构建使用 Windows 的 .NET Framework 编译器，不需要 .NET SDK。生成的 `Campus-Network.exe` 自带运行脚本，第三方依赖仍由向导引导用户安装。构建脚本只从明确的文件清单打包，不打包当前目录的私有配置。

部署细节见 [图形界面使用说明](图形界面使用说明.md) 和 [Termius 配置](Termius配置.md)。容器基于 [docker-easyconnect](https://github.com/docker-easyconnect/docker-easyconnect)，Clash 内核基于 [mihomo](https://github.com/MetaCubeX/mihomo)。第三方软件本体不包含在发布包中。
