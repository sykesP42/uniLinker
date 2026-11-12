# 06 · 三端 UI/UX 重构设计

| 项 | 值 |
|---|---|
| 版本 | v1.0 |
| 日期 | 2026-11-12 |
| 状态 | **已生效**（用户选定方案 ② 并追加「UI/UX 重构」范围） |
| 适用范围 | Windows（WinUI 3）、Android（Jetpack Compose）、浏览器观看端（原生 JS） |
| 前置契约 | 协议与接口以 `03` §2 为准；本文件只定义**视觉与交互**，不新增协议字段 |
| 边界 | 本文件是设计规格，不含可运行代码；三端实现分别落在 `07` 与 T06/T07 任务 |

---

## 0. 这份文档解决什么问题

现有三端的 UI 不是"不够漂亮"，而是**三个产品**：

| 端 | 视觉语言 | 证据 |
|---|---|---|
| Android | 工业暖灰 + 铜金（`#C49A6C` / `#121210`），统一 4dp 圆角 | `android/app/src/main/java/com/unilinker/android/ui/theme/Theme.kt:29,33,208` |
| Web | 冷黑 + 靛蓝（`#6366F1` / `#0d0d0d`），6~8px 圆角，Tailwind 色板 | `web/styles.css:4,11,47` |
| Windows | **无品牌色**，全部使用 Fluent 默认 `ThemeResource` | `src/UniLinker.WinUI/Styles/AppStyles.xaml:7-11` |

同一个功能，三端三种观感、三套色板、三套圆角、三套状态色。用户无法建立肌肉记忆，截图放在一起不像一个产品。

本文件给出**一套设计语言 + 15 个界面规格 + 6 条核心流程 + UX 验收标准**，作为 T06/T07 的实现依据。

**设计方向已定：以 Android 已有的「工业暖灰 + 铜金」为品牌 DNA，向三端收敛**，而不是另起炉灶。理由：

1. 这套配色已有实现与代码资产（`Theme.kt` 全量色板），改造成本最低；
2. 铜金 + 暖灰有明确辨识度，不像科技蓝那样满街都是；
3. 「仪表盘/工业设备」的隐喻与"屏幕共享 + 远控"这类**工具型**产品气质一致；
4. Web 的靛蓝 `#6366F1` 与 Windows 的 Fluent 默认蓝都属"通用科技蓝"，是**可替换项**而非资产。

---

## 1. 现状审计

### 1.1 代码级缺陷（可在代码中直接指认）

| 编号 | 缺陷 | 证据 | 影响 |
|---|---|---|---|
| X-01 | 三端三套色板/圆角/状态色，无共享令牌 | 见 §0 表 | 品牌割裂，改一次要改三处 |
| X-02 | `onSurface` 被设为**次级**文字色 | `Theme.kt:68` | 卡片主内容用了次级灰，信息层级整体下压一级 |
| X-03 | M3 的 `surfaceContainer*` / `surfaceBright` / `surfaceDim` / `tertiary` 全未定义 | `Theme.kt:51-86` 未列出；全仓无 `surfaceContainer` 引用 | **潜在风险**：一旦组件使用这些角色（M3 新组件默认会），将回落到基线**紫灰**默认值，出现"脏紫"污染。当前未触发，但必须在重构时补全 |
| X-04 | 注释称使用 JetBrains Mono，实现为 `FontFamily.Default`，且未打包字体 | `Theme.kt:19` vs `Theme.kt:95,117` | 数字（延迟/码率）非等宽，数值跳动时宽度抖动 |
| X-05 | 字号上限 18sp、`labelSmall` 10sp | `Theme.kt:218,178-184` | 10sp 低于可读下限；大屏/低视力用户无法使用 |
| X-06 | 仅 `darkColorScheme`，无 light 分支、不读系统主题 | `Theme.kt:191-200` | 系统切亮色后 App 仍全黑，与其他应用不一致 |
| X-07 | 全局单一圆角 4dp | `Theme.kt:208` | 卡片/按钮/弹层无层级区分，扁平到失去层次 |
| X-08 | Web 字体栈无 CJK 字体 | `web/styles.css:3` | 中文回落到系统默认，与拉丁字重不匹配，中英混排基线错位 |
| X-09 | 信号指示器 4 格为**装饰性**色阶（绿→黄→红），非真实测量 | `web/styles.css:57-72`；Android 同名组件 `ui/components/SignalIndicator.kt` | 用户以为在看真实链路质量，实际与延迟/丢包无关，是**误导性 UI** |
| X-10 | Windows 设备卡固定 220×160 | `AppStyles.xaml:51-52` | 长设备名截断、窄窗口溢出、无法响应式 |
| X-11 | Android 主界面 834 行单文件，UI 与状态强耦合 | `ui/ModernMainScreen.kt`（834 行） | 无法单独预览/复用子界面，改一处牵连全局 |
| X-12 | 无空状态、无骨架屏规范（仅 Web 有 `.skeleton`） | `web/styles.css:105-122`；Android/Windows 无对应 | 首次启动/无设备时是空白页，用户不知道是"没找到"还是"在找" |
| X-13 | 文案硬编码，中英混排，无 i18n 层 | 三端均直接内联字符串 | 无法切换语言，术语不一致（"投屏/镜像/共享"混用） |
| X-14 | 状态反馈不一致：Android 有 `ErrorBanner`、Web 有 `.error-banner`、Windows 分散用 `InfoBar`/`ContentDialog` | `ui/components/ErrorBanner.kt`、`web/styles.css:77`、`Views/*.xaml` | 同一错误三端三种呈现，严重程度分级不统一 |
| X-15 | Windows **没有任何颜色/字体/字号 token**：`AppStyles.xaml`（67 行）只有 6 个 `Style`，圆角全取 `OverlayCornerRadius`；XAML 内联 `FontSize=` **117 处** | `Styles/AppStyles.xaml` 全文；`grep -c 'FontSize=' Views/*.xaml` | 无统一改版入口，只能逐页改 |
| X-16 | Android **定义了自己的 Typography 却不用**：`Theme.kt:92-185` 定义 11 档，界面内联 `fontSize = N.sp` **60 处** | `ui/theme/Theme.kt:92-185`；`ModernMainScreen.kt:147,380,531,594,752` | 字号体系名义存在、实际失效 |
| X-17 | Android **6 个 UI 模块为死代码，合计约 1188 行**，全仓库无调用 | `ui/ConnectionMethodPicker.kt`(401)、`ui/DeviceListView.kt`(188)、`ui/components/{ErrorBanner(154),SignalIndicator(100),StatsOverlay(105)}`；`grep -rn` 无引用点 | 改造时极易"改到死代码"，必须先清理 |
| X-18 | Web **两套样式源并存**：`index.html:8-175` 内联 `<style>` 实际生效，同时 `:7` 又引入 `styles.css`；后者约 60% 规则无对应 DOM | `web/index.html:7,8-175`、`web/styles.css` 全文 | 改一处不生效，维护必漂移 |
| X-19 | 图标体系三端三套：Windows 用 Segoe Fluent Glyph、Android 用 Material Outlined、Web 直接**用 emoji 当图标**（📺 🔍 🖥️ ⚠️） | `web/index.html:212`；`ScreenMirrorTab.kt:153,199,219,264` | 同一功能三种图标语言，emoji 还随系统字体变化 |
| X-20 | 冷启动闪色：`themes.xml:6` 启动底色 `#0D0D0D` 与 `Theme.kt:33` 背景 `#121210` 不一致 | `res/values/themes.xml:3-7` vs `Theme.kt:33` | 每次冷启动闪一次色差 |
| X-21 | Windows 用**系统强调色**（实测为蓝），与 Android 铜金不一致；标题栏颜色另在 code-behind 写死 | `SharePage.xaml:211` 等全部 `ThemeResource`；`MainWindow.xaml.cs:217-243` | 换主题色时只改了安卓，Windows 不会跟 |
| X-22 | Android 视频 **sink 泄漏**：`AndroidView.update` 每次重组都 `videoTrack.addSink(renderer)`，且整个 `ScreenMirrorTab.kt` **无 `release()` 调用** | `plugins/screenmirror/ScreenMirrorTab.kt:103-112`（`:111` 为 `addSink`）；全文件 `release()` 命中 0 | 第二次进会话可能**黑屏**，`EglBase` 与渲染器泄漏；UI-05 的验收会被误判为"合成层问题"（RK-U1） |

### 1.2 结构性 UX 问题（流程层面）

