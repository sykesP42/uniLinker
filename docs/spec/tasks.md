# M1 原子任务拆解

纪律：每任务 ≤3 文件、可独立验证、带验收命令；完成即勾选并提交对应 commit。
禁止：任务间循环依赖；单任务静默扩大范围；红灯状态叠加修改。

| # | 任务 | 产出 | 验收命令 | 禁止 | 状态 |
|---|------|------|----------|------|------|
| T1 | git 卫生 | index 移除 `android/app/build`；`.gitignore` 去重 | `git ls-files \| grep android/app/build` 为空；Gradle 构建后 `git status` 干净 | 混入其他文件 | ✅ bbf4ef1 |
| T2 | 治理基线 | `AGENTS.md`（≤35 行硬约束）；`CLAUDE.md` → `@AGENTS.md` 导入；详细架构迁入 `docs/architecture.md` | 人工审查规则完整性；无双源漂移 | 把 README 内容整段复制进来 | ✅ 0f5462f |
| T3 | 编辑器一致性 | 根 `.editorconfig`（不强制行尾） | msbuild 仍绿 | 强制 end_of_line 触发全仓 reformat | ✅ c10536d |
| T4 | Spec 与拆解 | `docs/spec/M1-probes.md`；本文件 | 探针实现前两者已入库 | Spec 出现技术实现细节 | ✅ |
| T5 | 测试先行 | `tests/UniLinker.Core.Tests/`（FilterLoopbackCandidates、SignalingMessage/IceCandidateMessage JSON 契约）；Core 加 InternalsVisibleTo；CI 加 `dotnet test` 门禁 | 13/13 绿（43d120e）；推送后 CI 绿 | 跳过 characterization 直接写迁移 | ✅ 43d120e |
| T6 | P2 探针 | `tests/UniLinker.Core.Tests/Probes/SipsorcerySrtpProbe.cs` + verdict 落 09 §10 | **FEASIBLE**：外部密钥 SRTP 往返验证通过（45e6f11） | 反射调用 internal 成员；改生产代码 | ✅ 45e6f11 |
| T7 | P3 探针 | WinUI.csproj 加 FrameworkReference；`PlatformConfig.ExperimentalKestrel`；App.xaml.cs 最小 Kestrel（9528 `/ulp/info`） | 两态验证通过：ON=200/9527 不变，OFF=无监听；单测 15/15、SM01–05 5/5 绿 | 迁移现有路由；加 tus/WS | ✅ ff60043 |
| T8 | 沉淀 | 09 §10 verdict；`00` §12.6 裁决 C-28/C-29；记忆文件；tasks 勾选 | 文档齐全且与测试输出一致 | 漏记证据链 | ✅ 9f2cd48 |

## M2：SignalingServer → Kestrel 全量迁移（spec：M2-kestrel.md）

| # | 任务 | 产出 | 验收命令 | 禁止 | 状态 |
|---|------|------|----------|------|------|
| M2-T1 | 证据与规格 | `SipsorceryRtpSessionProbe`（RTPSession/SDES API 证据）；`M2-kestrel.md`；`M3-direct-rtp.md` | 探针绿且 spec-critical facts 落文档 | spec 含未证实 API 假设 | ✅ |
| M2-T2 | 抽 SignalingService | `src/UniLinker.Core/SignalingService.cs`（传输无关逻辑）+ `SignalingServiceTests`（test-first：先红后绿）；FilterLoopbackCandidates 迁移 | 6/6 绿，全套 22/22（b39da42） | Service 引 ASP.NET 类型 | ✅ b39da42 |
| M2-T3 | Kestrel 宿主 + 切换 | `SignalingKestrelHost`（9527 双栈、CORS、静态、契约路由）；App.xaml.cs 切换；删 SignalingServer.cs、/ice-pending、ExperimentalKestrel 探针 | msbuild 绿 | 留双服务并存；改 JSON 契约 | ✅ 392cff3 |
| M2-T4 | 回归验证 | 全量验证记录 | 单测 22/22；E2E SM01–05 5/5 + FT01–03 3/3；curl 契约基线逐项过（OPTIONS 204/静态 200/400 文案逐字节/ice-pending 404/netstat 双栈）；中途抓到 2 个真坑（OPTIONS 路由候选 405、UseWebRoot NotSupportedException）已修并沉淀 | 红灯合并 | ✅ |
| M2-T5 | 沉淀 | architecture.md 更新；tasks 勾选；记忆 | 文档与实际一致 | 漏记 | ✅ |

