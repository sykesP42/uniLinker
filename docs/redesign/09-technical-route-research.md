# 09 — 技术路线调研（2026-11-14，v1.0）

> 本文是对 `02-options-and-research.md` 的**实证补充轮**：把选型②落地前最关键的七条技术路线
> 用「反编译取证 + 源码核对 + 实机静态分析 + 官方文档」逐条验证，并给出裁决建议。
> 证据等级标注：**[E]** 反编译/实测取证 · **[S]** 源码核对（SIPSorcery v8.0.12 tag 4965e8f）·
> **[D]** 官方文档 · **[I]** 静态推断（待真机验证）。
> 裁决落点见 `00` §12.2（C-25~C-27）。本文优先级低于 `03` §2 契约，高于各文档正文的技术描述。

## 0. 结论速览

| 路线 | 结论 | 裁决 |
|---|---|---|
| A. Android 发送 | **libwebrtc 全栈**（stream-webrtc-android，已在 gradle） | 采纳，修四个信令 bug |
| B. Android 接收（Q9） | 可通，但**硬解码依赖同机硬编码存在**（新风险） | Q9 门禁增加机型场景 |
| C. Windows 接收解码 | **OpenH264 软解为保底**；MF 解码在本机实测不可用（22 个激活对象全部空心） | 新增 IDecoder 抽象 |
| D. 传输层 | QUIC 判出通用传输（Win10 SChannel 依赖）；USB 零代码；`restartIce` 存在 | 维持 WebRTC 唯一 P0 |
| E. RTCP/自适应 | **C-21「只协商 goog-remb」不可实现**（SDP 无行） | C-25：PLI + SDP 后处理 + 控制通道 |
| F. 触控注入 | `dispatchGesture` API 24+，唯一 no-ROOT 路线 | 维持 |
| G. 多观众/多视频 | SIPSorcery 单 PC 单视频流发送；多观众 = 每 PC 一观众 | 登记为实现注意项 |

---

## 1. SIPSorcery 8.0.12 能力清单 [E][S]

以下均经二进制字符串核对 + v8.0.12 tag 源码核对，是 Windows 端一切媒体路线的前提。

### 1.1 接收 H264 — 可用 ✅

- 事件链：`MediaStream.ProcessVideoRtpFrame` → `RtpVideoFramer.GotRtpPacket` → **`RTCPeerConnection.OnVideoFrameReceived(IPEndPoint, uint timestamp, byte[] frame, VideoFormat)`**（另有 `OnVideoFrameReceivedByIndex`）。**没有** `OnRtpFrameReceived`/`OnVideoFrameProcessed`（部分文档/示例里的名字是错的）。
- `H264Depacketiser`：FU-A（type 28）重组、STAP-A（type 24）解析、**按 marker bit 出帧**、按 seqnum 排序（含回绕），NAL 间补 Annex-B `00 00 00 01`，`out bool isKeyFrame`（SPS/PPS → true）。
- 帧大小上限 `MaxReconstructedVideoFrameSize` 默认 **1MB**——1080p 高质量帧可超，实现时必须调大。
- STAP-B/MTAP 仅计数不解析（实际无影响：libwebrtc/浏览器发送不产生 MTAP）。

### 1.2 RecvOnly 轨道与 fmtp — 可用 ✅

- `new MediaStreamTrack(new VideoFormat(96, "H264", 90000, "packetization-mode=1;profile-level-id=42e01f"), MediaStreamStatusEnum.RecvOnly)` → answer 输出 recvonly m-line 且 **fmtp 原样回写**（`SDPAudioVideoMediaFormat.Fmtp = vf.Parameters`）。
- 匹配逻辑 `AreMatch` **只比 rtpmap，不比 fmtp** → 协商不会被 fmtp 差异卡死；但 `CheckCompatible()` 会拒绝无 `packetization-mode=1` 的 H264——本地 track 必须带 `packetization-mode=1`。
- 传输 profile：默认 `UDP/TLS/RTP/SAVP`；`RTCConfiguration.X_UseRtpFeedbackProfile = true` 才是 SAVPF。

### 1.3 RTCP 反馈 — PARTIAL ⚠️

