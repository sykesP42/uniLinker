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
| Q4 | **已反转（C-18，2026-11-14）**：目标档 `1080p60 / ≤80ms` 升为 **P0 硬指标**；原最低档 `720p30 / ≤150ms` 降为 **B 档弱网兜底**（`≤120ms`）。硬编层由「条件性投入」改为 P0 必做 |
| Q5 | 音频列 P1，可开关 |
| Q6 | 允许安装/首次授权阶段联网；运行期零互联网、零移动数据 |
| Q7 | P0 保无线：同一 Wi-Fi LAN + 无路由器直连（Wi-Fi Direct 与热点二选一即可）；有线三型列 P1 |
| Q8 | 无障碍服务为主 + ADB 为辅，不依赖 ROOT；多显示器/窗口采集列 P1 |

| Q13 | **已拍板（C-18）**：硬性。硬编层 + 零拷贝采集 + 原生接收端为 P0 必做 |
| Q14 | **已反转（C-19）**：Windows 接收端用原生 `MirrorView`（MF 硬解），不再考虑 WebView2 复用 `web/app.js` |
| Q18 | **已关闭**：不做单向角色，改为对称角色协商（`IRoleNegotiator`） |

硬约束：C1 运行期零蜂窝流量；C2 不依赖互联网/云信令/中继/账号/遥测；C3 信令与媒体仅本地链路、目的地址无公网 IP；C4 无 SIM / Wi-Fi-only 设备全功能可用；C5 断开 WAN 后全功能可用。
验收判据 A 飞行模式+直连可用且流量不增；B 拔 WAN 全功能；C 拔 SIM 可作发送/接收/被控；D 抓包目的地址均私有网段或 USB、无公网 DNS；E 仅 USB 关 Wi-Fi 与蜂窝可用；F 无计费/无登录弹窗。

### 1.1 产品定位：uniLinker 这个名字是什么意思（C-22）

名字不是装饰，它规定了产品边界。三端现有文案把产品说成「投屏工具」——`web/index.html:6` 的标题直接是
`UniLinker - Screen Mirror`，而 `uniLinker` 本体只出现在配置路径（`App.xaml.cs:56,147`）里，
**在用户可见处没有任何定位陈述**。现收口为：

| 词素 | 含义 | 对产品的约束 |
|---|---|---|
| `uni-` | **统一** | 三端一套协议；四种链路（有线 / 5GHz / 热点 / Wi-Fi Direct）一个入口；一条画质阶梯（四模式）而不是三端各一套 |
| `-Linker` | **连接件** | 产品是「把异构设备连成一块可用屏幕的中间件」，不是「投屏软件」。投屏是第一个能力，不是产品定义（`README.md:9` 已写对：「投屏只是第一个功能」） |

由此派生三条硬要求，写进需求边界：
1. **术语唯一**：屏幕相关动作只用「投屏 / Mirror」；「共享」归还给文件传输
   （现状歧义实证：`ShareViewModel` 是投屏、`FileTransfer` 是文件共享，导航项却叫 `Share`（`MainWindow.xaml:75`））。
2. **不出局域网**：局域网连接器不向外部世界发请求——因此 Android / Web 的公网 STUN 配置（`WebRTCService.kt:76`、`web/app.js:112`）
   属于自我否定，必须移除。
3. **一条阶梯**：四模式（帧率优先 / 画质优先 / 均衡 / 节能）在三端语义完全一致，只有发射端可选，接收端只读。

定位语（对外）：**uniLinker · 把所有屏幕连成一块** / EN: **uniLinker — every screen, one link**。
能力句：局域网内 Windows / Android / 浏览器互投与控制，零计费流量。

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

> **2026-11-14 修订（C-18）**：本节原判定「目标档 1080p60/≤80ms 大概率不达标」并据此把目标档列为 P1。
> 该判定在**结论方向**上仍然成立（现状确实做不到），但**归因错了**——原判断把病根指向编码器，实际病根在采集回读。
> 归因修正后，达标路径是确定的工程路径而非「赌一把」，因此目标档升为 P0 硬指标。以下为新归因。

