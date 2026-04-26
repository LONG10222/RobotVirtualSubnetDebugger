# 第四阶段：认证、加密与稳定性

第四阶段目标是让第三阶段 TCP 端口代理具备基础安全性和长期运行保护。

## 已完成能力

- SharedKey 强制认证：调试端和网关端必须配置相同共享密钥。
- HMAC-SHA256 消息签名：所有代理控制帧和数据帧都带签名。
- AES-GCM 数据帧加密：业务字节流以加密帧传输。
- 会话令牌：每个代理连接生成独立 `SessionToken`。
- nonce 和时间戳：用于降低重放消息风险。
- 心跳：连接空闲时仍发送 `Heartbeat` 帧。
- 空闲超时：超过配置时间未收到有效帧会关闭连接。
- 连接重试：连接网关端或目标设备失败时按配置重试。
- 关闭帧：正常断开时发送 `CloseConnection`。
- 审计日志：连接建立、认证结果、失败原因、关闭事件写入 `AUDIT` 日志。

## 帧协议

当前 TCP 代理连接使用一行 JSON 作为一帧。每帧包含：

```text
ProtocolVersion
SessionId
ConnectionId
SessionToken
MessageType
Sequence
TargetHost
TargetPort
PayloadText
PayloadBase64
EncryptionNonceBase64
EncryptionTagBase64
ErrorMessage
Nonce
Timestamp
Signature
```

消息类型：

- `OpenConnection`
- `OpenConnectionResult`
- `Data`
- `Heartbeat`
- `CloseConnection`
- `Error`

## 安全说明

- `SharedKey` 不写入日志。
- `Signature` 使用 HMAC-SHA256。
- `Data` 帧载荷使用 AES-GCM 加密。
- HMAC 签名覆盖协议版本、会话 ID、连接 ID、会话令牌、消息类型、序号、目标端点、载荷、加密参数、错误信息、nonce 和时间戳。
- AES-GCM 密钥由 `SharedKey + SessionToken` 派生。
- 时间戳允许 5 分钟偏差。

## 稳定性说明

- 默认心跳间隔：`5` 秒。
- 默认空闲超时：`20` 秒。
- 默认连接重试次数：`2` 次。
- 调试端和网关端都会持续监听，单个代理连接断开后不会关闭整个代理服务。
- 任意 TCP 流断开后不做透明断点续传；上层调试程序需要重新连接本机代理端口。

## 边界

第四阶段仍然不创建真实虚拟网卡，不修改系统路由，不执行 NAT、桥接、防火墙或管理员权限命令。

它解决的是“安全可靠地转发指定 TCP 端口”，不是“让调试端拥有目标网段 IP”。
