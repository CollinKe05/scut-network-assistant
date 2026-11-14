# Termius：保留原有 A800 代理方式

现已利用已有 Clash TUN 实现全电脑校园分流。Termius 保持原来的主机与转发规则，不需要再套本机 22222 转发或 PowerShell 反向隧道。

1. 双击桌面“华工校园网-A800.cmd”：先尝试校园直连；能直连时跳过 Docker，校外再启动 VPN。只想使用校园网直连时，可双击“华工校内直连.cmd”。
2. 校外需要时在 RealVNC 完成校园认证，等待数据通道连通；校内直接打开 Termius。
3. A800 主机保持 `10.48.12.129:22`，用户名 `kyx`，原有密码不变。
4. 启动你原有的 **Remote Rule**，图标变蓝表示已启动。

| 原有 Remote Rule 字段 | 当前值 |
| --- | --- |
| Remote host | A800 |
| Remote port | 18888 |
| Bind address | 127.0.0.1 |
| Destination address（电脑） | 127.0.0.1 |
| Destination port | 7897 |

链路为 `A800 的 127.0.0.1:18888 → Termius SSH 隧道 → 电脑 Clash 127.0.0.1:7897`。

服务器 Codex 继续使用原来的 18888。新终端若尚未设置代理，可执行：

```bash
export http_proxy=http://127.0.0.1:18888
export https_proxy=http://127.0.0.1:18888
export all_proxy=socks5h://127.0.0.1:18888
curl -I https://example.com
```

校内保持 Clash 与 Termius 的 Remote Rule 运行即可；校外还需要 Docker VPN。此前因网络失败退出的 Codex 进程，需在转发恢复后重新启动。

Clash 中的 `SCUT-EasyConnect` 现在是可切换组：校内选择 `DIRECT`，校外选择 `SCUT-EasyConnect-Docker`。启动器负责选择；也可以在 Clash 的代理页面手动切换。

全电脑分流覆盖 `10.48.0.0/16`、`scut.edu.cn` 及校园网站网关；校园 VPN 网关直连，其余流量沿用原有外网规则。通过 Docker 的校园分流只支持 TCP，UDP 与 ICMP ping 不经过校园 SOCKS5。

看到校园资源列表与数据通道真正连通是两步；本次观察到数据通道初始化延迟数分钟。启动器已分别提示认证状态和 A800 连通状态，无需反复输入验证码。
