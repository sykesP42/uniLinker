# AGENTS.md — Coding Agent 项目手册

技术栈：Windows 端 C# .NET 9 + WinUI 3（unpackaged）；Android 端 Kotlin minSdk 26（向下兼容 armeabi-v7a）；`web/` 浏览器端原生 JS。
详细架构与实现坑位见 [docs/architecture.md](docs/architecture.md)；重设计与裁决（C-1~C-27）见 docs/redesign/。

## Hard Rules（MUST NEVER）

- NEVER 直接 commit 到 `main`：一律 `dev/<feature>` 或 `fix/<bug>` 分支。
- NEVER 提交非英文或非 Conventional Commits 格式的提交信息；NEVER 添加 `Co-Authored-By: Claude`。
- NEVER 提交构建产物、`.env`、`*.key`、`local.properties`（`android/app/build`、`bin/`、`obj/` 均已 ignore，勿反向添加）。
- NEVER 修改 TFM 钉值 `net9.0-windows10.0.22621.0` + `WindowsSdkPackageVersion 10.0.22621.38`——升到 26100 启动即崩（XamlTypeInfo）；插件与宿主的 WinRT.Runtime 版本 MUST 保持一致，否则插件程序集加载失败。
- NEVER 在未获用户确认时改动防火墙、杀软或系统网络设置（火绒 HIPS 事故先例）。
- NEVER 变更 `/signaling` 或 `/ice` 的 JSON 契约而不先更新 `tests/UniLinker.Core.Tests` 中的 characterization 测试（Android/浏览器线上契约）。
- NEVER 用 `dotnet build` 构建解决方案（缺 AppxPackage PRI 任务必败）；NEVER 在应用实例运行时构建（MSB3021/3027 DLL 锁，先 taskkill）。
- NEVER 在变更依赖或 FrameworkReference 后不带 `-restore` 构建——否则 runtimeconfig 缺失 framework 条目、本地 DLL 又被剔除，启动即崩（P3 探针实证）。
- NEVER 复活 Media Foundation SinkWriter 编码器（`MFCreateMemoryByteStream` 导出不存在，从未能初始化）。

## MUST ALWAYS

- 宣称"完成"前先跑下方验收命令并如实贴出结果；测试红了就停，不在红灯上叠加修改。
- 每个原子任务一个 commit；单次变更 diff 超过 ~400 行时先拆分。

## 验收命令

```bash
# 构建（VS2022 MSBuild；dotnet build 必败）
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" UniLinker.sln -p:Configuration=Debug

# 单元测试（CI 门禁，无 GUI 依赖）
dotnet test tests/UniLinker.Core.Tests

# E2E 回归（本机，fixture 启动真实应用；Playwright 必须用 Edge/Chrome 通道——bundled Chromium 无 H264）
dotnet test tests/UniLinker.E2E.Tests --filter "Category=ScreenMirror"
```
