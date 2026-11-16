# M1 规格：轮子适配探针 P2 / P3

日期：2026-11-16 · 状态：待验证 · 验收裁决落点：docs/redesign/09 §10、裁决 C-28/C-29

按工程化 Vibe 纪律：AI 碰探针代码之前，目标 / 边界 / 验收标准已冻结于此。
Spec 只写行为，不写技术实现细节；验收必须是可执行的命令或可观察的结果。

---

## P2 — SIPSorcery SRTP 复用可行性（直连 RTP 档的加密轮子）

**目标**：判定 SIPSorcery 8.0.12 的 SRTP 实现能否脱离 DTLS、由我们自己的密钥材料驱动，从而让"直连 RTP"档零新增加密代码。

**Given-When-Then**：

- Given 已加载 SIPSorcery 8.0.12 程序集（UniLinker.Core 现有依赖，零新包），
  When 反射查找 `SIPSorcery.Net.DtlsSrtp` 类型，
  Then 记录：类型可见性（public/internal）、全部公共构造函数签名、`ProtectRTP`/`UnprotectRTP` 方法签名，并输出到测试日志。
- Given 该类型 public 且存在不依赖 DTLS 握手即可注入密钥的构造路径，
  When 用 RFC 7714 AES-GCM 测试向量构造实例，
  Then `ProtectRTP` 输出与 RFC 7714 期望密文一致，`UnprotectRTP` 能还原原文。
- Given 该类型 internal 或构造函数强绑定 DTLS 传输对象，
  When 探针运行结束，
  Then 判定 NOT FEASIBLE，测试显式 skip 并输出原因；不得用反射 hack 强行绕过（后续走决策树 P6）。

**边界（不做）**：不改任何生产代码；不引入新 NuGet 包；不实现直连 RTP 数据通路；不用反射访问 internal 成员（只读元数据，不调用）。

**验收**：`dotnet test tests/UniLinker.Core.Tests` 全绿（探针测试本身运行且给出 FEASIBLE / NOT FEASIBLE 结论）；verdict 与证据写入 docs/redesign/09 §10。

---

## P3 — Kestrel 寄宿 WinUI 宿主（服务器迁移的前置）

**目标**：判定 ASP.NET Core Kestrel 能否在 WinUI 3 unpackaged 进程内启动并服务请求，为后续 SignalingServer→Kestrel 迁移（tus / WebSocket / HTTPS 的载体）提供依据。

**Given-When-Then**：

- Given settings 中 `experimentalKestrel=true`（默认 false），
  When 应用启动完成，
  Then `GET http://127.0.0.1:9528/ulp/info` 返回 200，JSON 含 `name`、`version`、`port` 字段。
- Given `experimentalKestrel=false`（默认），
  When 应用启动，
  Then 9528 无监听；9527 信令服务行为与现状完全一致。
- Given 探针开启状态下运行 E2E 回归，
  When 跑 SM01–SM05，
  Then 全部通过（现有 9527 路径零回归）。

**边界（不做）**：不迁移 `/signaling`、`/ice` 等现有路由到 Kestrel；不加 tus / WebSocket / TLS；不加新 UI；不引入配置界面项（config.json 手工编辑即可）。

**验收**：msbuild 构建 CI 绿；开关两态手工 curl 验证；`dotnet test tests/UniLinker.E2E.Tests --filter "Category=ScreenMirror"` 绿；verdict 写入 docs/redesign/09 §10。

---

## 停止规则

任一探针判负：不开始任何迁移/协议实现工作，按媒体路线决策树转向（P1 WebView 复用浏览器路径为下一探针）。连续修复失控：回滚到上一稳定 commit，重写 Spec。
