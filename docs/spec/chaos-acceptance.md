# Chaos Acceptance — clumsy 混沌注入验收清单（M3/M4 直连档）

手动手册：在 PC 端用 [clumsy](https://jagt.github.io/clumsy/) 对直连 RTP 的
UDP 流量注入故障，验证接收端滑动窗口丢包显示与画面自愈。每次场景跑完
**必须在 clumsy 里点 STOP** 再做下一个。

> **2026-11-19 第一轮结果（receiver 内建注入，adb extras 驱动，
> `--ei chaos_loss/chaos_dup/chaos_lag`）**：
>
> | 场景 | 注入 | HUD 滑窗读数 | 画面 | 结论 |
> |------|------|------------|------|------|
> | C1 | 2% loss | 1.6% ✓ | 花屏数秒后渐次恢复（PLI 出 IDR） | 滑窗/PLI ✓；**P(完整 IDR)=0.98¹⁰⁰≈13%**，恢复中位数 ~2-3s |
> | C2 | 10% loss | 9.6% ✓ | 持续花屏（0.9¹⁰⁰≈0%） | 滑窗精确 ✓；无 RTX 数学上不可恢复 → **M5: NACK/RTX** |
> | C4 | 30% dup | 0.0% ✓ | 流畅 | 重复不再计丢（RFC 3550 delta 处理修复后）✓ |
> | C5 | 100% outage→重连 | — | 重连后 ~3s 恢复出画 | 断流期间滑窗如实 100%，恢复 ✓ |
> | C3 | 500ms lag(=重序) | 94.9% | 全碎（SRTP 抗重放窗口拒收远序包，auth 失败 156 次） | 模拟器 lag 实现为乱序；**真实网络均匀延迟行为不同**——需 clumsy 真网验证；抗重放窗口加宽 → M5 |
>
> 每个场景暴露并修复的真 bug：dup/乱序被朴素模公式算成 65535 包假丢；
> lag 并行投递撕裂 nalBuffer/depacketizer（已串行化）；
> `NvEncEncoder.Dispose` 的 3s 超时 join 导致 use-after-free（coreclr
> 0xc0000005，断连中崩溃）。
>
> **核心架构结论**：无 RTX/NACK 的系统在 ≥2% 随机丢包 + 百包级 IDR 下
> 无法保证 ≤1s 画面恢复——这是真实边界而非 bug。RTX 在 0ms RTT 的
> RNDIS 链路上代价极低，列为 M5 首项。

## 环境准备

1. 启动 uniLinker（WinUI），PC 与平板同一链路（USB 网络共享 / 热点均可）。
2. 悬浮球 → 设置面板，确认 HUD 开启：丢包率、FPS、码率、抖动四项全开。
3. 记录 HUD 基线（无注入）：丢包 ≈0%、FPS ≈目标帧率、抖动 <15ms（有线）。
4. clumsy：以**管理员**运行 → Filter 勾 UDP，`tcpdump` 过滤器填
   `udp and port <安卓端 RTP 端口>`（发送会话建立时 logcat
   `DirectRtpClient` 可见端口，通常 5004）→ 各场景勾选对应功能开关。

> clumsy 是 PC 侧注入，丢的是**发往平板方向**的包 —— 与 HUD 丢包统计
> （接收端 seq-gap 检测）正好对应。

## 场景

### C1 轻损：2% 丢包 + 30ms 抖动（M3 验收基线）

- clumsy：Lag 勾选 +30ms；Drop 勾选，Probability 2%。
- **验收**
  - [ ] HUD 丢包率（滑动窗口 5s）稳定显示 1–3%，随注入停止回落至 ≈0%
  - [ ] 画面无永久花屏：短暂马赛克后 ≤1s 内自行恢复（周期 IDR 或
        关键帧请求兜底）
  - [ ] FPS 回落可感知但不出黑屏/冻结

### C2 突发丢包：10% 丢包持续 10s

- clumsy：Drop 10%，开启 10 秒后 STOP。
- **验收**
  - [ ] HUD 丢包率跳到 ≈10%（证明滑窗是 5s 窗口的**近期值**，不是累计值：
        累计值此时应远低于 10%）
  - [ ] STOP 后 ≤5s（一个窗口）丢包率回落至 ≈0%
  - [ ] 恢复后画面完整（后续 IDR 修复），无残留破损块

### C3 高延迟：500ms lag

- clumsy：Lag +500ms（不丢包）。
- **验收**
  - [ ] 画面延迟明显但**不冻结**（码率不变时 NVENC/OpenH264 均应持续出帧）
  - [ ] 抖动值上升；丢包率不上升（延迟≠丢包，验证滑窗只认 seq-gap）
  - [ ] STOP 后延迟立即恢复基线

### C4 乱序：30ms lag + Duplicate

- clumsy：Lag +30ms、Duplicate 勾选（模拟乱序+重复包）。
- **验收**
  - [ ] 丢包率**不虚高**：重复包不进 seq-gap；乱序轻微时不应计丢
  - [ ] 画面不花屏（depacketizer 对乱序容忍）

### C5 断流恢复：Drop 100% 持续 3s

- clumsy：Drop 100%，3 秒后 STOP。
- **验收**
  - [ ] 断流期间 HUD 丢包率 100%（窗口全丢）
  - [ ] STOP 后 ≤2s 画面恢复（下一个 IDR 到达即重连画面）
  - [ ] 断流期间的丢失计数在窗口滑出后归零，不再拖累显示

## 通过标准

全部场景勾选通过 = 混沌验收 ✅。任一场景出现**永久花屏 / 冻结 /
丢包显示不回落** → 记录场景编号与 HUD 表现，回 app.log + logcat
（`adb logcat -s DirectRtp H264Decoder`）定位，修复后重跑该场景。

## 已知边界（不在本轮验收范围）

- 带宽节流（Throttle）会触发 NVENC CBR 与链路拥塞的复合行为，留到
  自适应码率（后续里程碑）一并验收。
- RTCP/应用层关键帧请求闭环（PLI）尚未上线，C1/C5 的恢复依赖周期 IDR
  （2s GOP）兜底。