### 6.1 病根在采集回读，不在编码器

`WgcCapture.cs:173-192` 每帧走完整条「GPU → CPU」链路：

```
CopyResource(→ staging)  →  Map(Read) 同步阻塞  →  Marshal.Copy  →  new byte[rowPitch×height]
```

1080p BGRA 单帧 = 8.29 MB，60fps 即 **≈497 MB/s 的托管堆分配**（全部落 LOH），且 `Map` 是同步阻塞调用，
采集线程被它掐住。更关键的是 `CaptureFrame.D3dTexture` 恒为 `nint.Zero`（`:187`）——
**纹理指针被主动丢弃**，于是下游除了 CPU 像素之外无路可走，硬编路径在数据结构层就被堵死了。

对比：`OpenH264Encoder.cs:148` 的 `IsHardware: false` 只是「用了软编」，而回读是「**只能用软件路径**」。后者才是硬约束。

### 6.2 延迟预算与六项必改（设计目标，实测待 G2 门禁）

| 环节 | 现状 | 目标 | 必须的改动 |
|---|---|---|---|
| 采集回读 | 3–6 ms | 0 | GPU 直通（`ID3D11VideoProcessor` 出 NV12），纹理指针下传 |
| 色彩转换 | 3–5 ms | 0.3 ms | GPU `VideoProcessorBlt` BGRA→NV12 |
| 编码 | 8–15 ms（软编） | 2–4 ms（硬编） | MF 硬件 H.264 MFT |
| 打包/发送 | ~1 ms（无 pacer，突发） | ~1 ms（平滑） | 5ms 令牌桶 pacer |
| 网络（5GHz） | 3–8 ms | 3–8 ms | — |
| 接收抖动缓冲 | 10–30 ms（拥塞时无限增长） | 8–12 ms | `playout-delay=0` + 目标延迟调优 |
| 解码渲染 | 10–30 ms（浏览器） | 3–5 + 8–16 ms | 原生硬解 + 单帧缓冲 |

**结论：六项全改才可能进 80ms；只换编码器最好情况仍约 70–110ms，P95 必然 >80。**
这一条是本轮最重要的修正——它把「做不到」从「物理不可行」降级为「工程未做」。

### 6.3 其余硬瓶颈（仍然成立）

- `PeerMesh.cs:464` 把 RTP 时间戳写死 `90000/30`：选 60fps 时接收端按 30fps 还原，实际丢一半帧，且抖动估计随帧累积。
- 无 ABR / pacer / PLI / FIR / NACK：`OpenH264Encoder.cs:65` 的 `ForceKeyFrame()` 是死代码，全仓无 RTCP 订阅。
- `CLAUDE.md:66` 禁的是已被移除的 `MFCreateMemoryByteStream` SinkWriter 封装，**不是 MFT 本身**；
  直用 MF MFT（`MFT_ENUM_FLAG_HARDWARE`）是 WebRTC for Windows 的标准做法，不违反该红线。换硬编不需要推翻编码层架构。
- SDP 互通风险（升级为**出画阻断**）：`PeerConnection.cs:54` 既无 `profile-level-id` 也无 `packetization-mode`，
  而 `packetization-mode` 缺省为 0（单 NAL 模式），大 IDR 帧无法投递；Android `WebRTCService.kt:50` 的
  `WebRTC-H264HighProfile/Enabled/` field trial 只影响协商倾向，不能补上缺失的 fmtp。

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

Q9 Android 现有接收是否真机验证可用（影响工期最大）；Q11 能否接受「一台开热点」为默认、还是必须 Wi-Fi Direct；~~Q13 1080p60/≤80ms 是否硬性~~（**已拍板：硬性，C-18**）；~~Q14 Windows 接收端用 WebView2 复用 `web/app.js` 还是原生解码~~（**已反转：原生 `MirrorView`，C-19**）；Q10 是否接受两次独立授权；Q12 有线链路是否允许依赖 ADB/开发者选项/PC 辅助；Q15 是否保留 DTLS-SRTP 并追加应用层 PIN 配对；Q16 是否允许修改 `web/app.js`；Q17 Windows 侧是否需要禁用蜂窝网卡；~~Q18 是否坚持「发送方=控制方」单向角色~~（**已关闭：对称协商**）；Q19 是否接受用 `WgcCapture.cs:184-192 TimestampUs` 做延迟打点（**已采纳并落地，见 B-5**）。

