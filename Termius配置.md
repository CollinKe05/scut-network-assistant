# 服务器与代理（可选）

不使用服务器时跳过此部分。需要时，在向导填写**自己的**服务器地址、端口与 SSH 用户名，再安装 Termius 或 Windows OpenSSH 客户端。

## SSH

Termius → Hosts → New Host：填写向导中的目标地址、端口与用户名。首次连接自行核对主机指纹；密码仅在 Termius 中输入。也可复制向导生成的 `ssh` 命令在 PowerShell 执行，不需要更改 SSH 服务端配置。

## 服务器使用电脑已有代理

只有电脑已有可用的外网代理时才需要反向转发，助手不提供外网节点。Termius → Port Forwarding → 新建 Remote Rule：

| 字段 | 填写内容 |
| --- | --- |
| Remote host | 自己配置的 SSH 主机 |
| Remote bind address | `127.0.0.1` |
| Remote port | 向导中的服务器代理端口，默认 `18888` |
| Destination address | `127.0.0.1` |
| Destination port | 电脑已有 Clash 混合代理端口，默认 `7897` |

开启该规则并保持 Termius 与 Clash 运行。服务器程序使用 `http://127.0.0.1:所选远端端口`；具体 export 命令会按向导填写的端口生成。端口被占用时换一个未使用的端口，并同步服务器程序设置。HTTP 响应不能替代对具体应用的验证。
