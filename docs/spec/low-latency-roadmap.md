# 低延迟技术路线调研（M7 前置研究）

日期：2026-11-20。目标："几乎同步"的投屏——帧率稳定、延迟接近本地显示。
方法：以本轮实测数据做延迟分解，对标业界实现（Moonlight/Sunshine、Parsec、VNC/RDP 族），
逐项给出根因、方案、门禁与预期收益。来源见文末。

## 1. 现状实测（本仓库，2026-11-19/20 设备日志）

### 1.1 各模式端到端体感（PC 动 → 平板显示）

| 模式 | 介质 | 帧率(实测) | 端到端延迟(估) | 主导因素 |
|------|------|-----------|---------------|---------|
| SIPSorcery (H264) | USB RNDIS | 30-51fps | ~120-160ms | MediaCodec 解码管线 (~100ms) |
| SIPSorcery (H264) | WiFi 热点 | 22-27fps | ~250-350ms | **WiFi 省电 RTT ~130ms** + 解码 |
| Native RTX (H264+RTX) | USB | 稳态钉死 ⬜ | — | M5-T3 待查 |
| Tile (JPEG 分块) | USB RNDIS | 24-40fps | ~35-50ms | tile diff 20ms + 渲染 |
| Tile (JPEG 分块) | WiFi 热点 | 20-25fps | ~165-180ms | **WiFi 省电 RTT ~130ms** |

### 1.2 分段实测（发送端，[NvEnc]/[Tile] 日志）

- 采集（WGC 轮询 + GPU 降采样 + 回读）：**1-8ms**
- H264 编码（NVENC p4, vbr+cq16）：sws 1.3-2.1ms + 编码 1.9-3.8ms
- Tile：sws 1.4-1.7ms + **FNV 哈希 diff 19.7-20.7ms（4MB 逐字节，最大单项）** + mjpeg 0.2-1.2ms
- Tile 平板解码：BitmapFactory 1.4-2.3ms/tile（Gate A：≤15ms ✓ JPEG 定案）
- WiFi RTT（平板省电模式）：**PC↔平板 ping ~128ms**（首包 181ms）
- USB RTT：~0ms

### 1.3 结论要点

- **USB 直连下 Tile 模式已接近目标**（~35-50ms）——VNC/PCoIP 族的正常水位
- **WiFi 的最大延迟项不是编码也不是解码，是平板 WiFi 省电模式的 ~130ms RTT**——
  这是可关的（WifiLock），一行代码量级，收益 ~120ms
- H264 模式的地板是那颗 2013 年 MSM8974 的 MediaCodec 解码管线（~100ms，API 27
  无标准低延迟键）——Tile 模式用 CPU 解码（2-20ms）绕开了它，这正是两条路线
  延迟差 3-4 倍的根源
- Tile diff 的 20ms 是自伤（逐字节 FNV），向量化后应 <1ms

## 2. 业界参照

### 2.1 Moonlight/Sunshine（游戏串行，H264/HEVC/AV1 流）

- 分解法标准动作：Moonlight 内置统计浮层（Ctrl+Alt+Shift+S）把**编码/网络/解码**
  三段分开显示——先定位再调参（LizardByte 官方建议）
- 编码侧：NVENC + 低延迟参数（无 B 帧、无 lookahead、UV 帧内刷新）；QP 只在
  网络/解码受限时调