| 编号 | 问题 | 说明 |
|---|---|---|
| Y-01 | 首次启动无引导 | 用户不知道"要被发现"需要开开关、不知道防火墙会拦、不知道 Android 需要两次授权（投屏 + 无障碍）。失败点在第一步，且无解释 |
| Y-02 | 「零流量」是核心卖点但界面无任何证据 | 用户无法在 App 内确认"真的没走公网"，只能靠信任。这是本产品最该被可视化的东西，却完全缺失 |
| Y-03 | 连接方式选择是并列 Tab，无推荐 | 三个方式（局域网/手动 IP/扫码）平铺，用户不知道选哪个、为什么失败 |
| Y-04 | 会话中无质量反馈 | 卡了不知道是网络、编码还是解码；无延迟/丢包/码率数值，无法判断该调画质还是换网络 |
| Y-05 | 被控端授权信息不足 | 只显示"某设备请求控制"，无设备指纹、无"控制范围"说明、无"仅本次/始终"区分 |
| Y-06 | 断线只有报错，无重连 | 网络抖动一次就要从头连，会话上下文全丢 |
| Y-07 | 无键盘可达性 | Windows 端核心操作（开始投屏/切画质/断开）无快捷键，工具栏无焦点环 |
| Y-08 | 窗口尺寸/横竖屏未适配 | Windows 默认 1024×680、**无最小尺寸、0 处 `VisualStateManager`/`AdaptiveTrigger`**；Android `configChanges` 声明了旋转不重建，但投屏页布局未重排 |
| Y-09 | **Windows「Start Share」不启动采集**：点击后只写配置并把 `IsSharing` 置真，真正采集要等 viewer 发起请求才启动 | `ViewModels/ShareViewModel.cs:112-146`（`:132`）、`Plugins.ScreenMirror/ScreenMirrorPlugin.cs:36-58` | 界面显示"Sharing Active"却没在采集，**状态撒谎**，是最严重的信任问题 |
| Y-10 | **Windows「Connect」不是连接**：点设备卡只是弹 `ContentDialog` 内嵌 WebView2 打开对方网页 | `Views/DevicesPage.xaml.cs:36-50` → `Views/DeviceDetailsDialog.xaml.cs:33-34` | 用户以为建立了通道，实际只是看了个网页；对方没在投屏时只看到网页报错 |
| Y-11 | **Android 启动不自动扫描**：`loadActiveStrategy()` 只把 id 读进 state，不调 `discover()`；`LanMdnsStrategy.start()` 是空实现 | `ui/MainViewModel.kt:36-39`、`core/strategies/LanMdnsStrategy.kt:39-41`、`ModernMainScreen.kt:750-765` | 首屏**必然**显示"未发现设备"，用户第一印象就是坏的 |
| Y-12 | Android 无返回键处理、无沉浸式：观看中按返回直接退 Activity，会话不释放；视频四周留系统栏黑边 | `MainActivity.kt:34-62`（全仓 `BackHandler` 命中 0）、无 `enableEdgeToEdge` |
| Y-13 | Windows 通知 InfoBar 盖住标题栏右上角窗口按钮；关闭确认弹窗不读设置里的「最小化到托盘」开关 | `MainWindow.xaml:227-233`；`MainWindow.xaml.cs:335-372` vs `SettingsPage.xaml:124-126` |
| Y-14 | Windows 硬件状态写死：Dashboard `InfoBar Severity="Success" IsOpen="True"` 永远"System Ready"；侧栏状态点永远绿色"Server Online"，启动失败被吞 | `DashboardPage.xaml:22-33`、`MainWindow.xaml:177-183`、`App.xaml.cs:64-71,83-90` | 故障时界面仍显示正常，比没有状态更糟 |
| Y-15 | Web `disconnected` 不复位 UI（视频仍在、按钮仍是"断开"）；`answer` 阶段就把按钮改成"断开"，早于 `connected` | `web/app.js:126-142`、`:198-200` | 中间态语义错误，用户误操作 |

> 以上 X/Y 编号在 `07`（组件规范）与 `08`（UX 验收）中作为追溯锚点被引用。

### 1.3 可复用资产与应删代码（改造前必须先做）

**可直接沿用（不要重写）**

| 端 | 资产 | 位置 |
|---|---|---|
| Windows | 卡片/分区标题/设置行/描述文案 四个 `Style` 骨架 | `Styles/AppStyles.xaml`（`ModernCardStyle`/`SectionHeader`/`SettingRowStyle`/`DescriptionTextStyle`） |
| Windows | `NotificationService`（InfoBar 栈，最多 3 条、5s 消失）、`TrayIcon`（`Shell_NotifyIcon` 完整实现）、三态主题服务 | `Services/NotificationService.cs:46-84`、`Services/TrayIcon.cs:112-267`、`App.xaml.cs:142-189` |
| Windows | 7 张改版前基线截图 | `gui-test-screenshots/`（注意 `app-settings.png` 实际截的是文件传输页，属过期截图） |
| Android | 整套色板与 11 档 Typography、`IndustrialDesign` 间距常量 | `ui/theme/Theme.kt:29-49,92-185,206-228` |
| Android | `ErrorBanner` 的**错误模型**（`suggestions` + `actionLabel`，正是 P3 三件套要求的形状）、`StatsOverlay` 的四项指标布局、`ShareInfoCard` | `ui/components/ErrorBanner.kt:31-39`、`ui/components/StatsOverlay.kt:42-82`、`ui/ConnectionMethodPicker.kt:328-401` |
| Android | 7 个 `@Preview`（Standby/Scanning/WithDevices/Connecting/Connected/Error/ManualIP） | `ui/UiPreviews.kt:49-237` |
| Web | Toast 引擎、`getStats` 指标采集（分辨率/帧率/码率/RTT/解码耗时）、`focus-visible`、`prefers-reduced-motion` | `web/app.js:27-48,267-333`、`web/styles.css:127-148,153-165` |

**应先删除/停用（约 1500 行）**

| 项 | 行数 | 处理 |
|---|---|---|
| Android `ConnectionMethodPicker.kt` / `DeviceListView.kt` / `components/{ErrorBanner,SignalIndicator,StatsOverlay}.kt` | ~948 | 无调用。**保留 `ErrorBanner` 与 `StatsOverlay` 的模型/布局，删掉重复实现**，其余删除 |
| Web `styles.css` 中无 DOM 对应的约 95 行 | ~95 | 删除；保留 `:focus-visible`、`prefers-reduced-motion` 段并入 `tokens.css` |
| Web `webcodecs-decoder.js`（217 行，`useWebCodecs=false` 硬编码） | 217 | P0 删除；WebCodecs 能力留 P2 再评估，不要留"加载了但永久关闭"的死文件 |
| Android `ScreenMirrorTab.kt` 内不可达的 `DeviceListWithControls` 分支 | ~55 | 删除（`devices` 恒空、`LaunchedEffect` 空实现） |

> 顺序很重要：**先删死代码，再改 UI**。否则会出现"花两小时美化了一个从未被调用的组件"。

### 1.4 两条必须先在 UI 层纠正的"假状态"

现状有三个核心动作的**名词与真实行为不一致**，这是比配色更严重的问题，UI 重构必须先统一心智模型：

| 动作 | 现状真实行为 | 重构后必须变成 |
|---|---|---|
| Windows「Start Share」 | 只写配置 + 置 `IsSharing=true`，采集等 viewer 请求才启动（Y-09） | 点击即启动采集与编码，界面出现真实首帧预览；未连接观众时状态为「等待观看者」而非「正在共享」 |
| Windows「Connect」 | 打开内嵌 WebView2 看对方网页（Y-10） | 建立会话：信令握手 → ICE → 媒体，四阶段可定位（F2）；观看网页降级为「在浏览器中打开」次操作 |
| Android「投屏」Tab | 只能接收（`OfferToReceiveVideo`），插件名却叫"屏幕投屏" | 拆成两个明确入口：「投屏到对方」（发送）与「观看对方」（接收），互不混用 |

---

## 2. 设计原则

六条，每条附**可判定的反例**，避免变成口号。

### P1 · 内容优先，界面退让
视频画面是主角，chrome（工具栏/边框/标题）在没有操作时应消失。
> 反例：常驻工具栏遮挡视频边缘；窗口标题栏占 32px 且不可隐藏。

### P2 · 每个数字都必须真实
界面上出现的任何指标必须来自真实测量，不得用装饰性色阶冒充。
> 反例：`styles.css:57-72` 的四格信号条（与真实链路无关）。

### P3 · 状态永远三件套：在做什么 / 为什么 / 我能做什么
任何非稳态（连接中、失败、降级）必须同时给出**现象 + 原因 + 下一步动作**。
> 反例：一个转圈 + "连接失败"；一个红色感叹号没有按钮。

### P4 · 零流量的可见性高于零流量的实现
"没走公网"要能被用户**看见**，而不是写在 README 里。
> 反例：设置页一行灰字"本产品不联网"。