> **仍阻塞的只有 Q9 / Q11 / Q12**。Q9 已由 §12.1 B-9 升级为出画阻断，须先做 N0 再谈排期。

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

> ~~Q18 / B-4 是当前唯一需要用户显式拍板的口径冲突；Q9 / Q11 / Q13 / Q14 为阻塞选型问题，均按 `01` §6 默认答案推进并标注「基于默认假设」。~~
> **以上已被 §12.1 取代**（Q18 已关闭、Q13/Q14 已反转），保留原文仅为追溯。

### 12.1 v0.3 质量与传输轮（2026-11-14）

> 起点是用户指令「所有选择计划按最佳质量和传输效果来设计，符合 uniLinker 这个名字」，
> 以及追加要求「支持帧率优先/画质优先/均衡/节能四种绝对模式，除节能外都在优先基础上最大化其余」。
> 本节**修正的是归因，而非结论方向**：原文说「1080p60 大概率不达标」仍然对，但把原因归到编码器是错的。

| 编号 | 原表述 | 修正后 | 证据 | 影响 |
|---|---|---|---|---|
| **B-5** | §6「目标档 1080p60/≤80ms 大概率不达标，硬瓶颈：软编 + readback + 时戳」 | **归因修正**：病根在**采集回读**，不在编码器。`CaptureFrame.D3dTexture` 恒为 `nint.Zero` 使硬编在数据结构层不可达；软编只是「不得不用软路径」的表象。六项全改才可能进 80ms，只换编码器最好情况仍 70–110ms | `WgcCapture.cs:173-192`（`Map` 同步阻塞 + `Marshal.Copy` + 每帧 8.29MB）+ `:187`（`D3dTexture = nint.Zero`） | §6 已重写为 6.1/6.2/6.3；`03` 新增 T05b 零拷贝采集 |
| **B-6** | §1 Q13/Q14 列为「阻塞选型问题，按默认推进」 | **均已拍板**：Q13 硬性（1080p60/≤80ms 为 P0）；Q14 原生 `MirrorView`（非 WebView2） | README §7.5 C-18/C-19 | `01`/`02`/`03`/`05`/`08` 同步反转 |
| **B-7** | 未评估「四种绝对模式」 | **新增 C-24**：模式 = 硬不变量 + 最大化目标；拥塞只能由非优先级维度吸收。四模式 × 三档链路的不可达组合**前置拒绝**，不运行中静默降级 | README §7.6 | `01` §2.2、`06` §7.6、`03` §11 `Mode` 字段 |
| **B-8** | 未涉及产品定位 | **新增 C-22**：`uni-` = 统一（三端一协议 / 四链路一入口 / 一条画质阶梯），`-Linker` = 连接件。术语收敛，「共享」归还文件传输；移除公网 STUN 是该主张的落地而非附带清理 | `web/index.html:6`、`MainWindow.xaml:75`、`WebRTCService.kt:76` | 新增 §1.1 |
| **B-9** | §8 Q9「Android 接收链路很可能根本连不通」 | **升级为出画阻断（B-1~B-4 四条）**：除 Q9 的信令缺陷外，另有首帧 IDR 竞态（`SignalingServer.cs:293-300`）、`SurfaceViewRenderer` 从未 `init(EglBase)`（`ScreenMirrorTab.kt:103-112`）、SDP 缺 `packetization-mode`/`profile-level-id`（`PeerConnection.cs:54`）。**N0 出画必须先于任何质量优化** | 见 README §7.5 C-23 | `03` 新增 T05a（P0 前置） |

