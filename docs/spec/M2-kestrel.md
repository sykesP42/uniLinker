# M2 规格：SignalingServer → Kestrel 全量迁移（strangler 切换）

日期：2026-11-17 · 状态：待验证 · 前置：C-29（P3 探针 FEASIBLE）已归档

## 目标

把手写 TcpListener HTTP/1.1 服务（`SignalingServer.cs`，641 行）整体替换为
ASP.NET Core Kestrel minimal API：同端口 9527、IPv6 双栈绑定、JSON 契约
逐字节兼容。信令业务逻辑下沉为传输无关的 `SignalingService`（Core 工程，
零 ASP.NET 依赖），为 M3 直连 RTP 档与 Android 侧未来复用铺路。

## 契约基线（旧服务实测行为，迁移必须保持）

| 路由 | 行为 |
|---|---|
| `GET /info` | 200 JSON `{name, version, capabilities, status:"available"}` |
| `GET /api/status` | 200 JSON `{deviceName, version, port, peers, status:"running"}` |
| `POST /signaling` | camelCase 反序列化（JsonException→null）；`msg==null \|\| Sdp==空` → **400 text** `"Invalid signaling message"`；`type=="offer"` → 建 PC/注册 mesh/等 gathering 3s → 200 JSON answer（SignalingMessage camelCase）；`type=="answer"` → `{ok:true}`；其他 type → **400 text** `"Unknown message type"`；PC init 失败 → **500 text** `"WebRTC init failed"` |
| `POST /ice` | 反序列化（camelCase）；写入 pending PC；200 `{ok:true}`；无 PC 时静默忽略 |
| `OPTIONS *` | 204 空 + CORS 头 |
| `GET /*` | 静态文件（`web/` 目录）；非 GET 未知路径 → 404 |
| CORS | 所有响应带 `Access-Control-Allow-Origin: *` |

**死路由处理**：`GET /ice-pending` 全仓无客户端引用（浏览器与 Android 均
不调用，仅服务端自引用）→ **移除**，不留兼容层。

**探针代码处理**：`PlatformConfig.ExperimentalKestrel` 与 9528 `/ulp/info`
探针删除（verdict 已归档 C-29，回归门禁由本里程碑的测试接棒）。

## Given-When-Then 验收

- Given 旧契约测试（SignalingContractTests）与 E2E（SM01–05、FT01–03），
  When 迁移后的服务跑全部测试，
  Then 全绿——JSON 形状、状态码、错误文案逐字节一致。
- Given 应用启动，
  When `netstat -ano | findstr 9527`,
  Then Kestrel 监听 `[::]:9527`（IPv6 双栈，外部可达性与旧服务一致）。
- Given 迁移后的服务,
  When `curl -X OPTIONS`、`curl /info`、`curl /api/status`、`curl /`（静态）,
  Then 状态码与响应体同契约基线表。
- Given 传入畸形 JSON 或空 Sdp 的 /signaling 请求,
  Then 400 + `"Invalid signaling message"`（与旧服务一致）。
- Given `GET /ice-pending`,
  Then 404（移除生效）。

## 边界（不做）

- 不加 tus / WebSocket / TLS / 认证 / 新路由（M3+）
- 不改 PeerConnection / PeerMesh / 插件
- 不做性能优化（Kestrel 替换本身不是性能里程碑）
- 不动 Android / web 前端一行代码

## 停止规则

E2E 任一红灯且两轮修复未收敛 → 回滚到迁移前 commit，改走
「双服务并存灰度」方案（Kestrel 只接新路由），重写 Spec。