### P5 · 一次操作只推进一步
不合并步骤、不隐藏必要授权。Android 的两次系统授权（MediaProjection + 无障碍）必须拆成两个屏，各自解释清楚。
> 反例：一个页面里连点两次系统弹窗，用户不知道第二个是干什么的。

### P6 · 三端同一语义，不同密度
同一功能在三端用**相同措辞、相同图标语义、相同颜色含义**，但布局密度按平台习惯调整（桌面信息密度高、移动端触控优先）。
> 反例：同一功能 Windows 叫"共享屏幕"、Android 叫"投屏"、Web 叫"镜像"。

---

## 3. 设计令牌（Design Tokens）

三端共用一套语义令牌，**禁止在业务代码里写十六进制**。

### 3.1 色彩

**深色（默认，媒体场景优先）**

| 令牌 | 值 | 用途 |
|---|---|---|
| `color.canvas` | `#121210` | 应用底色 |
| `color.surface` | `#1C1C18` | 卡片、面板 |
| `color.surface-raised` | `#262620` | 悬浮工具条、弹层 |
| `color.surface-sunken` | `#0C0C0A` | 视频舞台底、凹陷区 |
| `color.border-subtle` | `#2A2A26` | 分隔线、次级描边 |
| `color.border-strong` | `#3A3A34` | 输入框、卡片描边 |
| `color.text-primary` | `#E8E6E0` | 主文字（对比度 12.6:1 on canvas） |
| `color.text-secondary` | `#B8B6B0` | 次文字（9.2:1） |
| `color.text-tertiary` | `#8A8880` | 辅助文字（**由 `#7A7872` 上调**，5.2:1；原值仅 4.2:1 不达标） |
| `color.accent` | `#C49A6C` | 品牌铜金、主操作、焦点（7.3:1） |
| `color.accent-hover` | `#D6AE7E` | 悬停 |
| `color.accent-press` | `#AC8557` | 按下 |
| `color.on-accent` | `#1A1A1A` | 铜金上的文字（6.8:1） |
| `color.success` | `#7BA05B` | 已连接、正常（6.2:1） |
| `color.warning` | `#D4A84B` | 降级、待授权（8.5:1） |
| `color.danger` | `#E85D4C` | 错误、断开（5.4:1） |
| `color.focus-ring` | `#C49A6C` | 焦点环（2px，偏移 2px） |

**浅色（跟随系统）**

| 令牌 | 值 | 说明 |
|---|---|---|
| `color.canvas` | `#F4F2EE` | 暖白，与品牌同源 |
| `color.surface` | `#FFFFFF` | |
| `color.surface-raised` | `#FFFFFF` | 用描边而非阴影区分 |
| `color.surface-sunken` | `#E8E5DF` | |
| `color.border-subtle` | `#E0DCD4` | |
| `color.border-strong` | `#C9C4BA` | |
| `color.text-primary` | `#1A1A18` | |
| `color.text-secondary` | `#55534E` | |
| `color.text-tertiary` | `#75726B` | |
| `color.accent` | `#8A6A42` | **浅色下铜金必须加深**，`#C49A6C` 在白底仅 2.4:1，不可用于文字 |
| `color.on-accent` | `#FFFFFF` | 4.9:1 |
| `color.accent-hover` | `#7A5C38` | 浅色悬停（6.2:1 on white） |
| `color.accent-press` | `#6B4E2E` | 浅色按下（7.7:1） |
| `color.success` | `#4A7530` | **由候选 `#4C7A32` 下调**：原值在 `#F4F2EE` 上仅 4.55:1，余量不足；现值 4.85:1 |
| `color.warning` | `#856312` | **由候选 `#8C6A17` 下调**：原值 4.52:1；现值 4.95:1 |
| `color.danger` | `#B23B2C` | 5.3:1 |
| `color.focus-ring` | `#8A6A42` | 与 accent 同值，2px 环需 ≥3:1，实测 4.9:1 |

> 浅色分支不能用深色值直接换算：`#C49A6C` 在白底仅 **2.4:1**、`#D4A84B`（深色 warning）在白底约 **2.0:1**，均不可用于文字。上表 6 个值为按同色相降明度后的实测结果，已计入 C-15 裁决。

**语义色使用规则（强制）**

1. 铜金 `accent` **只用于**：主按钮、当前选中项、焦点环、可交互强调。不得用于大面积背景。
2. 绿/黄/红**只表达状态**，不表达品牌。
3. 状态不得只靠颜色传达：必须 图标 + 文字 同时存在（色盲可达）。
4. 会话页的视频舞台背景固定 `surface-sunken`，不随主题变化。

### 3.2 排版

| 令牌 | Windows（px，Fluent 阶梯） | Android（M3 角色 / sp） | Web (px) | 字重 |
|---|---|---|---|---|
| `text.display` | 28 / 36 | `headlineMedium` 28/36 | 24 / 32 | 600 |
| `text.title` | 20 / 28 | `titleLarge` 22/28 | 18 / 24 | 600 |
| `text.subtitle` | 16 / 22 | `titleMedium` 16/24 | 16 / 22 | 600 |
| `text.body` | 14 / 20 | `bodyMedium` 14/20 | 14 / 20 | 400 |
| `text.body-strong` | 14 / 20 | `bodyMedium` 14/20 | 14 / 20 | 600 |
| `text.caption` | 12 / 16 | `bodySmall` 12/16 | 12 / 16 | 400 |
| `text.mono` | 13 / 20 | `bodySmall` 12/16（等宽族） | 13 / 18 | 500 |

> 数值全部取自平台官方阶梯，不自造：Windows 用 Fluent ramp `12/16 · 14/20 · 16/22 · 20/28 · 28/36`，最小可用为 14 Semibold / 12 Regular；Android 用 M3 十五档中的对应角色。**不引入第四套字号体系**。
>
> 修正 X-05：原 `headlineLarge = 18sp`、`labelSmall = 10sp` 低于两端官方下限，全部上移到上表。

**字体族（关键约束：全部使用系统字体，禁止外链 Web 字体）**

产品必须在**无互联网**的局域网内正常工作，任何 `fonts.googleapis.com` / CDN 字体都会在白屏状态阻塞渲染或直接失败。因此：

| 端 | 字体栈 |
|---|---|
| Windows | `Segoe UI Variable Display`（标题）/ `Segoe UI Variable Text`（正文）；数值用 `Cascadia Mono`（Win11 内置），回退 `Consolas` |
| Android | `FontFamily.Default`（即 Roboto / 厂商系统字体）；中文回落系统 Noto Sans CJK；数值 `FontFamily.Monospace` |
| Web | `-apple-system, "Segoe UI Variable Text", "Segoe UI", Roboto, "Helvetica Neue", "PingFang SC", "Microsoft YaHei", sans-serif`；数值 `ui-monospace, "Cascadia Mono", Consolas, monospace` |

> 修复 X-04：所有延迟/码率/帧率数值必须 `tabular-nums` / 等宽，避免跳动。

### 3.3 间距 · 圆角 · 层级 · 动效

| 类别 | 令牌 | 值 | 说明 |
|---|---|---|---|
| 间距 | `space.1/2/3/4/6/8/12` | 4 / 8 / 12 / 16 / 24 / 32 / 48 | 4pt 网格，三端同一套 |
| 圆角 | `radius.xs` | 4 | 标签、Chip |
| | `radius.sm` | 8 | 按钮、输入框、设备卡 |
| | `radius.md` | 12 | 内容卡片、面板 |
| | `radius.lg` | 16 | 对话框、底部弹层 |
| | `radius.full` | 999 | 胶囊、头像、状态点 |

> **圆角偏差说明**：Fluent 2 的官方圆角上限是 8（small 2 / medium 4 / large 6 / xlarge 8），本设计在 Windows 上用到 12/16 是**有意的品牌取舍**——若完全遵循 Fluent 上限，三端会出现"Windows 方正、手机圆润"的割裂。系统控件（NavigationView、ContentDialog、Flyout）仍用 `OverlayCornerRadius` 保持原生观感，**只对自绘卡片/面板使用本套圆角**。
| 层级 | `elev.0` | 无 | 平面（深色默认） |
| | `elev.1` | `0 1px 2px rgba(0,0,0,.24)` | 卡片悬停 |
| | `elev.2` | `0 8px 24px rgba(0,0,0,.32)` | 悬浮工具条、下拉 |
| | `elev.3` | `0 16px 48px rgba(0,0,0,.40)` | 模态、对话框 |
| 动效 | `dur.fast` | 100ms | 悬停/按下 |
| | `dur.base` | 160ms | 展开/切换 |
| | `dur.panel` | 240ms | 抽屉/弹层 |
| | `dur.page` | 320ms | 路由切换 |
| 曲线 | `ease.standard` | `cubic-bezier(0.2, 0, 0, 1)` | 默认 |
| | `ease.emphasized` | `cubic-bezier(0.05, 0.7, 0.1, 1)` | 进入 |
| | `ease.exit` | `cubic-bezier(0.3, 0, 1, 1)` | 退出 |

