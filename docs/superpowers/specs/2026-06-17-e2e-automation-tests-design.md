# UniLinker E2E 自动化测试设计

## 概述

为 UniLinker Windows 端实现端到端自动化测试，覆盖 ScreenMirror（屏幕投屏）和 FileTransfer（文件传输）两个核心功能。测试使用 Playwright 驱动浏览器，验证完整用户流程。

## 测试范围

| 功能 | 测试场景 | 验证点 |
|------|----------|--------|
| ScreenMirror | Windows 启动 → 浏览器访问 → 观看投屏 | `<video>` 元素播放 |
| FileTransfer | Windows 启动 → 浏览器访问 → 文件上传/下载 | 文件内容正确 |

## 技术选型

| 组件 | 选择 | 理由 |
|------|------|------|
| 测试框架 | xUnit | .NET 生态主流，VS 集成良好 |
| 浏览器驱动 | Playwright | 跨浏览器、自动等待、API 简洁 |
| 断言库 | FluentAssertions | 可读性强，错误信息清晰 |
| 应用启动 | Process.Start | 后台启动 WinUI exe |

## 项目结构

```
tests/
└── UniLinker.E2E.Tests/
    ├── UniLinker.E2E.Tests.csproj
    ├── Fixtures/
    │   └── PlaywrightFixture.cs      # 浏览器上下文管理
    ├── Infrastructure/
    │   ├── TestHost.cs               # 启动/停止应用
    │   └── AppSettings.cs            # 端口、超时配置
    ├── Tests/
    │   ├── ScreenMirrorTests.cs      # 投屏功能测试
    │   └── FileTransferTests.cs      # 文件传输测试
    └── TestData/
        └── sample.txt                # 测试用的上传文件
```

## 测试流程

### TestHost 生命周期

```
[BeforeTest]  → 启动 UniLinker.WinUI.exe
               ↓
               等待端口就绪 (GET /info 返回 200)
               ↓
[RunTest]     → 执行测试逻辑
               ↓
[AfterTest]   → 终止应用进程
```

### ScreenMirror 测试用例

| 用例 | 步骤 | 预期结果 |
|------|------|----------|
| SM-01: 浏览器访问首页 | 打开 http://localhost:{port} | 显示设备名称和状态 |
| SM-02: 触发投屏 | 访问页面，触发投屏 | `<video>` 元素出现 |
| SM-03: 视频播放验证 | 等待视频播放 | `playing` 事件触发，fps > 0 |

### FileTransfer 测试用例

| 用例 | 步骤 | 预期结果 |
|------|------|----------|
| FT-01: 上传文件 | 选择文件并上传 | 显示上传成功，进度 100% |
| FT-02: 下载文件 | 点击下载 | 文件下载完成，内容正确 |
| FT-03: 取消传输 | 上传过程中取消 | 传输中断，状态为 cancelled |

## 关键设计决策

### 1. 应用端口策略

应用启动时动态分配端口（或使用固定测试端口如 8080），测试代码通过环境变量或配置文件获取。

**推荐方案**: 固定测试端口 `18080`，避免端口冲突。

### 2. 视频验证策略

不验证画质和延迟，只验证：
- `<video>` 元素存在
- 触发 `playing` 事件
- `video.readyState >= 2` (HAVE_CURRENT_DATA)

### 3. 超时配置

| 操作 | 超时时间 |
|------|----------|
| 应用启动等待 | 10s |
| 页面加载 | 5s |
| 视频播放等待 | 10s |
| 文件上传/下载 | 30s |

### 4. 测试隔离

- 每个测试类共享一个应用实例（通过 xUnit `IClassFixture`）
- 测试间清理临时文件
- 不依赖外部网络（局域网测试）

## 依赖项

```xml
<PackageReference Include="Microsoft.Playwright" Version="1.48.0" />
<PackageReference Include="xunit" Version="2.9.2" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
```

## 运行方式

```bash
# 安装 Playwright 浏览器（首次）
dotnet test --filter "Category=Install"

# 运行所有 E2E 测试
dotnet test tests/UniLinker.E2E.Tests

# 只运行 ScreenMirror 测试
dotnet test --filter "ScreenMirror"

# 只运行 FileTransfer 测试
dotnet test --filter "FileTransfer"
```

## 已确认事项

- [x] WinUI 应用端口固定为 9527（App.xaml.cs 硬编码）
- [x] 浏览器端 UI 已有完整投屏入口：输入框 + Connect 按钮
- [x] FileTransfer UI 已完成（FileTransferPage.xaml.cs）

## 风险与缓解

| 风险 | 缓解措施 |
|------|----------|
| 应用启动慢 | 增加启动等待超时，使用重试机制 |
| 视频编码依赖 GPU | 在 CI 环境跳过视频相关测试 |
| 浏览器弹窗干扰 | Playwright headless 模式 |