- 解码侧：Moonlight-android 的
  [MediaCodecHelper.java](https://github.com/moonlight-stream/moonlight-android/blob/master/app/src/main/java/com/limelight/binding/video/MediaCodecHelper.java)
  是**低延迟键探测链的工业实现**：Android 11+ 的 `KEY_LOW_LATENCY`、Qualcomm
  `vendor.qti-ext-dec-low-latency.enable`、 MediaTek 变体、按设备名的探测链
- 社区解码延迟参考：1440p60 ~7-8ms、4K ~13ms（现代设备）；老设备（Amlogic
  S912）曾出现 ~120ms 的解码病（与我们 MSM8974 的量级一致——老 Venus 管线
  是已知坑）
- 维护者确认：Moonlight 已启用所有已知 Qualcomm 低延迟键，"这就是这些设备
  能做到的极限"（issue #1120）

### 2.2 远程桌面族（我们 Tile 模式的同族）

- VNC/RFB（1998-）、RDP、PCoIP（2007-）、SPICE、DisplayLink：tile/脏矩形 +
  每块独立编码，10-50ms 级端到端，丢包不级联
- 我们的杂交点：族内传统跑 TCP（可靠但队头阻塞），我们把 tile 塞进自研
  UDP + SRTP + NACK/RTX 环形缓冲——0ms RTT 的 USB 上按 tile 重传快于 TCP ✓
- DisplayLink 的分块混合压缩（UI 无损 + 图像有损）= 我们 Gate A 之后 LZ4/JPEG
  混合的同型方案

### 2.3 采集 API（DXGI DDA vs WGC）

- 两者同为合成器取帧，帧间隔随显示器刷新（60Hz ≈ 16.7ms/帧），单帧差异
  <1 个刷新周期
- DXGI DDA 同 GPU 场景略精简；WGC 的价值在跨 GPU/单窗口隔离
- 已知坑：DDA 的 AcquireNextFrame 等待方式决定帧率下限（sleep 轮询劣化）；
  WGC 在 Win11 24H2 有 60fps 帧率钳制报告；WGC 窗口捕获有帧间距抖动报告
  （OBS #13893）
- 我们实测 WGC 回读 1-8ms——**采集不是当前瓶颈**，DDA 替换收益 5-10ms 级，
  列为低优先

### 2.4 JPEG 硬件编码（nvJPEG）

- NVIDIA 数据：A100 上 4K JPEG 编码 >1000fps（<1ms/帧）；第三方 Fastvideo
  在 RTX 4090 上比 nvJPEG 快 30-39%（2K 3632fps）
- RTX 5070 Ti 上的 nvJPEG 走 CUDA kernel（非专用 NVJPG 引擎）——预计 1-4ms
  级，与 CPU FFmpeg mjpeg 的 0.2-1.2ms/tile 相比**无优势**（tile 后单 tile 更小）。
  结论：**mjpeg CPU 已够，nvJPEG 不立项**

## 3. 根因清单（按收益排序）

| # | 根因 | 影响量级 | 方案 | 工作量 |
|---|------|---------|------|--------|
| R1 | 平板 WiFi 省电 RTT ~130ms | WiFi 全模式 +120ms | WifiManager.WIFI_MODE_FULL_HIGH_PERF 锁（STREAMING 期间持锁） | 极小 |
| R2 | Tile diff 逐字节 FNV 20ms | Tile 模式 +20ms/帧 | memcmp 向量化 P/Invoke 或 8 字节步进 + 早退；或 GPU 上 diff | 小 |
| R3 | MediaCodec 解码 ~100ms 地板 | H264 全模式 +80ms | 低延迟键探测链（移植 Moonlight MediaCodecHelper 探测序：KEY_LOW_LATENCY → vendor.qti-ext-dec-low-latency.enable → 参数级回退）；**API 27 + MSM8974 大概率不支持 → 门禁探测决定** | 中 |
| R4 | 重序窗口固定 250ms | WiFi 丢包时 +250ms | 自适应：按实测 NACK→回填耗时缩放（USB ~30ms / WiFi ~200ms） | 小 |
| R5 | Tile 60fps 未启用 | Tile 模式 30→60fps | tier_fps=1 时 TileEncoder/Renderer 全链路 60fps（解码 1.4ms×60 可承受） | 小 |
| R6 | WGC→DDA | 采集 5-10ms | AcquireNextFrame 事件驱动 | 中（收益低，延后） |
| R7 | Pacer 批发抖动 | 偶发 10-40ms | 已做（令牌桶 + 帧级队列）；WiFi 高 RTT 下影响小 | — |

## 4. 路线图（分期 + 门禁）

### 第 1 期（立即，一次提交量级）
1. **R1 WiFi 低延迟锁**：STREAMING 期间 `createWifiLock(WIFI_MODE_FULL_HIGH_PERF)`，
   断开释放。验收门禁：流中 ping RTT ≤20ms（前值 ~128ms）——**达标后 WiFi 总延迟
   预计从 ~250ms 降到 ~130ms 以内**
2. **R2 diff 向量化**：`memcmp` P/Invoke 或 8 字节步进，4MB diff 20ms→≤2ms。
   验收：diffAvg ≤3ms
3. **R4 自适应重序窗口**：按链接类型（USB 30ms / WiFi 150-250ms）——已有 250ms
   固定值，加 USB/WiFi 区分即可

### 第 2 期（Tile 60fps + 探针）
4. **R5 Tile 60fps**：tier_fps=1 全链路验证（TileEncoder 60fps 编码 + 平板
   1.4ms×60 解码承受力实测）
5. **R3 探针**：MediaCodec 低延迟键探测（移植 Moonlight 探测序为
   `MediaCodecProbe`，API 27 上探测 MSM8974 是否响应 vendor 键）——门禁：
   若响应 → H264 模式解码地板下探；若不响应 → **Tile 模式是该设备的唯一
   低延迟路线**（已建成），H264 保持"稳定高画质档"定位

### 第 3 期（按需）
6. R6 DDA 采集、LZ4 无损混合（DisplayLink 型）、音频通道

## 5. 预期终态

| 链路 | 模式 | 端到端延迟 | 帧率 |
|------|------|-----------|------|
| USB | Tile | **~15-35ms**（diff 优化后） | 30-60fps |
| WiFi（省电解） | Tile | **~40-70ms** | 30-60fps |
| WiFi（现状省电） | 任意 | 165-350ms | 20-25fps |
| USB | SIPSorcery | ~120-160ms（解码地板） | 30fps |

"几乎同步"的判定：**输入回显（平板打字/触摸 → PC 屏幕 → 投回平板）** ≤ 一帧 +
链路 RTT。Tile 模式在 USB 上已具备该条件；WiFi 上取决于 R1。

## 来源

- [Moonlight/Sunshine 优化与常见问题 (Reddit)](https://www.reddit.com/r/MoonlightStreaming/comments/1d2ihd8/moonlightsunshine_optimisation_and_common_issues)
- [LizardByte 讨论：延迟分解与统计浮层](https://github.com/orgs/LizardByte/discussions/17)
- [Sunshine Advanced Usage（编码参数/QP）](https://docs.lizardbyte.dev/projects/sunshine/latest/advanced_usage.html)
- [Moonlight-android MediaCodecHelper.java（低延迟键探测链）](https://github.com/moonlight-stream/moonlight-android/blob/master/app/src/main/java/com/limelight/binding/video/MediaCodecHelper.java)
- [Moonlight issue #1120（解码延迟极限）](https://github.com/moonlight-stream/moonlight-android/issues/1120)
- [Qualcomm 博客：MediaCodec vendor extensions](https://www.qualcomm.com/developer/blog/2023/07/building-media-rich-android-apps-mediacodec-and-vendor-extensions)
- [AOSP：低延迟解码（KEY_LOW_LATENCY, Android 11+）](https://source.android.com/docs/core/media/low-latency-media)
- [AOSP：Wi-Fi low-latency mode](https://source.android.com/docs/core/connect/wifi-low-latency)
- [Android SE：WiFi 省电模式](https://android.stackexchange.com/questions/18713/turn-off-wi-fi-power-saving)
- [nvJPEG 4K 编码性能（A100 >1000fps，NVIDIA 论坛）](https://forums.developer.nvidia.com/t/nvjpegs-jpeg-encode-performance-on-ampere-a100/202349)
- [Fastvideo JPEG 基准（RTX 4090）](https://github.com/fastvideo/jpeg-benchmark)
- [SO：Desktop Duplication vs Windows.Graphics.Capture](https://stackoverflow.com/questions/74084077/desktop-duplication-api-vs-windows-graphics-capture)
- [OBS #13893：WGC 帧间距抖动](https://github.com/obsproject/obs-studio/issues/13893)
