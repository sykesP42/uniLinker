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
| M2-T2 | 抽 SignalingService | `src/UniLinker.Core/SignalingService.cs`（传输无关逻辑）+ `SignalingServiceTests`（test-first：先红后绿）；FilterLoopbackCandidates 迁移 | `dotnet test` 全绿（先红证明测试有效） | Service 引 ASP.NET 类型 | ⬜ |
| M2-T3 | Kestrel 宿主 + 切换 | `SignalingKestrelHost`（9527 双栈、CORS、静态、契约路由）；App.xaml.cs 切换；删 SignalingServer.cs、/ice-pending、ExperimentalKestrel 探针 | msbuild 绿 | 留双服务并存；改 JSON 契约 | ⬜ |
| M2-T4 | 回归验证 | 全量验证记录 | 单测全绿；E2E SM01–05 + FT01–03 绿；curl 契约基线逐项过（含 OPTIONS/静态/400 文案/netstat 双栈） | 红灯合并 | ⬜ |
| M2-T5 | 沉淀 | architecture.md 更新；tasks 勾选；记忆 | 文档与实际一致 | 漏记 | ⬜ |

## M3：直连 RTP 档（spec：M3-direct-rtp.md，M2 后启动）

Windows 侧 RTPSession+SDPSDES 发送链（证据已由 M2-T1 探针锁定）+ Android
侧 SRTP/depacketizer/MediaCodec 接收链（RFC 向量单测先行）。验收：平板
≥10s 稳定出画 + clumsy 2% 丢包自愈。

## 依赖顺序

M1 完成 → M2-T1 → M2-T2 → M2-T3 → M2-T4 → M2-T5 → M3。
（M2-T2 先行使逻辑与传输解耦，M2-T3 的宿主只做映射；两者可交叉验证但提交按序。）

## 停止规则

探针判负 → 转媒体路线决策树；迁移 E2E 红灯两轮不收敛 → 回滚，改双服务灰度重写 Spec；
M3 中 SRTP 向量失败 → 停，不得带病上线。
