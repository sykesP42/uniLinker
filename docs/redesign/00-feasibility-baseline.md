# 00 · 可行性评估基线（上一轮评审归档）

> 本文件由主理人齐活林整理归档，内容来自产品经理许清楚的需求结构化清单与架构师高见远的只读代码评审（2026-11-12）。
> 用途：作为本轮「多套重构方案 + 详细开发文档」的统一设计基线。所有方案必须以此处的**源码实证**为出发点，不得与已核实事实冲突。
> 状态：**只读基线**。若后续真机验证推翻了标注「静态推断」的条目，请修订本文件并在修订记录中说明。

---

## 1. 用户需求与默认假设

原话：「实现 windows 端和安卓端间或者双安卓设备间通过有线或无线方案，不用联网扣费（指的是有开销的移动数据网络）（允许局域网）。实现高质量屏幕共享，设备控制等功能。」

关键澄清：用户自注「不用联网扣费」指**禁止计费蜂窝移动数据**，并非禁止一切网络；局域网、Wi-Fi Direct、热点、USB 链路均允许。

已采用的默认假设（用户尚未逐条拍板，方案需标注「基于默认假设」）：

| 编号 | 默认假设 |
|---|---|
| Q1 | P0 屏幕共享只保单向（Windows→Android、Android→Android 单向），双向互投列 P1 |
| Q2 | P0 = 控制 Android 被控端；控制 Windows 列 P1 |
| Q3 | Android→Windows 投屏列 P1，不阻塞 P0 |
| Q4 | 最低档为 P0 验收线（720p/30fps/2-4Mbps/≤150ms/不含音频/仅主显示器）；目标档 1080p60/≤80ms/ABR 列 P1 |
| Q5 | 音频列 P1，可开关 |
| Q6 | 允许安装/首次授权阶段联网；运行期零互联网、零移动数据 |
| Q7 | P0 保无线：同一 Wi-Fi LAN + 无路由器直连（Wi-Fi Direct 与热点二选一即可）；有线三型列 P1 |
| Q8 | 无障碍服务为主 + ADB 为辅，不依赖 ROOT；多显示器/窗口采集列 P1 |

硬约束：C1 运行期零蜂窝流量；C2 不依赖互联网/云信令/中继/账号/遥测；C3 信令与媒体仅本地链路、目的地址无公网 IP；C4 无 SIM / Wi-Fi-only 设备全功能可用；C5 断开 WAN 后全功能可用。
验收判据 A 飞行模式+直连可用且流量不增；B 拔 WAN 全功能；C 拔 SIM 可作发送/接收/被控；D 抓包目的地址均私有网段或 USB、无公网 DNS；E 仅 USB 关 Wi-Fi 与蜂窝可用；F 无计费/无登录弹窗。

---

## 2. 现状能力矩阵（源码实证）