## M3：直连 RTP 档（spec：M3-direct-rtp.md）

| # | 任务 | 产出 | 验收 | 状态 |
|---|------|------|------|------|
| M3-T1/T2 | Windows 发送链 | DirectRtpChannel（RTPSession SDES）+ 控制面 type=rtp + 跨语言 fixture | C# 环回往返 + 互操作诊断绿（1af41a5） | ✅ |
| M3 互操作诊断 | outbound key 根因 | SIPSorcery 用 OFFER 里宣布的 key 保护出向包（非外部注入 key）→ CryptoLine 改从 offer 提取；C# 自证往返门禁 | SenderOutput_unprotects_with_own_crypto_line 绿（cd091e4） | ✅ |
| M3-T3 | Android SRTP/FU-A | jitsi-srtp 包装 + RFC 6184 depacketizer + JVM fixture 测试 | 跨实现闭环：Windows 密文 → JVM 解保 → 重组逐字节还原（cd091e4） | ✅ |
| M3-T4 | Android 接收引擎 | DirectRtpReceiver（UDP loop）+ DirectRtpClient（控制面）+ H264SurfaceDecoder（MediaCodec）+ DirectRtpActivity（deep link `--es connect`） | **设备验收通过（eea1b5d）**：真机 720p 出画、599+ 帧解码、画面随桌面变化；期间修复 7 个真 bug（SunJCE 别名、jitsi-srtp 1.0-4 钉版、旧通道清理、周期 IDR、4MB 收包缓冲、SurfaceView Z 序、deep-link 自动连接）。clumsy 混沌注入见 [chaos-acceptance.md](chaos-acceptance.md)（手动） | ✅ 设备 |
| M3-T5 | 沉淀 | architecture.md / tasks / 记忆 / push | CI 绿 | ✅ |

**互操作关键发现（沉淀）**：① SIPSorcery 出向保护 key 来自其 OFFER 宣布（外部注入 key 无效——诊断测试实证）；② SRTP payload 全密文，长度证据（每包 +10B tag）是判定保护是否生效的唯一可靠信号；③ RTCP SR 周期 ~5s，短窗口抓包别假设必现。

## M4：体验与编码升级（HUD / 滑窗 / NVENC / 配对码）

| # | 任务 | 产出 | 验收 | 状态 |
|---|------|------|------|------|
| M4-T1 | Android UX 重构 | 全屏沉浸 + TextureView + 悬浮球（拖拽/半隐/展开设置面板）+ 深链自动连接 | 设备验收：进入即全屏、悬浮球可拖可隐、面板档位切换即时生效 | ✅ 设备 |
| M4-T2 | HUD 仪表盘 | 可拖动 HUD、逐项开关（fps/码率/丢包/抖动/解码）、tnum 定宽防抖、数值区间分色 | 设备验收：数字不抖动、颜色随数值区间变化 | ✅ 设备 |
| M4-T3 | 丢包滑窗 | DirectRtpReceiver 5s 滑动窗口（seq-gap 计丢）替换累计值；RFC-3550 抖动 | 注入 10% 丢包时 HUD 显示 ≈10% 并随注入停止回落（见 chaos-acceptance.md C2） | ✅ 代码 / 混沌手动 |
| M4-T4 | SAS 配对上端 | PC 端 /ulp/pairing 显示配对码；Android probePairing 全屏比对对话框 | 设备验收：两端同屏六位码一致 | ✅ 设备 |
| M4-T5 | NVENC 硬编 | FFmpeg.AutoGen h264_nvenc 封装 NvEncEncoder : IEncoder；encoder=auto \| nvenc \| openh264，同步 open 失败自动回退 CPU；third_party DLL 构建期复制 | 单测绿（33/33）；探针 RTX 5070 Ti 出包（2 pkts/33718B）；commit 37cc9c0 | ✅ |
| M4-T6 | clumsy 混沌验收 | [chaos-acceptance.md](chaos-acceptance.md) C1–C5 场景清单；**第一轮（receiver 内建注入，6402cd7）已跑**：滑窗精度 ✓（2%→1.6%、10%→9.6%）、C4 重复 0.0% ✓、C5 重连恢复 ~3s ✓；核心发现：≥2% 随机丢包无 RTX 不可保证 ≤1s 恢复（P(完整 IDR)=0.98¹⁰⁰≈13%）→ M5 首项 NACK/RTX；真实网络 clumsy 注入待手动（需 UAC） | 注入式验收 ✓ / clumsy 手动 ⬜ |
| M4-T7 | 沉淀 | architecture.md M4 段 / 记忆 / push | CI 绿（6402cd7） | ✅ |