| 能力 | 状态 | API |
|---|---|---|
| 发送 PLI | ✅（手动构造） | `pc.SendRtcpFeedback(SDPMediaTypesEnum.video, new RTCPFeedback(senderSsrc, mediaSsrc, PSFBFeedbackTypesEnum.PLI))` |
| 发送 REMB | ✅（序列化支持）但 **SDP 不会输出 `goog-remb` 协商行**（源码 `SupportedRtcpFeedbackMessages` 只产出 `transport-cc`，remb 行被注释） | `RTCPFeedback.GetBytes()` AFB/REMB |
| 解析反馈 | ✅ PLI/NACK/REMB/TWCC 均可解析，经 `OnReceiveReport(ByIndex)` 暴露 | — |
| NACK 重传 | ❌ 无自动重传 | — |
| 发送 TWCC | ✅ 含 `TransportWideCCExtension` 外发 | `SendRtcpTWCCFeedback` |
| FIR | ❌ `GetBytes` 走 default 分支（未实现） | — |
| ICE restart | ✅ `pc.restartIce()`（W3C 风格；`RTCOfferOptions` 无 iceRestart 标志） | — |
| DTLS 角色 | ⚠️ 仅 `pc.IceRole`（`setRemoteDescription` 会覆写，须在其后设置） | — |
| 多视频流发送 | ❌ `SendVideo` 只作用 `VideoStreamList[0]`；第二个本地视频 track 会覆盖主 stream 的 LocalTrack | 多显示器同发需多 PC |

**对裁决的影响**：`C-21`（只协商 goog-remb）在 SIPSorcery 上不可实现 → `00` §12.2 **C-25**。

### 1.4 替代自适应路线（C-25 的落地设计）

1. **关键帧**：新观众入会 → 接收端按 1.3 构造 PLI 发给发送端（发送端为 libwebrtc 时，需 answer SDP 含 `a=rtcp-fb:<pt> pli`，见 3）。
2. **answer SDP 后处理**：SIPSorcery 生成的 answer 字符串在返回给对端前，追加 `a=rtcp-fb:<pt> pli` / `nack` / `goog-remb` 行（我们完全控制 answer 文本，确定性可行；libwebrtc 按协商结果启用对应 RTCP 行为）。
3. **码率控制走控制 DataChannel**：接收端由 RTCP RR（`OnReceiveReport` 的 loss/RTT）+ 本地渲染统计计算目标码率 → 控制通道下发 → 发送端调节：Android `RtpSender.setParameters(maxBitrateBps)`；Windows 直接改 OpenH264 码率。REMB RTCP 包作为浏览器路径备用（浏览器自行管理，无需我方参与）。
4. C-24 四模式与 3 兼容：模式 = 预设（分辨率/fps/码率不变量），自适应 = 模式内动态码率上限。

---

## 2. 路线 A：Android 发送端 ✅

**选型：libwebrtc 全栈**（`io.getstream:stream-webrtc-android:1.1.1`，已在 `android/app/build.gradle.kts:56`）。

- 否决自研 MediaCodec→RTP 栈：需自研 ICE + DTLS-SRTP（BouncyCastle DTLS 存在但工作量与脆弱度不成比例），且放弃 libwebrtc 自带的拥塞控制/ jitter buffer/硬编适配。
- 采集：`ScreenCapturerAndroid`（MediaProjection，libwebrtc 内置）；编码：`DefaultVideoEncoderFactory`（HW H264 via MediaCodec）。
- 信令继续走 Windows 侧 9527 HTTP（对齐 `03` §2.3），必须先修四个已确认 bug：
  1. `WebRTCService.kt:146-151` offer JSON 缺 `fromPeerId`/`capability`（实锤：`grep fromPeerId` = 0）；
  2. `WebRTCService.kt:132-136` `scope.launch { delay(2000) }` 为空协程，`sendSdp` 立即执行（纯 no-op）；
  3. `/ice` POST 的 `peerId`（"android-client" 默认）与 offer 无关联 → Windows `_pendingIceConnections` 键不匹配；
  4. 从不 GET `/ice-pending`。
- **Android 8（API 26）下限兼容清单** [D]（minSdk 已是 26，`build.gradle.kts:13`）：
  - 通知渠道：API 26 必须，FGS 前先创建；
  - `startForegroundService` 语义：API 26 起（用户主动触发场景合规）；
  - `startLocalOnlyHotspot`：**API 26 起步**，恰好覆盖下限；API 33+ 需 `NEARBY_WIFI_DEVICES`，26-32 需定位权限；
  - `dispatchGesture`：API 24+，覆盖；
  - FGS 类型声明（`mediaProjection`）：API 34+ 强制，旧机忽略属性但声明无害；`POST_NOTIFICATIONS` 运行时权限仅 API 33+；
  - MediaProjection 授权：**每次会话弹一次**（Android 14 起强化，[D1][D2]），Android 15 计入 6 小时/天 FGS 上限 [D] → 授权 UX 按「每次投屏一次确认」设计。