**动效硬规则**：
- 视频画面**永不加过渡动画**（禁 fade/scale），任何一帧延迟都会被感知。
- 工具栏显示/隐藏不得改变视频的布局尺寸（用 overlay + opacity/translate，不占位）。
- `prefers-reduced-motion` / 系统「减少动画」开启时：所有位移动画降级为 ≤100ms 的透明度变化。

### 3.4 三端令牌落地方式

| 端 | 落地位置 | 做法 |
|---|---|---|
| Windows | `src/UniLinker.WinUI/Styles/Tokens.xaml`（新建）+ `AppStyles.xaml`（改造） | `ResourceDictionary` 定义 `<Color x:Key="UlColorAccent">` 与 `<SolidColorBrush x:Key="UlBrushAccent">`；用 `ThemeDictionaries` 提供 `Light`/`Dark` 两套，跟随系统。**必须显式覆盖系统强调色**——现状全部取 `ThemeResource` 系统蓝（X-21），不覆盖则 Windows 永远是蓝的 |
| Android | `ui/theme/Theme.kt`（改造为 `Tokens.kt` + `Theme.kt`） | `data class UlColors` + `LocalUlColors` CompositionLocal 提供非 M3 令牌；同时把 M3 `darkColorScheme`/`lightColorScheme` 的**全部角色补齐**（含 `surfaceContainer*`、`tertiary`） |
| Web | `web/tokens.css`（新建），`styles.css` 改为引用变量 | `:root { --ul-color-accent: #C49A6C; ... }` + `@media (prefers-color-scheme: light)` 覆盖 |

> 令牌命名统一前缀 `Ul` / `ul-` / `UL`，与现有 `Industrial*` 命名并存过渡一版后删除旧常量。

### 3.5 补充令牌（v1.1 增补，C-15 裁决）

§3.1~§3.3 是初版令牌。组件级规格（`07`）在落地时发现 8 处"用了数值但没有令牌名"的缺口，全部**补入本节**，使本文件继续作为唯一真源（对应 `03` §10 L15）。

| 编号 | 令牌 | 值 | 用途 |
|---|---|---|---|
| T-1 | `color.scrim` | `rgba(0,0,0,.40)`（弹层）/ `rgba(0,0,0,.60)`（被控授权） | 遮罩。两个强度必须分开：弹层用 40%，授权遮罩用 60% |
| T-2 | `color.skeleton-base` / `color.skeleton-sheen` | `#1C1C18` / `rgba(255,255,255,.10)` | 骨架屏底色与扫光 |
| T-3 | `color.{status}-soft` | 对应状态色 20% alpha | 状态点外圈、危险按钮底、Chip 底 |
| T-4 | `size.dot` | 8 | 状态点直径 |
| | `size.control-desktop` | 40 | 桌面控件高度 |
| | `size.control-mobile` | 44 | 移动主按钮高度 |
| | `size.touch-min` | 48 | 移动最小热区 |
| | `size.icon-empty` | 64 | 空状态图标 |
| T-5 | `dur.breathe` | 1600ms | 状态点呼吸周期 |
| | `dur.shimmer` | 1500ms | 骨架屏扫光周期 |
| T-6 | `size.device-card-min-h` | 72 | 设备卡最小高度 |
| T-7 | `z.hud` / `z.toolbar` / `z.sheet` / `z.toast` | 10 / 20 / 100 / 1000 | 三端统一层叠顺序；会话内 HUD 必须低于工具栏，弹层高于二者，Toast 最高 |
| T-8 | `UlEase*`（字体学例外） | `cubic-bezier` 字符串 | **唯一允许以字面量形式存在的令牌**：WinUI 无法把缓动曲线实例化为强类型资源，故 `Tokens.xaml` 中以 `sys:String` 保存 |

**规则**：T-1~T-7 三端都必须定义为正式令牌，业务代码不得直接写 alpha、px、ms 数值。T-8 是**书面豁免**，除缓动曲线外无其他豁免。

---

## 4. 信息架构

### 4.1 三端导航模型

| 端 | 模型 | 理由 |
|---|---|---|
| Windows | `NavigationView`（Left，可折叠）+ **会话页为独立全屏窗口** | 桌面信息密度高，5 个平级入口；会话需要最大化且可能跨屏拖动，嵌在 NavigationView 内会被裁切 |
| Android | 底部 `NavigationBar` 3 项：设备 / 投屏 / 设置 + **会话页为独立全屏 Activity** | 3 项是移动端上限的舒适区；会话页要隐藏系统栏、接管返回手势 |
| Web | 单页两面：`连接态` / `会话态`，无导航 | 观看端只做一件事，加导航是负担 |

**断点（官方口径）**

| 端 | Compact | Medium | Expanded | 行为 |
|---|---|---|---|---|
| Windows | < 641 px | 641–1007 px | ≥ 1008 px | Compact：NavigationView 收为图标栏；Medium：设备列表 2 列；Expanded：3~4 列 + 右侧详情常驻 |
| Android | < 600 dp | 600–839 dp | ≥ 840 dp | Compact：单列 + 底部导航；Medium/Expanded：`NavigationRail` + 双栏（列表 ‖ 详情） |
| Web | < 900 px | — | ≥ 900 px | 单列 / 左侧设备栏 + 右侧舞台 |

### 4.2 界面清单（15 屏）

| ID | 端 | 名称 | 职责 | 现状 |
|---|---|---|---|---|
| **W1** | Win | 概览 Dashboard | 本机身份卡（名称/IP:9527/可被发现开关）、最近设备、快速投屏入口 | 有 `DashboardPage.xaml`（201 行），需重排 |
| **W2** | Win | 设备 Devices | 发现列表（mDNS 实时）、手动 IP 连接、二维码、搜索框 | 有 `DevicesPage.xaml`（199 行），需重做卡片与空状态 |
| **W3** | Win | 投屏 Share | 源选择（整屏/窗口/区域）、画质档、预览、开始 | 有 `SharePage.xaml`（313 行），需重做源选择网格 |
| **W4** | Win | 文件传输 | 拖拽区 + 队列 | 有 `FileTransferPage.xaml`（255 行），仅视觉收敛 |
| **W5** | Win | 设置 | 分组卡片：通用/网络/画质/安全/关于 | 有 `SettingsPage.xaml`（447 行），需重排与补齐 |
| **W6** | Win | 首次引导 | 3 步：说明局域网原理 → 防火墙放行 → 完成自检 | **新建** |
| **W7** | Win | 会话窗口 | 全屏视频 + 悬浮命令条 + 统计抽屉 + 局域网边界卡 | **新建**（当前无独立会话窗口） |
| **A1** | And | 设备（首页） | 可被发现开关、附近设备、手动连接入口 | `ModernMainScreen.kt` 内，需拆分 |
| **A2** | And | 连接方式 | BottomSheet：局域网/手动 IP/扫码 | 有 `ConnectionMethodPicker.kt`（401 行），需重做 |
| **A3** | And | 投屏准备 | 源选择、画质档、方向提示、开始按钮 | **新建** |
| **A4** | And | 被控授权 | 全屏半透明遮罩：设备指纹、控制范围、仅本次/始终 | **新建** |
| **A5** | And | 设置 | 分组列表 | 内嵌，需独立 |
| **A6** | And | 会话 | 全屏 SurfaceView + 边缘唤出工具栏 + 统计浮层 | 部分有（`StatsOverlay.kt`），需重做 chrome |
| **X1** | Web | 连接页 | 设备名、连接方式、错误提示 | 有 `index.html` Tabs，需重做 |
| **X2** | Web | 会话页 | 视频 + 顶部状态条 + 底部 HUD | 有，需重做 HUD |

### 4.3 三端共用的四个"全局元素"

这四个元素的语义在三端必须完全一致，是品牌一致性的锚点。

1. **状态点（Status Dot）**：`radius.full` 8px 圆点 + 文字。灰=空闲、铜金呼吸=连接中、绿=已连接、黄=降级、红=断开。呼吸动画仅"连接中"使用，周期 1.6s。
2. **局域网边界卡（LAN Boundary）**：见 §7.3，本产品的差异化组件。
3. **链路卡（Link HUD）**：四项真实数值 + 30 点 sparkline。
4. **空状态（Empty State）**：图标 + 一句话说明 + 一个主操作，永不留白。

---

## 5. 核心界面线框

> 线框用 `·` 表示留白，宽度按 100 列近似；不代表像素精度，只表达层级与分区。

### 5.1 W7 · Windows 会话窗口（最重要的一屏）

