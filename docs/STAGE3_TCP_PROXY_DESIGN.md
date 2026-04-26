# 第三阶段：TCP 端口代理设计

第三阶段的目标不是完整虚拟网段，而是先打通一条真实、可验证、低风险的数据链路。

## 使用场景

两台 Windows 电脑在同一个 LAN/WiFi 内。

网关端：

- 运行本程序。
- 能访问目标设备 IP 和端口，例如 `192.168.1.10:30003`。

调试端：

- 运行本程序。
- 调试代码连接本机 `127.0.0.1:LocalListenPort`。
- 本程序把该连接转发给网关端，再由网关端连接目标设备。

## 数据路径

```text
调试代码
  -> 127.0.0.1:LocalListenPort
  -> 调试端 RobotNet
  -> LAN 会话连接
  -> 网关端 RobotNet
  -> TargetDeviceIp:TargetDevicePort
  -> 目标设备
```

## 控制路径

```text
UDP 发现
  -> 选择网关端
  -> 模拟/真实会话握手
  -> 端口预检
  -> 启动本地监听
  -> 创建代理连接
  -> 转发数据
  -> 关闭连接
```

## 角色职责

### 调试端 DebugClient

- 自动检查 `LocalListenPort` 是否可用。
- 如果端口被占用，自动选择替代端口并更新配置。
- 在本机监听 TCP。
- 每个进入的本地连接创建一个代理连接。
- 将本地连接字节流发送给网关端。
- 接收网关端返回字节流并写回本地连接。

### 网关端 GatewayAgent

- 接收调试端代理请求。
- 连接目标设备 `TargetDeviceIp:TargetDevicePort`。
- 在调试端连接和目标设备连接之间做双向复制。
- 记录连接开始、结束、异常和字节数。

## 会话约束

- 一个设备同一时间只能连接一个对端。
- 已连接对端未断开或未离线时，不允许连接其他设备。
- 一个会话内可以有多个 TCP 代理连接。
- 每个代理连接使用独立 `ConnectionId`。

## 当前协议设计

第三阶段先使用 JSON 控制消息完成连接打开握手：

- 调试端连接网关端 `ProxyControlPort`。
- 调试端发送 `OpenConnection` 消息，包含 `ConnectionId`、`TargetHost`、`TargetPort` 和时间戳。
- 网关端尝试连接目标设备端口。
- 网关端返回 `OpenConnectionResult`，包含 `Success` 和 `ErrorMessage`。
- 如果成功，后续进入数据转发阶段。

第四阶段已经把数据转发升级为签名加密帧协议，继续使用一行 JSON 帧承载以下消息类型：

消息字段：

```text
ProtocolVersion
SessionId
ConnectionId
SessionToken
MessageType
Sequence
PayloadBase64
EncryptionNonceBase64
EncryptionTagBase64
Nonce
Timestamp
Signature
```

消息类型：

- `OpenConnection`
- `OpenConnectionResult`
- `Data`
- `CloseConnection`
- `Heartbeat`
- `Error`

## 错误处理

必须覆盖：

- 本地监听端口被占用。
- 网关端离线。
- 网关端已连接其他设备。
- 目标设备端口不可达。
- 中途断线。
- 单连接异常关闭。
- 会话整体关闭。

## 安全边界

第三阶段先完成可用链路，第四阶段已经补齐以下安全能力：

- SharedKey 握手认证。
- HMAC-SHA256 消息签名。
- AES-GCM 数据帧加密封装。
- 会话令牌。
- nonce 和时间戳防重放。
- 心跳和空闲超时。

## 当前代码落地

本阶段已完成 TCP 代理的模型、服务接口和第一版真实转发实现：

- `TcpProxyStatus`
- `TcpProxySessionInfo`
- `TcpProxyControlMessage`
- `ITcpProxyService`
- `SafeTcpProxyService`

当前 `SafeTcpProxyService` 已完成第三阶段 MVP：

- 会检查配置和端口。
- 会返回诊断结果。
- 调试端可以启动 `127.0.0.1:LocalListenPort` 本地 TCP 监听。
- 网关端可以启动 `0.0.0.0:ProxyControlPort` 代理控制监听。
- 本地客户端连接调试端监听端口后，调试端会主动连接网关端控制端口。
- 调试端会发送 `OpenConnection` 控制消息。
- 网关端会连接 `TargetDeviceIp:TargetDevicePort`。
- 网关端连接目标设备成功后，会返回 `OpenConnectionResult`。
- 调试端、网关端和目标设备之间会进行双向字节流转发。
- 会记录活动连接数、发送字节数、接收字节数、最后错误和最后更新时间。

第四阶段已进一步补齐：

- SharedKey 强制认证。
- HMAC-SHA256 签名。
- AES-GCM 数据帧加密。
- 会话令牌、nonce 和时间戳校验。
- 心跳、空闲超时、连接重试和审计日志。

仍未包含的能力：

- 真实虚拟网卡、路由、NAT 或桥接。
- UDP、ICMP、广播、组播或完整 IP 包转发。