- **v7a 32 位**：AAR 实测含 `jni/armeabi-v7a/libjingle_peerconnection_so.so`（6.4MB，另 arm64/x86/x64），manifest `minSdkVersion="21"` [E]；32 位进程内存上限 ~3GB → **v7a/低配设备建议 720p30 档**（对应 C-26 分档）。
- 风险：v7a 老设备 HW AVC encoder 普遍存在（Codec2/OMX），但个别低端机 Only-CB 编码；libwebrtc 会按协商兜到 42e01f。

## 3. 路线 B：Android 接收端（Q9）⚠️

- SIPSorcery（Windows 发送）→ libwebrtc（Android 接收）的 SDP 互操作：fmtp 不阻塞协商（1.2），但 Windows track 必须带 `packetization-mode=1`（现有 `PeerConnection.cs:54` 无 fmtp，B-4 已列为出画阻断）。
- **新风险（本轮发现，登记为 Q9 附加门禁）[S][D]**：Android libwebrtc 的 H264 **硬解码以同机存在 H264 硬编码为前提**（[webrtc issue 42220261](https://issues.webrtc.org/42220261)，华为麒麟等设备踩坑），且 libwebrtc Android 无 H264 软解兜底 → 无 HW AVC encoder 的机型**收不了** Windows 的 H264 流。Q9 门禁需覆盖此类机型；兜底方向（Windows 发 VP8）依赖 libvpx，列为候选不进 P0。
- 其余确认项：`ScreenMirrorTab.kt:106-112` 未共享 `EglBase`（`WebRTCService.kt:24` 私有）、`Platform.kt:66-67` `peers` 抛异常、Manifest 7 权限 0 `<service>`、`usesCleartextTraffic=true`（LAN 明文信令依赖项，收敛到私有网段后再收紧）。
- Q9 判定：**静态推断维持「默认不可用」，但全部阻断项均为小修**（信令 4 bug + EglBase + fmtp），M0/G1 真机 PoC 可在修复后一次通过。

## 4. 路线 C：Windows 接收/解码/渲染

### 4.1 Media Foundation 解码 — 本机实测不可用 [E]（探针 2026-11-14）

在 build 10.0.26300 实测（P/Invoke 探针，`/tmp/decprobe`）：

| 探针 | 结果 |
|---|---|
| `MFTEnumEx(VIDEO_DECODER, flags=0x4A)`（软/本地） | hr=0, **count=0** |
| `MFTEnumEx(VIDEO_DECODER, flags=0xFF)`（全部，含硬异步） | hr=0, **count=22** |
| `CoCreateInstance(MSH264DecoderMFT {62CE7E72-…} → IMFTransform)` | **0x80040154 REGDB_E_CLASSNOTREG** |
| 22 个激活对象 `ActivateObject(IMFTransform)` | **全部 0xC00D36E6**，且 DISPLAYNAME/URL/CLSID 属性全空 |
| 穷举 64 个注册 Transform CLSID QI IMFTransform | **0/64** |

结论：该 Canary 构建的 MFT 激活链路系统性损坏（枚举出幽灵对象但无法激活），解码与编码同病。**零售版 Windows（10 1809+/11）不受此影响**——软解 "Microsoft DTV-DVD H264 Decoder MFT" 在零售系统可正常 CoCreateInstance。

### 4.2 解码路线：IDecoder 抽象 + OpenH264 保底 ✅

- **保底：H264Sharp.H264Decoder**（openh264 2.4.1，仓库已有依赖，**零新增**）：Annex-B 输入 → YUV420 输出；`Decode(EncodedData/byte[], bool, out DecodingState, out YUVImagePointer)` 与 SIMD **YUV→RGB 直写 `RgbImage`** 重载（免一次 YUV 拷贝）[E]。android-arm32 的 `.so` 也在包内（v7a 兜底可用）。
- 设计：`IDecoder`（`Initialize/Decode/Dispose`）→ `OpenH264Decoder`（全平台保底）+ `MediaFoundationDecoder`（运行时探针通过才注册的加速器，零售 Windows 生效）。与编码器策略对称。
- 1080p30 软解在现代 CPU 余量充足；1080p60 在低端 CPU 吃紧（与 C-26 分档一致）。

### 4.3 渲染

- 1080p30：`WriteableBitmap`（每帧 ~8MB CPU 拷贝，30fps 可接受）——最小实现；
- 1080p60：`SwapChainPanel` + D3D11（解码出 NV12/I420 → D3D11 纹理 → shader YUV→RGB），或 SIMD 预转 BGRA 后 `ID3D11Device` 上传。第二步仅在 C-26 的 60fps 档位触发。

## 5. 路线 D：传输层

| 项 | 结论 | 证据 |
|---|---|---|
| QUIC（`System.Net.Quic`） | **判出通用传输**：.NET 8+ 捆绑的 msquic 依赖 **Win11/Server 2022** 的 SChannel QUIC 支持，零售 Win10 直接「not supported」，除非用户手装非官方 OpenSSL 版 libmsquic [D] | learn.microsoft.com/dotnet/fundamentals/networking/quic；dotnet/runtime readme |
| USB/ADB 隧道 | **零代码**：`adb forward tcp:9527 tcp:9527` 即可承载全部 HTTP 信令与 DataChannel | adb 官方文档 |
| Wi-Fi Direct | Windows 侧 API 在钉死的 22621.38 投影中存在（`WiFiDirectAdvertisementPublisher` 等 7 符号实测）[E]；Android 侧骨架烂（B-2/B-9 已列） | 维持 P1，热点先行 |
| ICE restart / 链路自愈 | `pc.restartIce()` 存在 [S] → R0-13 可实现 | 1.3 |
| 多链路仲裁 | `ILinkManager` 设计不受影响；仅注意 `System.Net.NetworkInterface` 在热点模式下看不到 SSID，需 `ConnectivityManager.NetworkCallback`（Android 侧） | 维持 03 §3.2 |

## 6. 路线 F：触控注入

- `AccessibilityService.dispatchGesture`：API 24+（覆盖 Android 8 下限）[D]；唯一 no-ROOT 路线（`INJECT_INPUT_EVENT` 权限仅系统应用，V5 约束成立）。
- Android 13+ 侧载应用启用无障碍需手动跳「受限设置」——UX 已知项（引导页）。
- 延迟量级 50–150ms（手势事件走系统队列）→ M3 的「落点误差 ≤1% 屏宽」可达，「端到端 ≤80ms」的控制链路不受此约束（控制信令走 DataChannel，注入是末端）。
- FLAG_SECURE / 锁屏 / BiometricPrompt：维持不做（00 §7）。

## 7. 路线 G：多观众 / 多显示器

- 多观众：现状即「每观众独立 PeerConnection」，SIPSorcery 无共享流需求；
- 多显示器**同时**发送：单 PC 只有一条视频流（1.3）→ 若 P1 要「多显示器同发」，需多 PC 或 track 轮换，登记为实现注意项，不进 P0。

## 8. 对既有裁决的影响汇总

| 编号 | 内容 | 状态 |
|---|---|---|
| **C-25** | goog-remb 协商不可实现 → 改「PLI + rtcp-fb SDP 后处理 + 控制通道码率」（§1.3/1.4） | 本文提出，`00` §12.2 采纳 |
| **C-26** | Q13 分档修订：v7a/低配 720p30 为 P0 基线；1080p60 绑硬编路线（D3D11 Video Encoder / 厂商 API，**Vortice 未封装需自写 COM**）降 P1 | 本文提出，`00` §12.2 采纳 |
| **C-27** | Android 8/v7a 兼容：minSdk 26 已满足（gradle 现值）、AAR 实测含 v7a、minSdk 21；04 的 Android 实现指导补 API 26/33/34/35 条件分支清单（§2） | 本文提出，`00` §12.2 采纳 |
| — | 新风险：Android H264 解码依赖同机硬编码（§3） | Q9 门禁附加场景，`05` 门禁清单引用 |
| — | Windows 解码：IDecoder 抽象（OpenH264 保底 + MF 运行时探针加速器）（§4） | `03` RemoteRenderer 注记 |
| — | QUIC 出通用传输（§5） | `03` QuicDatagramTransport 注记 |

## 9. 参考来源

- [Android 14 FGS types required](https://developer.android.com/about/versions/14/changes/fgs-types-required) · [FGS service types](https://developer.android.com/develop/background-work/services/fgs/service-types) · [MediaProjection on Android 14](https://stackoverflow.com/questions/77307867/screen-capture-mediaprojection-on-android-14)
- [MDN: WebRTC codecs（pm=1 强制）](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/WebRTC_codecs) · [webrtc issue 42220261（Android H264 硬解码依赖硬编码）](https://issues.webrtc.org/42220261) · [bloggeek H264 profiles](https://bloggeek.me/webrtcglossary/h-264)
- [.NET QUIC 支持](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview) · [System.Net.Quic readme](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.Quic/readme.md) · [Unofficial.MsQuic](https://www.nuget.org/packages/Unofficial.MsQuic)
- [GetStream/webrtc-android](https://github.com/GetStream/webrtc-android) · Maven Central `io.getstream/stream-webrtc-android/1.1.1` AAR 实测
- SIPSorcery v8.0.12 源码（tag 4965e8f）：RTPSession/VideoStream/MediaStream/H264Depacketiser/RTCPFeedback/RTCPeerConnection