```
┌──────────────────────────────────────────────────────────────────────────┐
│  ● 已连接 · Pixel 7  ←  192.168.1.23        延迟 42ms   ⟳   ⛶   ⋯      │  ← 命令条（悬浮，2.5s 后隐）
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│                                                                          │
│                         ┌────────────────────────┐                        │
│                         │                        │                        │
│                         │      远端画面           │                        │
│                         │   （保持原始宽高比）      │                        │
│                         │                        │                        │
│                         └────────────────────────┘                        │
│                                                                          │
│                                                                          │
├──────────────────────────────────────────────────────────────────────────┤
│  1080p 60fps · 8.2 Mbps · 丢包 0.3% · 编码 6ms          仅局域网 · 0 B 公网 │  ← 状态条（细，常驻）
└──────────────────────────────────────────────────────────────────────────┘
```

- 命令条与状态条均为 **overlay**，不挤压视频区域（P1）。
- 命令条隐藏条件：鼠标静止 2.5s 且焦点不在其内；唤出条件：鼠标进入顶部 8px、或按 `Ctrl+Shift+T`、或视频获得焦点后按 `Tab`。
- 统计抽屉（`⋯`）从右侧滑出 320px，含完整指标与「局域网边界卡」。
- 视频区域双击切换 1:1 / 适应窗口；`Esc` 退出全屏但**不断开会话**。
- 窗口底层用 **Mica**（`Window.SystemBackdrop = MicaBackdrop`），内容层再叠 `LayerFillColorDefaultBrush` 形成两层；关闭"透明效果"/节电模式/低端硬件/Windows < 22000 时自动回退纯色 `SolidBackgroundFillBase`。会话窗口是长驻窗口，Mica 只采样一次壁纸，开销可接受。
- 命令条按 WinUI `CommandBar` 四区模型组织：左侧 1 个状态区（状态点+延迟），右侧 ≤4 个主命令，其余进 `…` 溢出；窄窗口下主命令自动溢出，不横向滚动。
- 状态条不用 `CommandBar`（它会吃掉 48px 高），改为自定义 24px 高 `Grid`，避免遮挡视频。

### 5.2 W2 · Windows 设备页（列表 + 详情双栏）

```
┌───────────────┬──────────────────────────────────────────────────────────┐
│  ◆ uniLinker   │  设备                                             ⟳ 搜索  │
│               │                                                          │
│  ▸ 概览        │  ┌────────────────────────────────────────────────┐     │
│  ▸ 设备     ◀  │  │ ● Pixel 7                  Android 14  · Wi-Fi │     │
│  ▸ 投屏        │  │   192.168.1.23:9527         mDNS · 2s 前         │     │
│  ▸ 文件        │  │                            [ 连接 ]  [ ⋯ ]      │     │
│  ▸ 设置        │  └────────────────────────────────────────────────┘     │
│               │  ┌────────────────────────────────────────────────┐     │
│  ─────────    │  │ ○ DESKTOP-8F2A             Windows 11 · 有线   │     │
│  本机          │  │   192.168.1.10:9527         15s 前（可能离线）   │     │
│  192.168.1.10 │  │                            [ 连接 ]  [ ⋯ ]      │     │
│  端口 9527     │  └────────────────────────────────────────────────┘     │
│  ● 可被发现    │                                                          │
│               │  ── 没有找到你的设备？ ─────────────────────────────    │
│               │  [ 手动输入 IP ]   [ 扫描二维码 ]   [ 检查防火墙 ]       │
└───────────────┴──────────────────────────────────────────────────────────┘
```

要点：
- 卡片高度自适应内容（修 X-10），长设备名 2 行截断 + tooltip。
- 在线/离线用**不同形状**：在线实心圆 + 铜金左描边 2px；离线空心圆 + 灰字「15s 前（可能离线）」。
- 空状态（无设备）不留白，直接显示三个排障入口。
- 手动 IP 与扫码不是并列 Tab，而是「没找到？」的降级路径（修 Y-03）。

### 5.3 A3 · Android 投屏准备

```
┌─────────────────────────────┐
│  ←        准备投屏           │
│                             │
│  共享内容                    │
│  ┌───────────┐ ┌───────────┐│
│  │  ▣ 整个屏幕 │ │  ▢ 单个应用 ││   ← 选中项：铜金 1.5dp 描边 + 底部对勾
│  └───────────┘ └───────────┘│
│                             │
│  画质                        │
│  ○ 流畅   720p · 30fps      │
│  ● 标准   1080p · 30fps      │   ← 默认
│  ○ 高清   1080p · 60fps      │
│                             │
│  ⓘ 请保持本页在前台，系统将  │
│    弹出「开始录制或投放」授权 │
│                             │
│  ┌─────────────────────────┐│
│  │       开始投屏           ││   ← 主按钮，44dp 高，铜金实心
│  └─────────────────────────┘│
└─────────────────────────────┘
```

- 画质档必须显示**真实参数**，不用"高清/超清"这类无意义形容词。
- 授权前必须预告系统弹窗（P5），否则用户会以为是恶意弹窗。

### 5.4 A6 · Android 会话页（观看/被控）

```
┌─────────────────────────────┐
│ ╭─────────────────────────╮ │  ← 工具栏：从顶部下滑 8dp 唤出，2.5s 隐
│ │ ● 已连接  42ms   ⛶   ✕ │ │
│ ╰─────────────────────────╯ │
│                             │
│                             │
│        远端画面（全屏）        │
│                             │
│                             │
│        ╭──────────╮         │  ← 底部胶囊：单击唤出统计，长按断开
│        │ 8.2M · 0.3% │       │
│        ╰──────────╯         │
└─────────────────────────────┘
```

- 系统栏（状态栏/导航栏）进入会话即隐藏（`WindowInsetsController`），从边缘下滑临时显示。
- 返回手势：第一次返回弹「再滑一次断开」，2s 内第二次才断开（防误触）。
- 被控中必须有**常驻提示**：顶部 1px 铜金进度条式指示 + 通知栏常驻（M4 验收要求）。

> **技术约束（决定这一屏能否实现）**：Compose 中 `AndroidView` 包裹的 `SurfaceView` 运行在独立合成层，**不受** Compose 的 `clip()`/`clipToOutline` 约束，圆角裁切与缩放/透明度动画都会失效；且其 z 序与 Compose 覆盖层的关系依赖 `setZOrderOnTop` 与机型实现。
> 因此本屏定为：**全屏用 `SurfaceView`（低延迟、低功耗优先），覆盖层（工具栏/胶囊）放在同一 Compose 层级中、位于 `AndroidView` 之后**，禁用任何对视频视图的圆角/缩放动画。
> 若实机验证覆盖层被遮挡，降级方案为 `TextureView`（可裁切、可动画，但同分辨率下像素处理成本约为 SurfaceView 的两倍）。该分支作为 M4 的验证项，不靠猜。

### 5.5 X2 · Web 会话页

```
┌──────────────────────────────────────────────────────────────┐
│ ◆ uniLinker    ● 已连接 · Pixel 7          42ms   8.2M   ⛶  │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│                    ┌──────────────────────┐                   │
│                    │                      │                   │
│                    │      远端画面         │                   │
│                    │                      │                   │
│                    └──────────────────────┘                   │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│ 1080p60 · H.264 · 仅局域网 · 公网流量 0 B        [ 统计 ]     │
└──────────────────────────────────────────────────────────────┘
```

Web 端只保留**只读观看**，不提供控制（控制是 P1；且浏览器注入权限模型不同，混在一起会让界面复杂度翻倍）。

---

## 6. 核心流程交互规格

每个流程给出：触发 → 步骤 → 每步的可见状态 → 失败分支 → 时长预算。

### F1 · 冷启动与自检（预算 3s 内可用）

| 步骤 | 可见状态 | 时长 |
|---|---|---|
| 1 启动 | 骨架屏（canvas 底 + 3 条 shimmer 行），不闪白 | 0–300ms |
| 2 本机信息 | 名称 / IP:9527 / 「可被发现」开关（默认开） | ≤500ms |
| 3 网络自检 | 若有虚拟网卡/多网卡，显示「检测到 3 个网络接口，已选择 192.168.1.x」+ 「更改」 | ≤800ms |
| 4 权限自检（仅 Android） | 缺 MediaProjection → 不提示（按需申请）；缺通知权限 → 顶部黄条「无法常驻提示，可能被系统杀后台」+「去开启」 | 即时 |
| 5 防火墙自检（仅 Windows） | 9527 未放行 → 黄条「其他设备可能无法发现本机」+「一键放行」 | ≤1s |

失败分支：自检超时 2s → 不阻塞主界面，黄条降级提示「网络自检未完成，可手动输入 IP 连接」。

### F2 · 发现并连接