| 模块 | Windows (C#) | Android (Kotlin) | 浏览器 |
|---|---|---|---|
| 设备发现 | 可运行：mDNS 广播 + Zeroconf 浏览 `src/UniLinker.Core/DiscoveryService.cs:47-138` | 骨架/单向：NSD 只被动浏览、不广播自身 `android/.../strategies/LanMdnsStrategy.kt:39-41`；`core/DiscoveryService.kt` 完整实现但 `Platform.kt:16-28` 未实例化（死代码）；连接码 UDP 9528 可跑 `ConnectionCodeStrategy.kt:59-117` | 无 |
| 信令 | 可运行但单向：`TcpListener(IPv6Any, 9527)` DualMode `SignalingServer.cs:60-66`；只处理 offer，answer 分支直接返回 ok `:199-208` | **缺失服务端**：全项目 Grep `ServerSocket/<service>` 零命中，全部为 OkHttp 客户端 | 参考实现 `web/app.js:165-196` |
| WebRTC 发送 | 可运行：`MediaStreamTrack(H264,96,90000,SendOnly)` `PeerConnection.cs:53-56`，SIPSorcery FU-A `:224-236` | **缺失**：Grep `MediaProjection/VideoSource/addTrack` 零命中；offer 为 `OfferToReceiveVideo=true` `WebRTCService.kt:125` | 无 |
| 接收渲染 | **缺失**：`PeerConnection.cs:38,55` 恒 SendOnly；`PeerMesh.cs:420-425` 明示 Windows 不解码 | 可运行：`SurfaceViewRenderer` + `addSink` `ScreenMirrorTab.kt:103-112` | 可运行 `web/app.js:115-124`，E2E 出帧 `ScreenMirrorTests.cs:116-157` |
| 采集编码 | 可运行：WGC BGRA `WgcCapture.cs:69-85`，仅主显示器 `:342-346`，全帧 GPU→CPU readback `:173-181`；OpenH264 **软编** `OpenH264Encoder.cs:35-52,148` | **缺失** | 无 |
| 控制 / 注入 | 通道基座有（`Platform.cs:33-34,92-106` 的 `IIncomingChannelHandler` 分发），注入缺失 | **全缺**：`WebRTCService.kt:102` onDataChannel 空；Grep `AccessibilityService/dispatchGesture/injectInputEvent` 零命中 | 无 |
| 文件传输 | 可运行：DataChannel + SHA256 `src/UniLinker.Plugins.FileTransfer/` | 缺失 | 无 |
| 角色/策略抽象 | 抽象有但未接线：`DeviceRole.cs:4-10`、`IConnectionStrategy.cs:13-32` 全项目零实现类 | 残缺：`Models.kt:24` DeviceRole 零使用；`Platform.kt:64-77` `IPluginContext.peers` 直接抛 `UnsupportedOperationException` | — |
| Wi-Fi Direct | 无 | **骨架/不可用**：`WifiP2pStrategy.kt:153-179` connect 成功路径永不 resume；`:146` 把 MAC 当 IP；`:181-197` handleConnectionChange 只有注释；`:201-204` accept 返回空壳；`:48` incomingConnections 从不 emit | — |
| 权限声明 | 首次运行需放行防火墙 `README.md:43` | `AndroidManifest.xml:4-35` 仅 7 个权限 + 1 个 Activity，**零 `<service>`**；缺 FOREGROUND_SERVICE / FOREGROUND_SERVICE_MEDIA_PROJECTION / POST_NOTIFICATIONS / SYSTEM_ALERT_WINDOW / BIND_ACCESSIBILITY_SERVICE / 存储权限 | — |

## 3. P0 覆盖度结论

10 项中 **1 项已具备、4 项需改造、5 项缺失**，当前 P0 验收线不可达。
四个从零补齐的致命缺口：

1. **Android 采集/编码/发送**（场景②③、P0-6、P1-1）
2. **Android 本地信令服务端**（双 Android 场景没有一方能当信令方；`Platform.kt:19-21` 把 mesh 指向 `localhost:9527`）
3. **Windows 接收渲染**（场景②）
4. **设备控制全栈**（控制通道、输入注入、坐标映射、授权/撤销/常驻指示）

## 4. 零流量约束结论（改造量最小的一项）

- 代码无云、无账号、无中继、无遥测；Windows 侧本就 `iceServers = []` `PeerConnection.cs:48`。~~`DebugConnectionTest.cs:45` 与 `ScreenMirrorTests.cs:116-157` 证明纯 host candidate 在 LAN 可建连~~ → **本表述已由 §12 修订记录 B-1 降级为「高置信静态推断」，列为 M0/G1 门禁验证项，不是既成事实**；E2E 证明的是 localhost 带 STUN 的浏览器观看全链路。
- 唯一违规：Android `WebRTCService.kt:76` 与 `web/app.js:112` 各有一处 `stun:stun.l.google.com:19302` 公网 STUN → 触发公网 DNS + UDP，判据 D 必然失败。改成 `[]` 零功能损失。
- `SignalingServer.cs:60` 的 `IPv6Any` DualMode 绑定包括蜂窝在内的所有网卡，需收敛到私有网卡白名单或防火墙规则。
- 改完后判据 A/B/C/D/F 可达；**判据 E（仅 USB）当前完全不可达**，全项目零 USB/ADB/RNDIS 代码。
- `WebRTCService.kt:62` `disableNetworkMonitor=true` 在链路切换（Wi-Fi Direct 组成立、热点开关、Wi-Fi↔USB）时不刷新 host candidate、不 ICE restart，是直连场景硬伤。

## 5. 连接方案实证结论

- **热点自组 LAN 优于 Wi-Fi Direct**：热点主天然是固定信令方（GO 角色由系统协商、只能在连接建立后才知道），可复用 mDNS + 9527；Windows 侧无 Wi-Fi Direct 实现，Win↔Android 直连不可用。Wi-Fi Direct 建议绕开 mDNS，直接用 `WifiP2pInfo.groupOwnerAddress` + 连接码/二维码确认身份。
- mDNS 在热点下需补 `WifiManager.MulticastLock.acquire()`（Manifest 有 `CHANGE_WIFI_MULTICAST_STATE` 但代码未 acquire），国产 ROM 常默认过滤组播。
- ADB reverse 仅适合 PC↔单设备；双 Android 需 PC 中转或 USB OTG + RNDIS，且需开发者选项授权，不能作普通用户默认路径。

## 6. 高质量可达性与硬瓶颈

- 最低档 720p30/≤150ms **可达**（延迟预算 ≈37–143ms：采集+readback 5–15ms、节流 0–33ms、软编 10–25ms、RTP+LAN 2–10ms、jitter buffer 10–30ms、解码渲染 10–30ms）。
- 目标档 1080p60/≤80ms **大概率不达标**，硬瓶颈：`PeerMesh.cs:464` 把 RTP 时间戳写死 `90000/30`；OpenH264 软编基本不产 High/CABAC；WGC 全帧 BGRA readback（1080p ≈8.3MB/帧）；无 ABR/PLI/FIR/NACK。
- `CLAUDE.md:66` 明确 MediaFoundation SinkWriter 硬编旧路 `MFCreateMemoryByteStream` 不存在、不可复活 → Windows 换硬件编码需重新设计编码层。
- SDP 互通风险：`PeerConnection.cs:54` 未显式设置 `profile-level-id`/`packetization-mode`，Android `WebRTCService.kt:50` 开了 `WebRTC-H264HighProfile/Enabled/` field trial；RFC 7742 要求 in-band 参数集。仓库无 SIPSorcery↔libwebrtc 互通测试，需真机抓 SDP 比对。

## 7. 平台级不可行项（必须写入需求边界）

- 无 ROOT 下安全锁屏、`FLAG_SECURE` 窗口、BiometricPrompt/系统授权弹窗不可注入（`INJECT_EVENTS` 需系统签名）。
- Windows UAC 安全桌面无法用 `SendInput` 注入。
- Android 投屏发送（MediaProjection，每次弹窗、token 一次性、targetSdk≥34 强制 `foregroundServiceType="mediaProjection"`）与被控注入（无障碍服务，需用户到系统设置手动开启）是**两套独立授权**，互不替代。

## 8. 静态推断、待实机验证（最大工期未知项）

**Android 现有接收链路很可能根本连不通**（Q9）：
1. `WebRTCService.kt:145-150` POST `/signaling` 缺 `fromPeerId`/`capability`（正确格式见 `web/app.js:179-184`）；
2. `:134` `scope.launch{delay(2000)}` 是 fire-and-forget，不阻塞 `:135 sendSdp` → offer 不含 ICE candidate（对比 `web/app.js:239-253` 的真实等待）；
3. `:160-169` `/ice` 携带 `peerId="android-<ts>"`，而 Windows `_pendingIceConnections` key 是 `"unknown"` `SignalingServer.cs:241-244` → 候选被静默丢弃 `:330-341`；
4. Android 从不 GET `/ice-pending`。

> 仓库 E2E 仅覆盖浏览器端。**任何方案在排期前都应以真机 PoC 回答 Q9。**

## 9. 上一轮推荐路线与里程碑（供本轮方案对比的参照系）

推荐路线 A：沿用现有 WebRTC 插件架构全面补齐，ADB/USB 隧道仅作 P1 补充。
任务 T01 零流量合规+信令协议对齐+角色基座 → T02 双端收发能力补齐 → T03 角色协商+无路由器直连 → T04 控制通道+触控注入+坐标映射+授权 → T05 质量达标+增强项。
里程碑 M1 双 Android 同 LAN 单向投屏出帧 → M2 热点直连+零流量抓包 → M3 触控坐标映射 → M4 授权与撤销 → M5 质量达标。

## 10. 关键证据索引

`SignalingServer.cs:60-66,134-176,199-208,215-311,330-341,515-519,582-588` · `PeerConnection.cs:38,48,53-56,224-236` · `PeerMesh.cs:246-309,394-477(464)` · `DiscoveryService.cs:47-138` · `Platform.cs:33-34,92-106` · `ScreenMirrorPlugin.cs:18,36-67,139-140,154-160` · `WgcCapture.cs:35-41,69-85,173-181,316-346,465-468` · `OpenH264Encoder.cs:35-52,119-135,148` · `web/app.js:111-113,165-196,239-253` · `ScreenMirrorTests.cs:116-157` · `DebugConnectionTest.cs:45` · `AndroidManifest.xml:4-35` · `build.gradle.kts:13-16,40-69` · `WebRTCService.kt:50,54-62,74-80,102,105-112,122-169` · `ScreenMirrorPlugin.kt:17,51-94` · `ScreenMirrorTab.kt:26,35-38,103-112` · `WifiP2pStrategy.kt:48,146,153-197,201-204` · `Platform.kt:16-28,64-77` · `LanMdnsStrategy.kt:36,39-41,47-96` · `ConnectionCodeStrategy.kt:38,59-117` · `DeviceRole.cs:4-10` · `IConnectionStrategy.cs:13-32` · `README.md:17-19,43,70,104-106` · `CLAUDE.md:56-57,66,73-77`

## 11. 本轮必须回答的待决策问题

Q9 Android 现有接收是否真机验证可用（影响工期最大）；Q11 能否接受「一台开热点」为默认、还是必须 Wi-Fi Direct；Q13 1080p60/≤80ms 是否硬性（决定是否换硬件编码）；Q14 Windows 接收端用 WebView2 复用 `web/app.js` 还是原生解码；Q10 是否接受两次独立授权；Q12 有线链路是否允许依赖 ADB/开发者选项/PC 辅助；Q15 是否保留 DTLS-SRTP 并追加应用层 PIN 配对；Q16 是否允许修改 `web/app.js`；Q17 Windows 侧是否需要禁用蜂窝网卡；Q18 是否坚持「发送方=控制方」单向角色；Q19 是否接受用 `WgcCapture.cs:184-192 TimestampUs` 做延迟打点。

---

## 12. 修订记录（2026-11-12）

> 主理人齐活林据架构师高见远阶段 1 复核回写。**本节优先级高于正文**：凡正文 §2 / §4 / §9 与本节冲突，以本节为准；正文原文保留以便追溯。修正均不改变上一轮可行性结论方向，只收紧表述强度或明确适用边界。

| 编号 | 正文原表述 | 修正后表述 | 证据 | 对后续文档的影响 |
|---|---|---|---|---|
| **B-1** | §4：「`DebugConnectionTest.cs:45` 与 `ScreenMirrorTests.cs:116-157` **证明**纯 host candidate 在 LAN 可建连」 | **降级为高置信静态推断**：E2E 证明的是 localhost 全链路出帧（且浏览器端仍带 STUN 配置），不是「零 STUN 的 LAN / 热点可建连」；`DebugConnectionTest.cs:44-68` 只 dump answer、未断言 connected；`web/app.js:111-113` 仍配 STUN；`PeerConnection.cs:48` 本就 `iceServers=[]` | `DebugConnectionTest.cs:44-68`、`web/app.js:111-113`、`PeerConnection.cs:48` | 「去 STUN 后能否建连」列为 **M0 / G1 门禁验证项**，不得当作既成事实；`02` §0、`03` §6、`05-test-strategy.md` 已按此设计。上一轮对话中「Windows 侧已证明纯 host candidate 可建连」的说法以本条为准 |
| **B-2** | §2 表格：「连接码 UDP 9528 **可跑**」 | 补注「**可跑但双 Android 不可用**」：广播的 `IP:9527` 在 Android 侧无监听者，不能计入 P0-1 / P0-4 的发现手段 | `ConnectionCodeStrategy.kt:64`、`Platform.kt:19-21`（mesh 指向 `localhost:9527`） | `01` 需求池与 `02` 方案对 P0 发现手段的计数以修正后为准 |
| **B-3** | §9「上一轮推荐路线 A」 | 明确为**参照系**而非本轮结论；本轮允许推翻，加权推荐为方案 ②（3.80/5.00） | `00` 原文已注明「供本轮方案对比的参照系」；`02` §6 | 本轮方案选型以 `02` §6 为准 |
| **B-4**（口径差异，待用户确认） | §1 Q18 默认「P0 只保单向」（Q1） | 产品经理 `01` §6 A2 的 Q18 默认答案为「**不坚持单向**、改为角色可协商的对称架构，但 P0 业务流仍只交付单向」。本轮按 **PM 默认**推进（支撑 D-01 强制废弃硬编码单向角色，也是方案 ② 的 D3=4 主因） | `01` §6 A2、`02` §9.2、`03` §9.1 | **若用户改判「坚持单向」，将与 D-01 冲突，须显式确认并重打矩阵 D1/D3/D6/D9**。选编号时需一并提示用户 |

另补两处硬实证（不改变结论）：`PeerMesh.cs:464` 的 `90000/30` RTP 时间戳硬编码；`WgcCapture.cs:465-468` 的 `CreateForWindow` 已声明但未被调用。

**Q18 / B-4 是当前唯一需要用户显式拍板的口径冲突**；Q9 / Q11 / Q13 / Q14 为阻塞选型问题，均按 `01` §6 默认答案推进并标注「基于默认假设」。
