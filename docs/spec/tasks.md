# M1 原子任务拆解

纪律：每任务 ≤3 文件、可独立验证、带验收命令；完成即勾选并提交对应 commit。
禁止：任务间循环依赖；单任务静默扩大范围；红灯状态叠加修改。

| # | 任务 | 产出 | 验收命令 | 禁止 | 状态 |
|---|------|------|----------|------|------|
| T1 | git 卫生 | index 移除 `android/app/build`；`.gitignore` 去重 | `git ls-files \| grep android/app/build` 为空；Gradle 构建后 `git status` 干净 | 混入其他文件 | ✅ bbf4ef1 |
| T2 | 治理基线 | `AGENTS.md`（≤35 行硬约束）；`CLAUDE.md` → `@AGENTS.md` 导入；详细架构迁入 `docs/architecture.md` | 人工审查规则完整性；无双源漂移 | 把 README 内容整段复制进来 | ✅ 0f5462f |
| T3 | 编辑器一致性 | 根 `.editorconfig`（不强制行尾） | msbuild 仍绿 | 强制 end_of_line 触发全仓 reformat | ✅ c10536d |
| T4 | Spec 与拆解 | `docs/spec/M1-probes.md`；本文件 | 探针实现前两者已入库 | Spec 出现技术实现细节 | ✅ |
| T5 | 测试先行 | `tests/UniLinker.Core.Tests/`（FilterLoopbackCandidates、SignalingMessage/IceCandidateMessage JSON 契约）；Core 加 InternalsVisibleTo；CI 加 `dotnet test` 门禁 | `dotnet test tests/UniLinker.Core.Tests` 绿；推送后 CI 绿 | 跳过 characterization 直接写迁移 | ⬜ |
| T6 | P2 探针 | `tests/UniLinker.Core.Tests/Probes/SipsorcerySrtpProbe.cs` + verdict 落 09 §10 | 探针运行并输出 FEASIBLE / NOT FEASIBLE | 反射调用 internal 成员；改生产代码 | ⬜ |
| T7 | P3 探针 | WinUI.csproj 加 FrameworkReference；`PlatformConfig.ExperimentalKestrel`；App.xaml.cs 最小 Kestrel（9528 `/ulp/info`） | 开关两态 curl 验证；SM01–05 回归绿；CI 绿 | 迁移现有路由；加 tus/WS | ⬜ |
| T8 | 沉淀 | 09 §10 verdict；README 裁决表 C-28/C-29；记忆文件；tasks 勾选 | 文档齐全且与测试输出一致 | 漏记证据链 | ⬜ |

## 依赖顺序

T1 → T2 → T3 → T4 → T5 → T6 → T7 → T8（T6/T7 均依赖 T5 的测试设施；T7 不依赖 T6 结果，可并行）。

## 停止规则

探针判负 → 转媒体路线决策树（P1 WebView 复用浏览器路径）；修复失控 → 回滚上一稳定 commit 重写 Spec。