**已完成的两项代码级修正（本轮，非文档）**：
1. `PeerMesh.cs` RTP 时基由 `90000/30` 改为按 `EncodedPacket.TimestampUs` 真实增量推进（`4dd2e54`）——
   这是「60fps 选项从假变真」的唯一前提。
2. Android / Web 移除公网 STUN（`da2e114`）。

### 12.2 v0.4 技术路线实证轮（2026-11-14）

> 起点是用户新增约束「做一个通用的，向下兼容支持安卓 8，v7a 32 位设备」，以及对选型②
> 关键路线的实证调研（SIPSorcery 8.0.12 反编译取证 + 本机 MF 探针实测 + AAR 拆包 +
> 官方文档核对）。全部证据与推理见 **`09-technical-route-research.md`**（v1.0，2026-11-14）。
> 本节只登记对既有裁决的修正，不推翻 C-18 的质量方向。

| 编号 | 原表述 | 修正后 | 证据 | 影响 |
|---|---|---|---|---|
| **C-25** | `02` §5 / README §7.5 C-21：「RTCP 反馈**只协商 goog-remb**」 | **不可实现**：SIPSorcery 8.0.12 的 SDP 输出只含 `transport-cc`（remb 行在源码中被注释）。改为「**按需 PLI**（`SendRtcpFeedback` + PSFB/PLI）+ **answer SDP 后处理**（补 `a=rtcp-fb: pli/nack/goog-remb` 行）+ **码率自适应走控制 DataChannel**（RTCP RR 统计 → 控制通道 → 发送端 `RtpSender.setParameters` / 编码器码率）」 | `09` §1.3/§1.4 [S][E] | `02` §5、`03` §2.5 MediaFeedback、`04` §3.x 同步修订 |
| **C-26** | C-18/Q13：1080p60/≤80ms 为 P0 硬指标（全设备） | **维持 P0，适用域收窄**：1080p60/≤80ms 适用于有硬件编码能力的目标组合（Windows→Windows/浏览器）；新增「**v7a/低配 Android 档位 720p30 为该档位的 P0 基线**」——32 位设备内存上限、HW 编码 profile 变异与软渲染路径使 1080p60 在该档位物理不可达。四模式在该档位自动映射到保守预设，不静默降级 | `09` §2/§4.2/§5 + 用户 2026-11-14 追加约束（Android 8 + v7a 通用兼容） | `01` 需求池按设备档位拆分 P0；`06`/`08` 的 U11 模式用例补充 v7a 档位；`03` HardwareEncoder 归入 P1（条件：C-18 目标档位在该设备档位生效时才需要） |
| **C-27** | `04` Android 实现指导未分 API 等级 | **补 API 26/33/34/35 条件分支清单**：通知渠道（26 必须）、`startForegroundService`（26 语义）、`startLocalOnlyHotspot`（26 起步 + 33+ `NEARBY_WIFI_DEVICES` / 26-32 定位权限）、`dispatchGesture`（24+）、FGS `mediaProjection` 类型（34+ 强制）、`POST_NOTIFICATIONS` 运行时权限（33+）、MediaProjection 每次 Session 授权 + Android 15 6h/天 FGS 上限。**minSdk 26 与 v7a 打包已满足**（gradle 现值；AAR 实测 `minSdkVersion="21"` + 含 armeabi-v7a） | `09` §2 [D][E] | `04` §3.3 Android 节重写时按此清单展开；`05` 门禁设备清单至少含一台 API 26-30 的 v7a 设备 |
| —（新风险登记） | Q9 门禁未覆盖「无 HW AVC encoder 机型」 | **Android libwebrtc 的 H264 硬解码以同机存在 H264 硬编码为前提**（webrtc issue 42220261），无软解兜底——此类机型收不了 Windows H264 流。Q9 真机 PoC 必须含该场景；兜底（Windows 发 VP8，需 libvpx）仅登记候选，不进 P0 | `09` §3 [S][D] | `05` M0/G1 门禁清单 +1 场景 |
| —（新实证） | Windows 解码路线未明确（RemoteRenderer Q14 反转后悬空） | **本机（26300 build）MF MFT 解码实测不可用**：`MFTEnumEx(0xFF)` 22 个激活对象 `ActivateObject` 全部 0xC00D36E6、属性全空；64 个注册 CLSID QI IMFTransform 0/64；MSH264DecoderMFT CoCreate 0x80040154。**零售版 Windows 不受影响**。设计为 `IDecoder` 抽象：OpenH264（H264Sharp，仓库已有依赖，android-arm32 .so 亦在包内）全平台保底 + MF 解码器运行时探针通过才注册 | `09` §4.1/§4.2 [E] | `03` RemoteRenderer 落地按此；软解 1080p30 无压力，1080p60 低端 CPU 吃紧（与 C-26 分档一致） |
| —（新实证） | QUIC 列 P1 实验位 | **判出通用传输**：.NET 8+ 捆绑 msquic 依赖 Win11/Server 2022 SChannel，零售 Win10 直接「not supported」。维持 WebRTC 唯一 P0 传输；QUIC 保留实验槽并标注「仅 Win11+ / 需手装 libmsquic」 | `09` §5 [D] | `02` §5 R-QUIC、`03` Transport 节注记 |
| —（实现注意项） | 多显示器同发未评估 | SIPSorcery **单 PC 仅支持单视频流发送**（`SendVideo` → `VideoStreamList[0]`）；多显示器同发需多 PC 或 track 轮换。多观众不受影响（每观众独立 PC） | `09` §1.3/§7 [S] | P1 实现注意项，不进 P0 |