| 步骤 | 可见状态 | 失败分支 |
|---|---|---|
| 1 列表扫描 | 顶部细进度条（2px，铜金，indeterminate）；列表按「在线 → 最近在线 → 离线」排序 | 15s 无结果 → 空状态 + 三个排障入口 |
| 2 点击设备卡 | 卡片右侧「连接」→ loading 态（按钮内 spinner，宽度不变） | 3s 超时 → 卡内红字「无响应，请检查对方是否开启可被发现」+「重试」 |
| 3 握手 | 卡片展开为「正在建立会话…」+ 4 阶段进度点（发现 → 信令 → ICE → 媒体） | 在具体阶段失败时，**该阶段点变红**并给出该阶段专属提示（ICE 失败提示检查防火墙/是否同一网段） |
| 4 进入会话 | 屏幕 320ms 内淡入视频舞台；首个关键帧到达前显示「正在接收画面…」 | 5s 无首帧 → 提示「已连接但无画面」+ 三项排查（对方是否在动、画质档、编解码兼容） |

关键设计：**四阶段进度点**把"连接失败"从黑箱变成可定位（修 Y-01/Y-03）。

### F3 · 发起投屏（Android 作为发送方）

| 步骤 | 可见状态 | 说明 |
|---|---|---|
| 1 选源与画质 | 见 §5.3 | — |
| 2 点「开始投屏」 | 按钮 → loading；页面出现半透明遮罩「等待系统授权…」 | 必须在系统弹窗之前，避免用户以为卡死 |
| 3 系统 MediaProjection 弹窗 | 应用无法定制，但**弹窗前有预告、弹窗后可解释** | 用户取消 → 返回步骤 1，顶部黄条「已取消授权，未开始投屏」（不报错） |
| 4 倒计时 3-2-1 | 大号数字居中，可点「取消」 | 给用户 3s 反悔期，也掩盖编码器初始化 |
| 5 进入会话 | 顶部常驻「正在共享屏幕」+ 通知栏 | 必须可一键停止 |

> MediaProjection 每次会话都需重新授权（平台限制，见 `00` §8），设计上不试图绕过，而是用「预告 + 取消不报错 + 3s 反悔」把摩擦降到最低。

### F4 · 会话中的质量调节

- 入口：命令条画质下拉（流畅/标准/高清）或统计抽屉里的「自适应」开关。
- 切换**不重建会话**：只发 `MediaCapability` 更新（`03` §2.5），编码器热重配。
- 切换后 1.5s 内必须给出反馈：状态条码率变化 + 若失败则回滚并 toast「该档位不可用，已回到标准」。
- 自适应开关关闭时，明确显示「已锁定 1080p60，网络不足时会卡顿」——把后果说清楚，而不是偷偷降质。

### F5 · 被控授权（接收方同意）

| 步骤 | 可见状态 |
|---|---|
| 1 收到请求 | 全屏半透明遮罩（`scrim` 60%），中央卡片四项**可核对信息**：① 请求方设备名与平台 ② 对端 IP ③ **六位核对码**（两端显示同一串，人工比对）④ 请求的操作范围（仅观看 / 可控制）。默认选中「仅观看」 |
| 2 选择范围 | 两段式：先选范围（仅观看 / 可控制），再选时效（「仅本次」为主按钮 / 「始终允许此设备」为次按钮）；右上角「拒绝」 |
| 3 拒绝 | 遮罩关闭 + 发送端收到明确原因「对方拒绝了本次控制」；本方记录一条「已拒绝 xxx 的控制请求」 |
| 4 授权后 | 屏幕四边 2px 铜金呼吸边框 + 通知栏常驻「xxx 正在控制本机」+ 悬浮停止按钮（可拖动） |
| 5 撤销 | 点悬浮按钮或通知栏「停止」→ ≤1s 内发送端画面冻结并提示「控制已被对方终止」 |

- 遮罩**不可**通过点击外部关闭（防误触），只能明确选择。
- 「始终允许」需要二次确认，并可在设置中一键清除所有授权记录。
- 六位核对码由**会话密钥派生**（取 `HMAC(sessionKey, "verify")` 前 6 位十进制），仅用于人眼比对，**不新增协议字段、不参与认证**——因此不违反 `03` §2.3 关于 P0 `PairingProof.method="none"` 的约定（C-2）。
- 参照 Deskreen（同意页显示发起端 IP + 操作系统供核对）与 Samsung Flow（两端显示同一 passcode 人工比对）的做法：无账号体系下，**可核对的具体信息**比"信任本设备"这类抽象承诺有效得多。

### F6 · 断线重连

| 阶段 | 可见状态 | 行为 |
|---|---|---|
| 0–0.5s | 视频冻结（保留最后一帧，不黑屏） + 顶部黄条「连接中断，正在重连 (1/5)」 | 立即发起第 1 次重连 |
| 0.5–15.5s | 同上，计数递增，指数退避 0.5 / 1 / 2 / 4 / 8s | 保留会话上下文，不销毁编码器 |
| 成功 | 黄条变绿 1.5s 后消失；视频恢复（可能先花屏再清晰，符合 H.264 特性） | 重连计数清零 |
| 5 次失败 | 中央卡片：原因分类（网络切换 / 对方关闭 / 超时 / 认证失效）+ 两个按钮「立即重连」（左，次）「结束会话」（右，主） | 会话上下文保留 60s，超时才彻底释放 |

- 重连期间黄条右侧常驻两个文字按钮：「立即重连」「结束」，**不让用户干等 15 秒**。这是现有 v3.1 最典型的体验缺口（断线后只剩报错，无任何自愈与出口）。
- 原因分类不是猜：从 `SessionState` 与 ICE 状态机取实际失败阶段（见 `03` §4 状态机），映射到四类文案。

> 关键：**保留最后一帧**而不是黑屏，让用户知道"画面还在，只是断了"。

---

## 7. 关键组件规范

组件以「语义 + 状态」定义，三端各自实现但外观一致。完整代码级规格见 `07`。

### 7.1 状态点 StatusDot

| 状态 | 颜色 | 形状 | 附加 |
|---|---|---|---|
| idle | `text-tertiary` | 实心 8px | — |
| connecting | `accent` | 实心 8px | 呼吸动画 1.6s（`ease.standard`，opacity 1→0.35） |
| connected | `success` | 实心 8px | 外圈 2px `success` 20% alpha |
| degraded | `warning` | 实心 8px | — |
| error | `danger` | 实心 8px | — |

必须与文字标签同时出现（如「● 已连接」），不得单独使用。

### 7.2 设备卡 DeviceCard

- 结构：状态点 + 设备名（`text.subtitle`）+ 副行（平台 · 网络类型 · IP:端口 · 最后在线）+ 右侧操作区。
- 尺寸：高度 ≥ 72px，宽度 100% 自适应；最大 2 行文字截断。
- 状态：`default` / `hover`（`elev.1` + border-strong）/ `selected`（左 2px `accent` 描边）/ `connecting`（展开 4 阶段点）/ `disabled`（60% 不透明）。
- 键盘：`Tab` 可达，`Enter` 触发连接，`Delete` 移除历史设备。

### 7.3 局域网边界卡 LAN Boundary（差异化组件）

这是把「零流量」从承诺变成证据的核心组件（P4）。

```
┌────────────────────────────────────────────┐
│  仅局域网                        已验证 ✓   │
│                                            │
│  本机      192.168.1.10  (Wi-Fi)           │
│  对端      192.168.1.23  (同一网段)         │
│  已传输    128.4 MB                        │
│  公网连接  0                               │
│  中转/云   无                               │
│                                            │
│  [ 查看网络接口 ]        [ 如何验证？ ]      │
└────────────────────────────────────────────┘
```

- 四个判据全部来自真实数据：本机接口、对端地址、会话字节计数、出站公网连接数（应为 0）。
- 「0 公网连接」为 0 时显示绿勾；若检测到任何非私有网段目的地址，立即变红并写明「检测到 1 个公网连接」。
- 位置：Windows W7 统计抽屉顶部、Android A6 统计浮层顶部、Web X2 状态条点击展开。

### 7.4 链路卡 Link HUD

四项数值 + sparkline（30 点，1s 采样，保留 30s）：

| 指标 | 格式 | 预警阈值 | 数据来源 |
|---|---|---|---|
| 延迟 | `42 ms` | >120ms 黄，>250ms 红 | 见 §7.4.1 分层 |
| 码率 | `8.2 Mbps` | 低于档位目标 60% 黄 | RTP 出/入字节速率 |
| 丢包 | `0.3 %` | >2% 黄，>5% 红 | RTP 序列号缺口 |
| 分辨率@帧率 | `1080p 60` | 实际 < 目标 80% 黄 | 解码器输出尺寸/帧计数 |