## M5：RTX 重传 + 传输方式选择（进行中）

| # | 任务 | 产出 | 验收 | 状态 |
|---|------|------|------|------|
| M5-T1 | RTX 自有管线 | DirectRtpSender（RFC 6184 打包 + 裸 SRTP + 4096 环形缓冲 + NACK 循环）；接收端重序缓冲（30ms 洞等待 + skip+PLI）；wire 契约不变，fixture 重生成 | C# 35/35；JVM 3/3；注入 2% 丢包画面全净 30fps | ✅ 代码 |
| M5-T2 | 传输方式选择 | 设置页"投屏传输方式"（以核心技术栈命名）：**SIPSorcery**（默认，RTPSession 会话管线，稳定 44-51fps/24-26Mbps）/ **Native RTX**（自研管线 + NACK 重传，实验）；DirectRtpTransport.Mode 运行时切换 | 双模式真机验收 ✓；默认 legacy | ✅ |
| M5-T3 | RTX 稳态卡死根因 | **待查**：稳态运行一段时间后 expectedSeq 钉死（assembled 停止、HUD 0-2fps、丢包 100%），包持续到达但不投递；已修 4 个相关 bug（CONNRESET 杀循环/baseline 不推进/pacer 3 处公式错/dispose UAF）仍复现；下一步：部署 reorder 状态诊断（expectedSeq/pending/lastSeq 日志已就位） | RTX 模式稳态 ≥10 分钟不掉帧 | ⬜ |
| M5-T4 | HUD 真实化 | fps 改用解码器 renderedFrames（此前统计 AU 提交数——坏帧也计数，撒谎）；断流归零；损坏 AU 丢弃（skip 后半帧不再喂解码器） | 断流显示 0fps ✓；花屏改为干净冻结 | ✅ |

**用户可见行为（2026-11-19）**：默认 SIPSorcery（上一版体验，稳定 44-51fps）；Native RTX 在设置页可选——2% 注入丢包下恢复极快，但存在 M5-T3 稳态钉死问题，修复后转正。

**M5-T5 画质/码率重构（nvenc rc=vbr+cq，OBS 同款质量目标模式）**：CBR 实测把静态桌面 P 帧填充到 ~130KB（16-64Mbps）——平坦区域白白吸收码率预算。改 rc=vbr + cq=19（质量目标 + maxrate 封顶）：静态桌面 1.6-2.8Mbps（降 10-20 倍）且近无损（文字透明），运动 30fps 仅 3.3Mbps，encAvg 不变。悬浮球面板同步重构：分区布局（画质预设/高级档位/传输方式/HUD/连接）、高对比芯片选择器、传输方式远程切换（/ulp/transport POST 持久化 + 自动重连生效）。遗留：面板传输芯片的自动化点击验证未完成（手动即可），HEVC 探针（v7a 解码能力）列为后续压缩升级候选。

## 依赖顺序

M1 完成 → M2-T1 → M2-T2 → M2-T3 → M2-T4 → M2-T5 → M3。
（M2-T2 先行使逻辑与传输解耦，M2-T3 的宿主只做映射；两者可交叉验证但提交按序。）

## 停止规则

探针判负 → 转媒体路线决策树；迁移 E2E 红灯两轮不收敛 → 回滚，改双服务灰度重写 Spec；
M3 中 SRTP 向量失败 → 停，不得带病上线。