### 12.3 M0/G1 真机 PoC 记录 — 第一轮（2026-11-14/15，信令半程通过）

> 设备：SM-P605（LineageOS 8.1 / API 27 / armeabi-v7a，骁龙 800）。
> 环境：USB（`adb reverse tcp:9527 tcp:9527` 承载信令）、Windows 宿主 3.1.0、
> APK 首次真实构建（minSdk 26 + v7a 确认可编译可运行）。
> 新增调试通道：`am start --es connect "ip:port"` 直接触发接收流程（`09` §2 建议的 PoC 入口）。

**信令半程：通过**。修复后真机日志证据：Android 侧 `PeerConnection created → Sending offer →
Answer received, applying remote description`；Windows 侧 `Offer handled for 'android-…'
capability=screen-capture answerSdpBytes=959`。双向 SDP 交换（offer/answer/fmtp 42e01f echo）落地。

**新发现（修正 B-9 的细节归因）**：Q9 的「offer 从未发出」其机制与静态推断不同——
`sendSdp` 挂在 **createOffer 的 observer** 的 `onSetSuccess` 里，而该回调**只为
setLocalDescription 的 observer 触发**，原代码的 offer 在任何环境下都发不出去（不只是
Android 侧配置问题）。另有三个叠加缺陷（已修）：`PeerConnectionFactory.initialize` 二次调用
抛异常被静默吞掉（ERROR 后永远无法重试）、信令失败不回 ERROR（永远卡 CONNECTING）、
`SurfaceViewRenderer` 未 `init` 即 `addSink`（必崩）。修复提交：Android 侧
`fix(android): make the receive chain actually reach signaling`、Windows 侧 fmtp
`packetization-mode=1;profile-level-id=42e01f`（B-4 的落地）。

**工具链发现**：`adb forward` 与 `adb reverse` 方向易错（设备应用连宿主信令需 **reverse**）；
本机 Gradle 9.0.0 分发包下载常年失败，wrapper 已钉到缓存的 8.9（AGP 8.7.3 兼容）并加
Aliyun 镜像（google/mavenCentral/gradlePluginPortal）。

**媒体半程：阻塞于设备无网络**——平板 Wi-Fi 处于 DISCONNECTED（无 IP），USB 只承载信令，
ICE 无路由（判据 E 的 USB-only 媒体属 M8 范围）。待办：平板加入宿主所在 Wi-Fi（或宿主开启
移动热点），随后复测 ICE → 出画 → M0/G1 结论。