**禁止**用信号格示意；数值必须等宽字体（修 X-09）。

#### 7.4.1 延迟必须分层显示，不能只有一个总数

单个「延迟 42ms」无法定位问题。参照 Moonlight 的做法，拆成三段，用户/排障才能判断是网络、解码还是渲染：

| 层 | 名称 | 含义 | 典型值（LAN） |
|---|---|---|---|
| L1 | `网络` | 采集端时间戳 → 接收端收到包 | 5–20 ms |
| L2 | `解码` | 收到包 → 解码完成 | 3–12 ms |
| L3 | `渲染` | 解码完成 → 上屏 | 2–8 ms |
| — | `总延迟` | L1+L2+L3（同一单调时基，见 `03` §2.5 与 D-8） | ≤40 ms |

- 默认只显示总延迟；展开统计抽屉后显示 L1/L2/L3 三段，用横向堆叠条形图（总长 100%，三段按占比着色，同色系深浅而非红黄绿）。
- 同时给出丢包的两个口径：`网络丢包`（未到达）与 `抖动丢包`（到达但超期被丢弃，jitter buffer 造成）——这两者的处置完全不同，混成一个数会误导排障方向。
- 所有数值必须标注采样窗口（如「近 3s」），否则用户会拿瞬时值当稳定值。

### 7.5 其余组件

| 组件 | 关键规则 |
|---|---|
| 主按钮 | 高 44px（移动 44dp）/ 40px（桌面），`radius.sm`，`accent` 实心，`on-accent` 文字；loading 时按钮宽度不变，仅图标替换为 16px spinner |
| 次按钮 | `border-strong` 1px 描边 + `text-primary`，无填充 |
| 危险按钮 | `danger` 文字 + `danger` 20% alpha 填充，仅用于「断开/撤销授权」 |
| 输入框 | 高 40px，`border-strong` 1px，聚焦时 `accent` 1.5px + 2px 外发光（12% alpha）；错误态 `danger` 描边 + 下方 `text.caption` 红字说明 |
| 画质分段控件 | 三段等宽，选中段 `accent` 填充 `on-accent` 文字；必须显示参数副行 |
| Toast | 右下（桌面）/ 底部（移动），`elev.3`，4s 自动消失，最多堆叠 3 条，可手动关闭 |
| 底部弹层 | `radius.lg` 顶部两角，遮罩 40%，可下滑关闭，最大高度 80vh |
| 空状态 | 64px 线性图标（`text-tertiary`）+ `text.subtitle` 一句说明 + 一个主操作 + 最多两个次操作 |
| 骨架屏 | canvas 底 + shimmer（`ease.standard`，1.5s），不得出现布局跳动 |
| 错误横幅 | 图标 + 现象 + 原因 + 动作（P3 三件套）；info/warning/error/critical 四级，色仅用于图标与左侧 2px 边 |

---

## 8. 无障碍与国际化

### 8.1 无障碍（对齐 WCAG 2.2 AA）

| 项 | 要求 |
|---|---|
| 对比度 | 正文 ≥4.5:1；大字（≥18.66px bold 或 ≥24px）与图形/边框 ≥3:1。§3.1 全部令牌已核算 |
| 触控目标 | Android ≥48×48dp（视觉可 40，热区补足）；Windows ≥32×32px，主要操作 ≥40px |
| 焦点 | 可见焦点环 2px `accent` + 2px 偏移；**禁止** `outline: none` 无替代；Tab 顺序 = 视觉顺序 |
| 键盘 | Windows 快捷键：`Ctrl+Shift+T` 唤出命令条、`Ctrl+Shift+Q` 切画质、`Ctrl+Shift+D` 断开、`F11` 全屏、`Esc` 退出全屏（不断开） |
| 读屏 | 所有图标按钮必须 `AutomationProperties.Name` / `contentDescription`；状态变化用 `liveRegion` / `AccessibilityLiveRegion` 播报（如「已连接」「连接中断，正在重连」） |
| 动效 | 尊重系统「减少动画」；无任何自动播放的循环动画超过 3 次（呼吸点除外，周期 ≥1.5s） |
| 颜色 | 状态必须双通道（颜色 + 图标/文字）；不得用红绿单独区分 |
| 缩放 | Windows 支持 100%–200% 缩放不裁切；Android 支持系统字体放大到 1.3× 不破版（`text.display` 及以上允许换行） |

### 8.2 国际化与文案

- 术语表（三端统一，禁止混用）：

| 概念 | 统一用词 | 禁用 |
|---|---|---|
| 把本机屏幕发给对方 | **投屏** | 共享屏幕、镜像、串流 |
| 接收并显示对方屏幕 | **观看** | 接收、播放 |
| 控制对方设备 | **远控** | 控制、操作、注入 |
| 本机可被其他设备发现 | **可被发现** | 可见性、广播 |
| 局域网内直连 | **局域网直连** | P2P、点对点、直连 |

- 文案结构：`现象 + 原因 + 动作`。例：「无响应（现象）。对方可能已关闭可被发现，或防火墙拦截了 9527 端口（原因）。[重试] [检查防火墙]（动作）」
- 数字与单位间加空格：`42 ms`、`8.2 Mbps`、`1080p 60`。
- 中文标点用全角，英文与数字两侧不加空格（`连接 192.168.1.23` 除外，IP 前后加空格提升可读性）。
- i18n 资源文件：**Android** `res/values/strings.xml`（现有 15 行但零引用，需全面接入）；**Web** `web/i18n/zh-CN.js` + `en-US.js`（纯前端查表）；**Windows 不能用 `.resw`** —— 该项目是**非打包（`WindowsPackageType=None`）+ 关闭 PRI（`GeneratePri=false` / `SkipMakePri=true` / `EnableMrtTargets=false`）**，MRT 资源链不可用。Windows 侧改用 `Resources/i18n/zh-CN.json` 随输出复制，经现有 `LocalizationService`（`Services/LocalizationService.cs`，手写 130 行 `OnPropertyChanged` 列表）改造为字典查表 —— 并把"漏属性就半中半英"的结构性问题一并修掉（现状已漏 8 处，见 X-13 与 Y 系列）。
- 首版只做 zh-CN + en-US 两套；首屏文案必须可切换，不允许"界面中文、状态英文大写"（Android 现状 `LINKED/CONNECTING/ERROR/STANDBY/SCANNING` 硬编码）。

---

## 9. UX 验收标准（U1~U10）

每条可判定、可复现，验收记录格式与 `05` §13 一致。

| 编号 | 判据 | 通过条件 | 验证方式 |
|---|---|---|---|
| **U1** | 品牌一致性 | 三端截图并排，色板取样差异 ≤2 个色值；圆角/间距符合 §3 令牌 | 人工 + 取样脚本 |
| **U2** | 对比度 | §3.1 所有文字/背景组合实测 ≥4.5:1，图形 ≥3:1 | 自动化脚本遍历令牌组合 |
| **U3** | 首屏可用时间 | 冷启动到「可操作」（能点开设备列表）≤3s（中端机/普通 PC） | 录屏逐帧 |
| **U4** | 连接可定位性 | 连接失败时，用户能从界面指出失败阶段（4 阶段点之一） | 5 名用户可用性测试，≥4 人正确 |
| **U5** | 零流量可见性 | 会话页能在 ≤2 次点击内看到「公网连接 0」 | 路径走查 |
| **U6** | 会话 chrome 隐藏 | 2.5s 无操作后命令条完全隐藏，视频区域尺寸不变（像素级对比） | 截图 diff |
| **U7** | 被控授权完整性 | 遮罩含设备名/IP/指纹/范围/两种时效；外部点击不可关闭；撤销 ≤1s | 手工用例 |
| **U8** | 断线恢复 | 拔网线 2s 后恢复，界面保留最后一帧，3 次内重连成功不丢会话 | 手工用例 |
| **U9** | 键盘可达 | 不用鼠标可完成：连接设备 → 开始投屏 → 切画质 → 断开 | 纯键盘走查 |
| **U10** | 文案一致性 | 术语表 5 个词在三端零违规；无硬编码英文残留 | 词表 grep |

> 判据 E（USB 链路）不在 UX 范围，见 `05` §4。

---

## 10. 落地任务与里程碑映射

UI/UX 重构不单独开里程碑，**嵌入既有 M1~M5**，避免与功能并行两套节奏。

