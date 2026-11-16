# M3 规格：直连 RTP 档 — Windows→Android 一帧出画

日期：2026-11-17 · 状态：已设计，实现待 M2 完成后启动 · 裁决依据：C-28（09 §10.1）

## 目标

平板（SM-P605 / Android 8 / v7a）通过**直连 UDP RTP** 收到 Windows 桌面
H264 流并渲染 ≥10s 稳定画面——彻底绕开 SIPSorcery↔libwebrtc 的 ICE 互操作
缺陷（M0/G1 三轮真机实证，含浏览器跨设备同样失败）。

## 架构（全部复用已有轮子，零自研加密/打包/传输）

### 控制面（现有 HTTP，契约已被 SignalingContractTests 锁定）

Android 发起会话：`POST /signaling`，`type` 扩展为 `"rtp"`（现有
`"offer"` 语义保留给 WebRTC 路径）。服务返回：

```json
{
  "type": "rtp-answer",
  "crypto": "1 AES_CM_128_HMAC_SHA1_80 inline:<base64 30B key|salt> UNAUTHENTICATED_SRTP",
  "rtpEndpoint": "192.168.42.39:5004",
  "ssrc": 1234567890
}
```

SDES 密钥经 HTTP 明文下发是**已知边界**（LAN-only 信任模型）；TLS 化
（Kestrel + 自签证书 + 指纹钉扎）是独立里程碑 M4，不阻塞媒体打通。

### Windows 发送端（8.0.12 RTPSession API，元数据探针实证 2026-11-17）

```
RTPSession(RtpSessionConfig { isSecure = true, RtpPort = 5004 })
  → addTrack(new MediaStreamTrack(VideoFormat(H264, 96, 90000, fmtp 42e01f), SendOnly))
  → CreateOffer(connectionAddress)          // 生成带 a=crypto 行的 SDP（SrtpCryptoSuites）
  → 经 HTTP 控制面下发 crypto 行 + RTP 端点
  → SetRemoteDescription(SdpType.answer, androidAnswerSdp)   // 建立 SrtpHandler（SDES）
  → SetDestination(SDPMediaTypesEnum.Video, tabletEp, rtcpEp) // 无 ICE，固定端点
  → Start() → ScreenMirror 编码 AU → SendVideo(durationRtpUnits, annexB)
```

证据（`tests/UniLinker.Core.Tests/Probes/SipsorceryRtpSessionProbe.cs`）：
`SetDestination` / `SetRemoteDescription` / `CreateOffer` / `SendVideo` /
`SrtpCryptoSuites` 全部为 public；SRTP 加密路径由 SIPSorcery 内部
SrtpHandler 承担（P2 已证实其外部供钥能力，SDES 是其官方形态）。

### Android 接收端（新 Kotlin 模块，约 500 行）

```
DatagramSocket(port) → SRTP unprotect → H264 depacketizer(FU-A 重组)
  → MediaCodec(H264 hw decode, v7a 硬解可用) → SurfaceView 渲染
```

- SRTP：Kotlin 实现 RFC 3711 AES-CM + HMAC-SHA1-80（javax.crypto 原生支持
  AES/CM 与 HmacSHA1；v7a 无 ARMv8 crypto 扩展，2Mbps 流量下软算开销可忽略）。
  **必须**用 RFC 7714 §14 测试向量单测，不手写不验证的密码代码。
- Depacketizer：RFC 6184 FU-A/STAP-A 重组，用固定向量单测（无网络依赖）。

## Given-When-Then 验收

- Given Android 端 UDP 收到 SRTP 包,
  When RFC 7714 向量输入 unprotect,
  Then 还原 RTP 包逐字节一致（单测，无网络）。
- Given Windows 端发送编码 AU,
  When Android 端重组 FU-A,
  Then Annex-B AU 完整还原（单测向量，无网络）。
- Given 平板与 PC 在同一局域网（RNDIS 热点均可）,
  When Android 发起 rtp 会话并渲染,
  Then ≥10s 连续 720p30 出画，`adb shell screencap` 能截到桌面内容变化。
- Given clumsy 注入 2% 丢包 + 30ms 抖动,
  When 流进行中,
  Then 关键帧请求后 ≤1s 恢复（不永久花屏）。

## 边界（不做）

- 浏览器不参与直连档（浏览器保持 WebRTC 路径）
- 不做多观众 / 多流 / 自适应码率（固定 720p30 / 2Mbps / 30fps）
- 不做 RTCP 反馈闭环（M3 只保发送方向 + 静态图重编码自愈；PLI 走应用层
  控制消息，进 M4）
- 不动 WebRTC 路径与浏览器 E2E

## 停止规则

MediaCodec 在 v7a 硬解不可用时（探针否决）→ 回退 OpenH264 软解
（H264Sharp 已含 .so）或降级 480p；SRTP 向量单测失败 → 停，不得带病上线。