| 任务 | 内容 | 依赖 | 落在里程碑 |
|---|---|---|---|
| **UI-00** | 令牌层：`Tokens.xaml` / `Tokens.kt` / `tokens.css` 三端建立，旧常量标注 `@Deprecated` | 无 | M1 前（可并行） |
| **UI-01** | 基础组件：状态点、按钮、输入框、Toast、空状态、骨架屏、错误横幅（三端） | UI-00 | M1 |
| **UI-02** | Windows 壳层重构：NavigationView + W1/W2/W4/W5 重排 + W6 引导 | UI-01 | M1 |
| **UI-03** | Windows 会话窗口 W7（视频舞台 + 命令条 + 统计抽屉 + 边界卡） | UI-01、T03 | M2 |
| **UI-04** | Android 拆分 `ModernMainScreen` → A1/A5 + A2 重做 + A3 投屏准备 | UI-01 | M1 |
| **UI-05** | Android 会话 A6 + 被控授权 A4（含常驻提示、悬浮停止） | UI-01、T04 | M4 |
| **UI-06** | Web X1/X2 重做 + 令牌接入 | UI-00 | M1 |
| **UI-07** | 无障碍补全（焦点环、读屏标签、快捷键、缩放） | UI-02~UI-06 | M5 |
| **UI-08** | 文案与 i18n 抽取（zh-CN / en-US） | UI-02~UI-06 | M5 |

**M1 出口必须包含**：UI-00/01/02/04/06 完成，U1/U2/U3/U10 通过。
**M4 出口必须包含**：UI-05 完成，U7 通过。
**M5 出口必须包含**：UI-07/08 完成，U4/U5/U6/U8/U9 通过。

工作量提示：UI-04 依赖把 834 行的 `ModernMainScreen.kt` 拆开，是 Android 侧最大的一块；建议与 T02 的"双端收发能力补齐"同批做，共用一次重构。

---

## 11. 明确不做（反模式黑名单）

| 不做 | 理由 |
|---|---|
| 引入第三方 UI 框架/组件库（如 WinUI Community Toolkit 的大件、Compose 的第三方设计系统） | 三端已经有三套原生能力，再叠一层会加剧不一致；令牌层足够 |
| 外链 Web 字体（Google Fonts / CDN） | 局域网离线场景会阻塞或失败（§3.2） |
| 玻璃拟态 / 大面积渐变 / 发光边框 | 与工具气质冲突，且在低端机上掉帧 |
| 装饰性"信号格" | 误导用户（X-09） |
| 会话页常驻大工具栏 | 遮挡内容（P1） |
| 动画化视频画面（淡入淡出、缩放缓动） | 每一帧延迟都会被感知（§3.3） |
| 在 P0 做主题色自定义 / Material You 动态取色 | 会破坏品牌一致性，且 Android 12 以下无此能力；列 P2 |
| 深色/浅色之外的手动主题切换器 | 跟随系统即可，多一个开关多一份测试成本 |
| Web 端做远控 | 浏览器注入权限模型不同，复杂度翻倍；P1 再评估 |
| 为"好看"改协议或加字段 | 视觉层不得侵入协议层（前置契约约束） |

---

## 12. 与其他文档的接口

| 文档 | 关系 |
|---|---|
| `03` §2 | 协议契约。本文件所有状态（connecting/connected/degraded）必须能映射到 `SessionState`；如需新状态，先在 `03` 增补，不得在 UI 私造 |
| `03` §5 | 任务分解。UI-00~UI-08 作为 T02/T03/T04/T05 的子任务挂载 |
| `04` | 实现指引。三端令牌与组件的代码级写法见 `04` §13（由 `07` 沉淀） |
| `05` | 测试策略。U1~U10 纳入 M1/M4/M5 出口判据 |
| `07` | 组件级规格（props/状态/三端实现片段），本文件的 §7 是其摘要 |
| `08` | UX 验收执行细则（用例、脚本、报告模板） |

---

## 13. 参考来源

设计决策的外部依据，均为可访问的一手来源。**平台规范优先于个人审美**；竞品做法只作参考，不作标准。

### 13.1 平台规范（强制遵循部分）

| 主题 | 来源 |
|---|---|
| Fluent 2 设计系统总览 | https://fluent2.microsoft.design/ |
| Fluent 2 色彩（含对比度与暗色模式规则） | https://fluent2.microsoft.design/color |
| Fluent 2 排版阶梯（Ramp 12/16 · 14/20 · 16/22 · 20/28 · 28/36） | https://fluent2.microsoft.design/typography |
| Fluent 2 圆角（small 2 / medium 4 / large 6 / xlarge 8） | https://fluent2.microsoft.design/corners |
| Fluent 2 图标（Segoe Fluent Icons） | https://fluent2.microsoft.design/icons |
| Fluent 2 无障碍 | https://fluent2.microsoft.design/accessibility |
| WinUI 3 NavigationView 模式与断点（641 / 1008） | https://learn.microsoft.com/windows/apps/design/controls/navigationview |
| WinUI 3 命令栏（320epx 主命令上限 4 个） | https://learn.microsoft.com/windows/apps/design/controls/command-bar |
| WinUI 3 材质（Mica / 亚克力 / 回退条件） | https://learn.microsoft.com/windows/apps/design/style/mica |
| WinUI 3 间距与圆角资源 | https://learn.microsoft.com/windows/apps/design/style/spacing |
| Material 3 色彩系统与动态取色 | https://m3.material.io/styles/color/system/overview |
| Material 3 排版（15 档） | https://m3.material.io/styles/typography/type-scale-tokens |
| Material 3 形状（圆角刻度） | https://m3.material.io/styles/shape/shape-scale-tokens（**未独立核实，内容已被 M3 色彩/排版页覆盖**） |
| Material 3 自适应布局与窗口尺寸类 | https://developer.android.com/develop/ui/compose/layouts/adaptive |
| Compose `WindowSizeClass`（Compact <600dp / Medium 600–839 / Expanded ≥840） | https://developer.android.com/develop/ui/compose/layouts/adaptive/use-window-size-classes |
| Compose `NavigationSuiteScaffold` | https://developer.android.com/develop/ui/compose/layouts/adaptive/navigationsuitescaffold |
| M3 在 Compose 中的动态取色 / 主题 | https://developer.android.com/develop/ui/compose/designsystems/material3 |
| WCAG 2.2（对比度 1.4.3 / 焦点可见 2.4.11 / 目标尺寸 2.5.8） | https://www.w3.org/TR/WCAG22/ |

### 13.2 竞品做法（参考，非标准）

| 结论 | 来源 |
|---|---|
| 远程工具的信息架构应收敛为「本机身份 + 设备列表 + 会话」三段；ID/密码等连接凭据必须前置可见，不能埋在设置里 | RustDesk 文档 https://rustdesk.com/docs/en/ ；scrcpy https://github.com/Genymobile/scrcpy |
| 会话 chrome 应按「鼠标距顶 0–8px 热区唤出、2–3s 隐藏」实现，工具栏不得常驻占位 | scrcpy 控制文档 https://github.com/Genymobile/scrcpy/blob/master/doc/control.md |
| 被控同意页需给出对端可核对信息（IP、操作系统），并在两端显示同一验证码供人工比对 | Deskreen https://github.com/pavlobu/deskreen ；Samsung Flow 同意页设计 |
| 编码/网络问题常被误判为"软件卡"，必须把采集/编码/发送/接收/解码/渲染分段暴露 | Sunshine 故障排查 https://docs.lizardbyte.dev/projects/sunshine/latest/troubleshooting.html |
| 延迟必须分层（网络/解码/渲染）+ 区分网络丢包与抖动丢包 + 展示 FPS 与帧队列 | Moonlight 性能指标 说明 https://github.com/moonlight-stream/moonlight-docs/wiki/Setup-Guide |
| 低端机软件编码掉帧时，UI 应主动降档并提示，而非静默卡顿 | Sunshine 配置文档 https://docs.lizardbyte.dev/projects/sunshine/latest/configuration.html |
| 无障碍投屏授权（AccessibilityService）必须做分步引导 + 一键跳转系统设置，否则流失极高 | AirDroid 帮助中心「远程控制权限」条目 https://help.airdroid.com/ |
| 首次引导不宜做成多页轮播；每页只讲一件事 + 明确跳过 | TeamViewer QuickSupport 首次启动流程 https://www.teamviewer.com/en/products/remote-access/ |

### 13.3 调研中明确「未找到可核查来源」的项

诚实标注，避免把推测写成事实：

- Material 3 形状令牌的独立页面（`shape-scale-tokens`）未能独立验活，圆角刻度以 M3 色彩/排版页与 Compose 实现为准。
- KDE Connect、AnyDesk 的**具体**同意页文案与布局未取得一手截图来源，故仅作定性参考、未写入任何规格数值。
- 「Material You 动态取色会破坏状态色语义」为工程判断（状态色需稳定可辨），未见官方明文，故在 §11 中列为 P0 不做、P2 再评估，而非引用为规范。

> 所有 §13.1 与 §13.2 链接建议在 `07` 落稿时二次验活；失效链接以平台官方站内搜索替代，不作为设计依据的免责理由。
