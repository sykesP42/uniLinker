# 08 · 三端 UI/UX 验收规范（用例 · 脚本 · 回归基线）

| 项 | 值 |
|---|---|
| 版本 | v1.1（v1.0 2026-11-12 → v1.1 2026-11-14：按 `06` §7.6 补 U11 绝对模式验收，19 条） |
| 日期 | 2026-11-12（v1.0）/ 2026-11-14（v1.1） |
| 状态 | **已生效**（用户选定方案 ② + 不坚持单向角色 + 追加 UI/UX 重构 + v0.3.1 四种绝对模式；v1.1 依据 `06` §7.6 / §7.4.0 / §9 U11 / §10 UI-09） |
| 作者 | QA 工程师 严过关（Yan） |
| **唯一设计依据** | `docs/redesign/06-uiux-redesign.md`（**不修改**；本文只做验收，不做设计）。**v1.1 起四模式章节已在 `06` §7.6 落地**（ModeSelector：§7.6.1 结构与尺寸 / §7.6.2 副行文案硬规则 + 固定文案表 / §7.6.3 不可达态 / §7.6.4 degraded 横幅 / §7.6.5 删除「自适应」开关 / §7.6.6 无障碍与 i18n / §7.6.7 反模式六条），U11 组**直接引 `06`，不再降级到 `README`**；`README` §7.6 C-24 只作为 `06` 的上游依据被引用 |
| 契约依据 | 协议与接口以 `03` §2 为准；本文不新增任何协议字段 |
| 标记体系 | **沿用 `05` §1.2 的 `[A]/[M]/[P]/[X]`**，不另立一套（见本文 §1） |
| 报告格式 | **沿用 `05` §13 风格**（`UX-RPT-*` 为 `MIL-*` 的子报告，见本文 §5.4） |
| 边界 | **只新建本文档**。未修改 `06`/`05`/`03`/`04`，未改 `src/`、`android/`、`web/`、`tests/` 任何源码，未新建测试代码文件，未做 git 操作 |
| 上游追溯 | `00` C1~C5 / 判据 A~F；`01` R0-01~15 / §7 UX-01~22；`03` §6 M0~M8；`06` §7.4.0（预警阈值随模式派生）/ §7.6（模式选择器）/ §8.2（术语表 7 词）/ §9 **U11** / §10 **UI-09**；`README` §7.5 C-18~C-23 / §7.6 C-24（`06` §7.6 的上游）；`05` §5 TC-001~121（功能侧，含 v1.1 的 F 组）、§11.2（出口条件） |

## 0. 这份文档解决什么问题

`06` 给出了设计规格（1 套设计语言 + 15 个界面 + 6 条流程），但它**不含验收手段**；`05` 覆盖的是功能与合规判据，UX 部分只有 §4.7 的 W-01~W-14 人工走查，粒度到不了「三端色值是否真的一致」「Start Share 是不是还在撒谎」。本文补这一层：

**追溯链**：`06`（设计规格）→ **`08`（本文：可执行验收）** → `07`/T06/T07（实现）→ `UX-RPT`（证据）；横向挂 `05` §5（功能用例 `TC-*`）与 §13（报告体系）。

**三条硬规则**（与 `05` 同源，违反即报告作废）：

1. **标记必须真实**：跑不了的脚本标 `[M]`，缺基础设施的标 `[X]`；**禁止把人工判定硬标成 `[A]`**。
2. **通过标准必须有阈值**：禁止「显示正常」「符合设计」这类无量化表述（`05` §1.4）。
3. **`[P]` 只能得出「降级提示已验证（非功能通过）」**，`06` §8/§2 中平台不可干预项（系统栏图标、厂商 ROM 通知栏、`FLAG_SECURE`）一律按此处理，严禁写「功能通过」。

## 1. 标记体系与本文增量规则

### 1.1 四标记（沿用 `05` §1.2，不改语义）

| 标记 | 含义 | 本文允许的结论 |
|---|---|---|
| **[A]** 可自动化 | 脚本/CI/E2E/instrumentation 可给布尔或量化结果 | 「通过 / 不通过」，附脚本名 + 输出文件 |
| **[M]** 需人工走查 | 必须真机人工操作或主观判定 | 「走查通过（附截图号/签字）/ 不通过」 |
| **[P]** 平台限制不可测通过 | 平台不允许第三方干预，只能验「降级提示」 | 「降级提示已验证（非功能通过）」 |
| **[X]** 当前不可达 | 依赖尚未实现的能力或未定义令牌 | 「待 <里程碑> 解除阻塞」+ 阻塞原因 |

### 1.2 本文增量规则（仅 UX 侧，不覆盖 `05`）

| # | 规则 | 说明 |
|---|---|---|
| R1 | **截图即证据，命名强制** | `UX-<端点>-<用例号>-<YYYYMMDD>-<序号>.png`，端点取 `WIN`/`AND`/`WEB`。无此命名不算证据 |
| R2 | **同屏截图必须同参数** | 同一用例的三端对比截图：同分辨率（或等比缩放后标注）、同主题（亮/暗）、同字体缩放（默认 1.0）、同 DPI 档、同静止帧内容（用 E-12 测试图卡） |
| R3 | **像素类断言必须先冻结画面** | 视频区域做像素差断言时，桌面/画面必须是**静止**内容（否则「差值为 0」不成立）。用 `05` §12 E-12 标准测试图卡，禁用动态桌面与光标闪烁 |
| R4 | **色值断言只认取样均值** | 单像素受抗锯齿/次像素渲染影响，一律取 7×7 patch 均值（本文脚本 S2 已实现） |
| R5 | **「令牌已落地」的静态门禁** | 业务代码出现裸十六进制色值或裸字号即判不通过（`06` §3.3 令牌制），例外须在 `06` §3.3 白名单登记 |
| R6 | **`06` 未定义的令牌不算实现缺陷** | 例如浅色主题缺 `success/warning/danger/focus-ring`（见 §3.7 发现 F-2），此类先开文档缺陷（`DOC-*`），不判实现 Blocker |
| **R7** | **模式类断言必须「可见文案 + 真实生效值」双证**（v1.1 新增，出处 `06` §7.6.2 / §9 U11） | 只验文案（副行写了「锁定帧率」）不验生效（实测帧率/分辨率/QP 真变了）= **不通过**。判据两条缺一即失败：①用户能看见「承诺了什么」；②系统真正做到了什么（对齐 PR-7「界面指标不得伪造」）。模式用例的证据必须同时含截图与实测采样 CSV；文案比对的**唯一基准是 `06` §7.6.2 的固定文案表（中/EN 两列）**，本文不复述、不改写 |

## 2. A · UX 用例矩阵（UX-TC-001 ~ UX-TC-079）

### 2.0 总览与分布

| 分组 | 主题（`06` 章） | 用例区间 | 条数 | [A] | [M] | [P] | [X] |
|---|---|---|---:|---:|---:|---:|---:|
| **U1** | 品牌一致性（§2.1~§2.3） | UX-TC-001~006 | 6 | 5 | 1 | 0 | 0 |
| **U2** | 对比度与可读性（§3.1、§8.1） | UX-TC-007~014 | 8 | 7 | 1 | 0 | 0 |
| **U3** | 首屏时间（§4.1） | UX-TC-015~018 | 4 | 3 | 1 | 0 | 0 |
| **U4** | 连接可定位（§6 F2、§4.2） | UX-TC-019~026 | 8 | 6 | 2 | 0 | 0 |
| **U5** | 零流量可见（§4.3、§5.1） | UX-TC-027~030 | 4 | 4 | 0 | 0 | 0 |
| **U6** | chrome 自动隐藏（§5.1、§6 F4） | UX-TC-031~035 | 5 | 4 | 1 | 0 | 0 |
| **U7** | 被控授权（§6 F5、§5.3） | UX-TC-036~043 | 8 | 7 | 1 | 0 | 0 |
| **U8** | 断线恢复（§6 F6） | UX-TC-044~050 | 7 | 6 | 1 | 0 | 0 |
| **U9** | 键盘可达（§8.1） | UX-TC-051~055 | 5 | 1 | 4 | 0 | 0 |
| **U10** | 文案一致（§2.3、§8.2） | UX-TC-056~060 | 5 | 5 | 0 | 0 | 0 |
| **U11**（v1.1 新增） | **绝对模式：帧率优先/画质优先/均衡/节能**（`06` §7.6 / §7.4.0 / §9 U11；任务 UI-09） | UX-TC-061~079 | 19 | 15 | 4 | 0 | 0 |
| **合计** | — | **001~079** | **79** | **63** | **16** | **0** | **0** |

> **[P]/[X] 均为 0 条，这是实测结论不是遗漏**：U 组用例全部落在「可自动化」或「需人眼但可执行」两侧。真正的平台不可干预项（系统栏图标、厂商 ROM 通知栏折叠、`FLAG_SECURE`）在 `05` §4.7 **W-12** 与本文 §6 **B-3** 处理，不重复计入本矩阵。

> **[M] 占比 20.3%（16/79）不是偷懒**：`tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj:12-19` 实测只引 `Microsoft.Playwright 1.48.0` / `xunit 2.9.2` / `FluentAssertions 7.0.0`，**没有 FlaUI / WinAppDriver / UIAutomation 依赖**，因此「Windows 端键盘遍历、焦点环、原生控件视觉规格」这一类当前**无自动化基础设施**。Web 端（Playwright）与 Android 端（`adb` + `uiautomator dump`）可自动化的均已标 `[A]`。若要提高 [A] 占比需引入 FlaUI，属新增依赖，不在本文授权范围（见 §3.7 发现 F-3）。

### 2.1 U1 · 品牌一致性（6 条）

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-001 | U1；`06` §2.1/§2.2/§2.3 | 三端 | 三端均在**深色**主题、默认字体缩放、同一测试图卡；会话页静止 | 1. 三端各截会话页整图；2. 按 §B2 脚本取 6 个令牌坐标（canvas/surface/surface-raised/accent/text-primary/border）；3. 逐令牌跨端比对 | 每个令牌三端取样**两两 ΔE(CIE76) ≤ 2** 或 **RGB 各通道差 ≤ 4**；6 个令牌 × 3 对 = **18 组比对全通过** | [A] | X-01、X-21 |
| UX-TC-002 | U1；`06` §2.1 圆角表 | 三端 | 同 TC-001 | 1. 截图量自绘卡片外圆角（对角像素步进法）；2. 与 `06` §2.1 令牌值比对；3. 系统控件（Button/InfoBar/ScrollBar）单独记录是否保留 `OverlayCornerRadius` | 自绘卡片圆角 = **12px@Win / 12dp@And / 12px@Web**，误差 ≤1px；自绘按钮 = 8；系统控件圆角**允许不同**但在报告中列明，不得为「统一圆角」重写系统模板 | [A] | X-02 |
| UX-TC-003 | U1；`06` §3.3 令牌制 | 三端 | 源码可读（只读，不改） | 1. `grep -rnE '#[0-9A-Fa-f]{6}\b'` 扫 `src/UniLinker.WinUI/**/*.xaml`、`src/UniLinker.WinUI/**/*.cs`、`android/**/*.kt`、`web/*.css`、`web/*.js`；2. 排除主题令牌定义文件与 `06` §3.3 白名单；3. 统计业务文件命中数 | 业务文件中裸十六进制色值命中 **== 0**；白名单外每 1 处 = 1 条 Major | [A] | X-01、X-21 |
| UX-TC-004 | U1；`06` §2.1 字体 | 三端 | 同 TC-001 | 1. 断言数值文本（延迟/码率/帧率）使用等宽数字（`font-variant-numeric: tabular-nums` / `FontFeatureTag` / `FontFeature`）；2. 断言无外链 Web 字体请求 | 数值跳动时**字符宽度不变**（同一元素前后两帧文字包围盒宽度差 ≤1px）；`0` 个外链字体域名（与判据 D 的 DNS 零外网一致） | [A] | X-04、X-08 |
| UX-TC-005 | U1；`06` §2.1 语义色、§8.1 双通道 | 三端 | 构造 connected / degraded / error 三态 | 1. 三态各截 1 图；2. 人工核对「颜色 + 图标 + 文字」三者是否同时表达状态；3. 灰度化截图后再判读一次（色盲模拟） | 三态在**灰度图**中仍可区分（形状或文字不同）；**单独靠颜色区分 = 不通过**（WCAG 1.4.1） | [M] | X-09、X-14 |
| UX-TC-006 | U1；`06` §2.3、X-19 | Web | 页面可访问 | 1. 扫 `web/index.html`、`web/app.js` 中的 emoji / 图标字体字符；2. 统计作为功能图标使用的 emoji 数 | 功能图标的 emoji 使用数 **== 0**（统一用 SVG/图标集）；正文内 emoji 允许但 ≤0 处作为按钮语义 | [A] | X-19 |

### 2.2 U2 · 对比度与可读性（8 条）

> **前置工具**：§B1 脚本 `contrast_audit.py`。所有数值以脚本输出为准，`06` §3.1 标注值为设计期核算值（两者差异见 §3.7 发现 F-1）。
> **F-1 状态更新（2026-11-12，C-17）**：`06` §3.1 已按本脚本实测结果修订色板（深/浅 `text-tertiary`、浅色 `accent`、新增 `border-control`），详见 `06` §3.1.1。**本文件 §3.7 与 TC-008 记录的 4.29 / 3.82 / 4.28 / 4.45 为修订前实测值，属历史证据，保留不改**；复跑 `S1` 应以 `06` §3.1 现值输入，期望违例归零。若仍非零，按缺陷提级处理。

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-007 | U2；`06` §3.1 深色表、§8.1 | 三端 | 深色主题 | 1. 跑 `python3 tools/ux/contrast_audit.py --gate`；2. 读「DARK · 文字」段 | `06` §3.1 声明的**全部深色文字/背景配对 ≥ 4.5:1**；脚本退出码 0；违例逐条开 Major | [A] | X-01 |
| UX-TC-008 | U2；`06` §3.1 浅色表 | 三端 | 浅色主题 | 同 TC-007，读「LIGHT · 文字」段 | 声明的浅色配对 ≥ 4.5:1。**`S1` 已实跑，已知 3 组预期 FAIL**：`text-tertiary #75726B` on `canvas` = **4.29**、on `surface-sunken` = **3.82**；`accent #8A6A42` on `canvas` = **4.45** → T07 前必须修正令牌或限定使用场景（§3.7 F-1） | [A] | X-06 |
| UX-TC-009 | U2；`06` §8.1 非文字 ≥3:1 | 三端 | 深色/浅色各一遍 | 同脚本「非文字」段：`border-strong`/`focus-ring` vs 相邻背景 | 输入框描边、焦点环、状态图形 ≥ **3:1**。**`S1` 已实跑，4 组预期 FAIL**：深色 `border-strong #3A3A34` on `surface` = **1.49**、on `surface-raised` = **1.33**；浅色 `border-strong #C9C4BA` on `surface`/`surface-raised` = **1.74**（需提亮才达 3:1） | [A] | X-01 |
| UX-TC-010 | U2；`06` §8.1 焦点环 | Win / Web | 深色主题 | 1. 用 Tab 把焦点移到首个交互元素；2. 截取焦点元素 ±12px 区域；3. 统计 accent 像素是否形成完整 2px 闭环 | 焦点环 **2px、offset 2px、四边不被父容器裁切**（四方向各至少 2px 连续 accent 像素）；被 `overflow:hidden` 裁切 = Major | [M] | — |
| UX-TC-011 | U2；`06` §2.1 语义色 | 三端 | 三态可构造 | 用脚本对 success/warning/danger/on-accent 与其承载背景求比 | 状态文本 + 图标 ≥ 4.5:1；`on-accent` 在 `accent` 底上 ≥ 4.5:1（预核算 `#1A1A1A` on `#C49A6C` = **6.78:1** ✓） | [A] | — |
| UX-TC-012 | U2；WCAG 1.4.3 大字例外 | — | — | 1. 从三端样式表提取所有字号 + 字重；2. 对 ≥24px（或 ≥18.66px 且 bold）的文本套用 3:1 阈值 | 该阈值**只允许用于标题与 KPI 数字**；正文/说明/按钮文案误用大字例外 = Major | [A] | — |
| UX-TC-013 | U2；`06` §3.1 浅色 accent 注 | Web / And | 浅色主题 | 1. 在浅色主题下找所有使用 `accent` 作为**文字色**的位置；2. 取样与背景求比 | 浅色下 `#C49A6C`（白底预核算 **2.57:1**，`06` 记 2.4:1）**禁止作文字色**，仅可作填充/描边；若出现文字色用法 = Major | [A] | X-01 |
| UX-TC-014 | U2；`06` §5.1 视频舞台 | 三端 | 会话中，亮/暗主题各一遍 | 1. 切换系统主题；2. 取样视频舞台四周留白与指挥条背景 | 视频舞台固定 `surface-sunken`，**不随主题变化**（两次取样 ΔE ≤ 2）；信箱黑边区域像素为纯黑或舞台底色（RGB 各通道 ≤ 8，避免灰边误判为画面） | [A] | X-06 |

### 2.3 U3 · 首屏时间（4 条）

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-015 | U3；`06` §4.1 | Win | 冷启动（进程未驻留）；录屏 60fps | 1. `taskkill /IM UniLinker.WinUI.exe /F` 后启动；2. 逐帧找「首帧可交互」帧；3. 与进程启动时间戳求差 | **冷启动 → 可操作 ≤ 3000ms**；骨架屏在 **≤ 300ms** 出现；全程**无 >1 帧的纯白/纯透明空窗** | [A] | X-20 |
| UX-TC-016 | U3；`06` §4.1 | And | 中端真机（**非模拟器**）、冷启动 | 1. `adb shell am force-stop com.unilinker.android`；2. `adb shell am start -W -n com.unilinker.android/.MainActivity`；3. 取 `TotalTime`/`WaitTime` | `TotalTime ≤ 3000ms`（中端机）；首帧为骨架屏而非空白；**分辨率/包名实测**：`applicationId = com.unilinker.android`（`android/app/build.gradle.kts:12`）、`.MainActivity`（`AndroidManifest.xml:26`） | [A] | X-20 |
| UX-TC-017 | U3；`06` §4.1 骨架屏 | 三端 | 冷启动 | 1. 截启动后第 1 帧、第 300ms 帧、第 3000ms 帧；2. 与前一次启动的第 1 帧比对 | 首帧背景取样 = `canvas`（ΔE ≤ 2），**不是白色**；骨架块数量与最终布局区块数一致（不允许「先空后跳」） | [A] | X-20、X-12 |
| UX-TC-018 | U3；`06` §4.1 自检不阻塞 | Win | 需可临时阻断自检目标（防火墙/网卡） | 1. 阻断自检依赖（如禁用网卡或加防火墙规则使自检超时）；2. 冷启动计时 | 自检 **2s 超时**后界面**照常可操作**（不冻结、不弹模态）；出现黄条降级提示 + 可点「重新自检」；超时期间点击任何按钮有响应（≤200ms 出现视觉反馈） | [M] | — |

### 2.4 U4 · 连接可定位（8 条，含四阶段逐段注入失败）

> 依据 `06` §6 F2 四阶段：① 发现 → ② 选中/握手 → ③ 信令/ICE → ④ 首帧。**每阶段必须单独注入失败**，因为现实中最常见的 bug 是「四阶段共用一句『连接失败』」。

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-019 | U4-① 发现失败 | And / Win | 同 LAN 无任何可发现端 | 1. 关闭对端；2. 进入发现页等待 15s | 15s 后进入**空状态**（非无限骨架）；空状态含**3 个排障入口**（检查同网段 / 手动输入 IP / 重试）；文案含「未发现设备」+ 本机 IP 显示 | [A] | Y-11、X-12 |
| UX-TC-020 | U4-② 握手超时 | And | 对端在线但拒绝/不响应 | 1. 点击设备卡；2. 3s 内无响应 | 设备卡**就地**显示红色副文案（非全局 toast）+ 卡内「重试」按钮；其他设备卡不受影响（可继续点） | [A] | — |
| UX-TC-021 | U4-③ 信令失败 | And / Win | 9527 端口不可达（对端防火墙拦截） | 1. 阻断 9527；2. 发起连接 | 阶段进度条**第 2 个点变红**；提示文案指向「对方未监听/端口被拦」；**不得**显示「网络不可用」（会误导用户查 Wi-Fi） | [A] | X-14 |
| UX-TC-022 | U4-③ ICE 失败 | And / Win | 信令通、媒体不通（阻断 UDP 或不同网段） | 1. 放行 TCP 9527、阻断媒体端口；2. 发起连接 | **第 3 个点变红**；文案明确「已建立信令但媒体不通」+ 提示「同一网段 / 检查防火墙 / 关闭 AP 隔离」；`iceConnectionState` 实测值写入诊断面板 | [A] | — |
| UX-TC-023 | U4-④ 无首帧 | 三端 | 信令与 ICE 均通、无视频帧 | 1. 正常建连但对端不发送；2. 等待 5s | 5s 后显示「**已连接但无画面**」（明确区别于「连接失败」）+ 3 项排查（对方是否点了开始共享 / 授权是否被拒 / 编码器是否降级），并显示 `readyState` 与 `frameCount=0` | [A] | Y-09、X-14 |
| UX-TC-024 | U4 阶段视觉规格 | 三端 | 四阶段各构造一遍 | 1. 逐阶段截图；2. 核对点的数量、状态色、图标、文字 | 四阶段**点数固定为 4**；状态色只用 `text-tertiary`/`accent`/`danger` 三档；**每档同时有图标 + 文字**（灰度可辨）；阶段名与 `06` §6 F2 完全一致 | [M] | X-14 |
| UX-TC-025 | U4 判据本体（可定位性） | 三端 | 5 名未参与项目的用户 | 1. 每人在不看文档的情况下完成 4 次「故障→定位」任务（每阶段 1 次，随机顺序）；2. 记录「指出正确阶段」的次数与用时 | **≥ 4/5 用户**在 **≤ 20s** 内正确指出「问题出在哪一阶段」；共 20 次任务，**≥ 16 次**正确；错误答案集中在同一阶段则判该阶段文案不通过 | [M] | — |
| UX-TC-026 | U4 文案三件套 | 三端 | 文案字典可读（只读） | 1. 抽取所有连接类文案；2. 逐条核对是否含「现象 + 原因 + 下一步动作」 | 每条失败文案**三要素齐全**（缺一 = Minor，缺两项 = Major）；禁止出现「连接失败，请重试」这类无原因无动作的单句 | [A] | Y-15 |

### 2.5 U5 · 零流量可见（4 条）

> 依据 `06` §4.3「把零流量从承诺变成可见证据」、§5.1 边界卡。判据 D 的**技术验证**在 `05` §4.4，本节只验**用户能否看见并核对**。

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-027 | U5；`06` §4.3 | 三端 | 会话进行中 | 1. 从会话页开始数点击次数，直到看到「公网连接 0」 | **≤ 2 次点击**即可看到；达到 3 次以上 = Major（可见性不达标） | [A] | Y-02 |
| UX-TC-028 | U5 数据真实性 | 三端 | 会话中，同时开抓包 | 1. 记录界面「本机/对端地址、已传输字节、公网连接数」；2. 与 `05` §4.4 抓包结果、`/info` 接口数据三方比对 | 界面数值与抓包**一致**：已传输字节偏差 ≤ 2%（允许采样时刻差）；公网连接数与抓包公网目的 IP 计数**完全相等**；地址显示为私网 IPv4/IPv6 字面量（不含域名） | [A] | Y-02、X-09 |
| UX-TC-029 | U5 负向告警 | 三端 | 可注入一条非私网目的地址 | 1. 注入（如临时配置一个公网 STUN）；2. 观察边界卡 | 边界卡**变红** + 写明「检测到 N 个非私网目的地址」+ 可展开明细；**不得**只变色不给数量（不可核对 = 不通过） | [A] | — |
| UX-TC-030 | U5 禁止无证据声明 | 三端 | 文案可读 | 1. `grep` 全量文案中的「不联网 / 不上传 / 无服务器 / 端到端加密」类声明；2. 每条找对应可核对 UI 或报告 | 每条声明**必须有可核对的入口**（边界卡/报告/抓包号），否则删除；「无证据声明」按 Blocker 处理（触碰 PR-1 红线） | [A] | Y-02 |

### 2.6 U6 · chrome 自动隐藏（5 条）

> 依据 `06` §5.1（2.5s 静止隐藏、8px 顶边热区、24px 常驻状态条）、§6 F4。

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-031 | U6 自动隐藏 | 三端 | **画面冻结**（E-12 静止图卡），无鼠标/触摸输入 | 1. t=0 截「命令条可见」帧；2. 静置 2.5s 后截「隐藏」帧；3. 用 §B3 脚本对视频区域与非视频区域分别求 AE | **视频区域 AE == 0**（overlay 不占位、不改变画面布局）；**顶部命令条区域 AE > 0**（确实隐藏了）；隐藏耗时 **2.5s ± 200ms** | [A] | — |
| UX-TC-032 | U6 唤出 | 三端 | TC-031 隐藏后 | 1. 鼠标移到顶边 8px 内 / 手指下滑 8dp；2. 另测键盘 `Ctrl+Shift+T` | 三种方式都能唤出；唤出延迟 **≤ 200ms**；唤出后 2.5s 无输入再次隐藏 | [A] | — |
| UX-TC-033 | U6 不重排 | 三端 | 会话中 | 1. 记录命令条显隐两态下视频元素/画面区域 bounds；2. 比对 | 视频区域 bounds **两态完全一致**（宽高与 x/y 变化 == 0）；**不允许**「隐藏时视频变大」（那会让码率/坐标映射在会话中途失效） | [A] | — |
| UX-TC-034 | U6 常驻状态条 | Win | 会话中 | 1. 截图；2. 量顶部/底部常驻条高度与遮挡关系 | 24px 状态条**常驻不隐藏**，且**完全不覆盖**视频区域（与视频 bounds 无交集）；状态文字（延迟/码率）每秒刷新且不引起布局抖动 | [A] | — |
| UX-TC-035 | U6 移动端手势边界 | And | 会话中 | 1. 顶部下滑唤出；2. 视频中部下滑；3. 双击 | 顶部下滑 = 唤出命令条；中部下滑 = **不唤出**（避免误触）；双击 = 播放/暂停或无副作用；三者在 2.5s 内均可再隐藏 | [M] | — |

### 2.7 U7 · 被控授权（8 条）

> 依据 `06` §6 F5（四项可核对信息 + 默认仅观看 + 不可点外关闭 + 二次确认）、`05` §7（授权与安全专项）。

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-036 | U7 信息完备 | And | 收到控制请求 | 1. 截授权遮罩；2. 逐项核对 4 项信息 | **四项齐全**：设备名+平台图标 / 对端 IP / **六位核对码** / 操作范围说明；缺任一项 = Blocker（用户无法判断在对谁授权） | [A] | — |
| UX-TC-037 | U7 最小权限默认 | And | 同上 | 1. 打开遮罩不做任何操作；2. 读默认选中项 | 默认选中「**仅观看**」；「允许控制」必须由用户主动点击（默认勾选 = Blocker，违反 `05` §7 最小授权） | [A] | — |
| UX-TC-038 | U7 不可误关 | And | 同上 | 1. 点击遮罩外部；2. 按返回键 | 两种操作**均不关闭**遮罩、不产生默认决策；返回键 = 折叠为通知/保持遮罩（二者择一但必须显式且在 `06` §6 F5 记录） | [A] | — |
| UX-TC-039 | U7 「始终允许」治理 | And | 同上 | 1. 点「始终允许」；2. 走二次确认；3. 到设置页清除 | 二次确认文案含「对方可在你不在场时操作本机」；设置页存在**一键清除**入口；清除后下次必再弹（清除无效 = Blocker） | [A] | — |
| UX-TC-040 | U7 拒绝闭环 | And + 对端 | 同上 | 1. 点「拒绝」；2. 观察两端 | 本端记录「已拒绝」并可回看；**发送端收到明确原因**（不是静默超时）；两端文案一致指向「对方拒绝了控制请求」 | [A] | Y-15 |
| UX-TC-041 | U7 撤销 ≤1s | And | 已授权、正在被控 | 1. 计时起点 = 点「停止控制」；2. 终点 = 对端下一次注入被拒（服务端日志/对端提示）；3. 重复 20 次取最大值 | **每次 ≤ 1000ms**，20/20 通过（取最大值而非均值，`05` §6 同口径）；超时 1 次即 Blocker | [A] | — |
| UX-TC-042 | U7 常驻可见提示 | And | 正在被控 | 1. 观察屏幕边缘；2. 拉通知栏；3. 全屏应用内查看 | **三处同时可感知**：2px 呼吸边框 / 通知栏常驻项（含停止按钮）/ 悬浮停止按钮；任一处缺失 = Major（用户无法随时发现被控） | [M] | — |
| UX-TC-043 | U7 核对码语义 | And | 收到请求 | 1. 比对两端六位码；2. 用错误码尝试确认 | 两端**显示同一码**；码由 `sessionKey` 派生且**不参与认证**（改码不应改变鉴权结果）；码刷新与会话绑定（重连后变化） | [A] | — |

### 2.8 U8 · 断线恢复（7 条）

> 依据 `06` §6 F6（0-0.5s 保留最后一帧、指数退避 0.5/1/2/4/8、5 次失败给 4 类原因、上下文保留 60s）。

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-044 | U8 不黑屏 | 三端 | 会话中，注入断链 | 1. 断链；2. 逐帧检查 0~0.5s | 0~500ms 内**保留最后一帧**（画面像素 AE ≈ 0，仅叠加半透明遮罩）；**不得**出现纯黑/纯白帧 | [A] | — |
| UX-TC-045 | U8 退避序列 | 三端 | 同上 | 1. 断链后持续阻断；2. 记录每次重试时间戳 | 重试间隔严格为 **0.5 / 1 / 2 / 4 / 8s**（误差 ≤ ±15%）；界面显示「第 N 次重连中」（N 递增可见） | [A] | — |
| UX-TC-046 | U8 恢复收敛 | 三端 | 阻断 3 次后放通 | 1. 放通网络；2. 观察提示条 | 重连成功后黄条**变绿并在 1.5s ± 300ms 内消失**；重试计数归零；**画面自动恢复**（无需用户点击） | [A] | Y-15 |
| UX-TC-047 | U8 终态 4 类原因 | 三端 | 连续失败 5 次 | 1. 保持阻断至第 5 次失败；2. 截中央卡片 | 卡片给出 **4 类原因**（网络已断 / 对方已关闭共享 / 授权失效 / 端口不可达）并**高亮最可能的一类**；含「立即重连」「结束会话」两个动作 | [A] | X-14 |
| UX-TC-048 | U8 四阶段注入落点 | 三端 | 可控注入 | 1. 分别在 ① 网络切换 ② 对方关闭 ③ 超时 ④ 认证失效 四种成因下断链；2. 记录 UI 落点 | 四种成因**落到各自不同分支**（文案 + 图标 + 动作不完全相同）；四种都落到同一句「连接失败」= Major | [M] | Y-15 |
| UX-TC-049 | U8 逃生通道 | 三端 | 重连中 | 1. 观察是否始终可点「立即重连/结束」；2. 点击计时 | 两按钮**在所有重连阶段常驻可点**；点击响应 ≤ 200ms；结束会话不残留半透明遮罩 | [A] | — |
| UX-TC-050 | U8 上下文释放 | 三端 | 结束会话后 | 1. 结束后静置 60s；2. 检查媒体/解码器/授权状态；3. 再发起新会话 | 60s 后会话上下文**彻底释放**（解码器实例数归零、`AuthState` 回 `IDLE`）；新会话不继承旧状态（继承 = Major，可能是 `addSink` 无 `removeSink` 类泄漏，`05` §14 RK-8） | [A] | — |

### 2.9 U9 · 键盘可达（5 条）

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-051 | U9；WCAG 2.1.1 | Win | 无鼠标 | 1. 仅用 Tab/Enter/Space/Esc 完成「连接 → 开始共享 → 切画质 → 断开」；2. 全程录屏 | 四步任务**全部可完成**，无死锁焦点；Esc **不断开会话**（只退出全屏/关闭浮层） | [M] | — |
| UX-TC-052 | U9；WCAG 2.4.7 | Win | 同上 | 1. Tab 遍历全部交互元素；2. 每次停焦截图 | 每个可交互元素停焦时**焦点环可见且四边不裁切**（2px 闭环，同 TC-010）；无「焦点不可见」元素 | [M] | — |
| UX-TC-053 | U9；WCAG 2.4.3 顺序 | Win | 同上 | 1. 记录 Tab 序列；2. 与视觉阅读顺序（自上而下自左而右）比对 | Tab 顺序 == 视觉顺序，**逆序/跳序 0 处**；模态（授权/确认）内焦点**限制在模态内** | [M] | — |
| UX-TC-054 | U9 快捷键 | Win | 同上 | 1. 逐个按 `06` §8.1 定义的快捷键；2. 记录行为 | 5 个快捷键全部生效且与菜单标注一致；快捷键**不与系统快捷键冲突**（Alt+F4 / Win+方向 除外）；每条可在菜单/提示中找到说明 | [M] | — |
| UX-TC-055 | U9 读屏标签 | Web | Playwright + 无障碍树 | 1. `page.accessibility.snapshot()`；2. 统计无名称的可交互节点 | **无名称的可交互节点 == 0**；图标按钮均有 `aria-label`；状态变更区域有 `aria-live`（连接状态变化能播报） | [A] | — |

### 2.10 U10 · 文案一致（5 条）

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-056 | U10；`06` §8.2 术语表 | 三端 | 文案可读 | 1. 按 `06` §8.2 的 5 个统一词（如「被控」「控制」「共享」「会话」「观看」）扫全量文案；2. 找同义异词 | 每个统一词的同义异词命中 **== 0**（如出现「远程协助/操控」替代「被控」即 Minor，≥3 处 = Major） | [A] | — |
| UX-TC-057 | U10；X-13 | 三端 | 文案可读 | 1. `grep -nw -e LINKED -e CONNECTING -e ERROR -e STANDBY -e SCANNING -e READY` 扫三端 UI 文案；2. 排除日志与注释 | 用户可见区域硬编码英文**命中 == 0**；日志与开发者面板允许但须与 UI 文案分离 | [A] | X-13、Y-15 |
| UX-TC-058 | U10 数字与单位 | 三端 | 会话中 | 1. 提取全部「数值+单位」文本；2. 对照规范 | 统一格式：`42 ms`（数字与单位间 1 空格）、`8.2 Mbps`、`1080p 60`、`10:32`（时长）；不一致处 ≤0 | [A] | X-13 |
| UX-TC-059 | U10 中英切换 | 三端 | 系统语言切到 en-US | 1. 切系统语言；2. 首屏截图；3. 找半中半英 | 首屏**无半中半英**混排（同一句内混用 = Major）；未翻译项显示英文而非中文残留；布局不溢出（en 文案更长时按钮不换行错位） | [A] | — |
| UX-TC-060 | U10 错误文案结构 | 三端 | 构造 ≥5 类错误 | 1. 逐类触发；2. 按三件套核对 | 每条文案含「现象 + 原因 + 动作」三要素（同 TC-026 口径）；**禁止**技术错误码直出（`WebRTC error 0x2A` 类必须转人话 + 可展开原文） | [A] | Y-15 |

### 2.11 U11 · 绝对模式（19 条，v1.1 新增；判据挂 `06` §9 **U11**）

> **依据**：`06` **§7.6 模式选择器**（§7.6.1 结构规格 / §7.6.2 副行文案硬规则与固定文案表 / §7.6.3 不可达态 / §7.6.4 degraded 横幅 / §7.6.5 删除「自适应」开关 / §7.6.6 无障碍与 i18n / §7.6.7 反模式六条）、`06` **§7.4.0**（预警阈值随模式派生）、`06` **§9 U11**（模式契约可验证，含反向判据）、`06` **§10 UI-09**（四模式选择器与模式契约的界面表达，M2 出口）。上游为 `README` §7.6 C-24。
> **判据挂法**：UX-TC-062/065 挂 U11①（显示的不变量与实测一致）；066/067 挂 U11②（不可达前置置灰 + 原因 + 出路）；068/069/077 挂 U11③（degraded 横幅写明取舍）；076 挂 U11④（无静默降级）；061/063/070/071/074/075/078 挂 §7.6.x 与 §7.6.7 反模式。
> **一处文档不一致（记录，不改 `06`）**：`06` §9 标题仍写「UX 验收标准（U1~U10）」，但表内已含 **U11**（`06` 第 888 行）。本文按表内实际编号引 U11，标题差异已报主理人。
> **与 `05` 的分工**：本文只验「用户能否看见并理解承诺、界面有没有撒谎」；量化不变量（帧率/分辨率/QP/功耗）由 `05` **TC-102~TC-113** 判定。两组一一对应，**任一组不过即该模式不成立**。
> **通用前置 U11-P0**：会话可建立（QG-1 已通过）、设置页/会话页可进入、测试内容为 E-12 图卡 + 滚动文本、可读取实测帧率与分辨率（`05` §8.5 口径）。

| 用例号 | 对应判据 | 端 | 前置条件 | 步骤 | 期望结果 | 标记 | 关联缺陷 |
|---|---|---|---|---|---|---|---|
| UX-TC-061 | U11；`06` §7.6 / §7.6.1 | 三端 | U11-P0 | 1. 打开模式选择器；2. 逐项点选；3. 重启应用后再看 | **恰好四项**（帧率优先 / 画质优先 / 均衡 / 节能）、**单选互斥**、**默认选中「均衡」**；重启后保持上次选择（若为默认则仍为均衡）；**不得**出现第五项或「自定义」占位 | [A] | — |
| UX-TC-062 | U11①；`06` §7.6.2 | 三端 | U11-P0 | 1. 截图选择器（中/EN 各一次）；2. 逐行取副行原文；3. **与 `06` §7.6.2 固定文案表逐字比对**（中文四行、英文四行，共 8 条） | 八条文案与本表**逐字一致**（只允许翻译，不允许改写）：帧率优先「锁定帧率，画质随网络自动取舍」/ EN `Locks frame rate; quality yields to the network`；均衡「各项都保持在可感阈值以上」/ `Keeps every dimension above perceptible loss`；画质优先「锁定原生分辨率与画质，帧率随网络取舍」/ `Locks native resolution and quality; frame rate yields`；节能「静态画面不重发，功耗最低」/ `Static screens are not re-sent; lowest power`。**出现「优秀/高清/超清/极致/丝滑」任一形容词 = 直接不通过**（§7.6.2 第 3 条）；缺副行 = 不通过 | [A] | Y-09 |
| UX-TC-063 | §7.6.5 控件收敛 / §7.6.1（只留设定值） | 三端 | U11-P0 | 1. 打开设置/模式区；2. 不展开任何折叠区；3. 找分辨率/帧率/码率三个滑杆 | 三个滑杆**只存在于「高级」折叠区**，默认收起；主界面首屏**一个滑杆都不出现**；展开后调滑杆必须提示「已脱离模式管理」并标明当前生效的是哪一方 | [A] | — |
| UX-TC-064 | U11①；切换生效时延 | 三端 | U11-P0 + 可读 `MediaCapability` | 1. 从均衡切到帧率优先；2. 秒表计时；3. 抓 `MediaCapability` 更新与实测值变化 | 切换后 **≤3s** 内参数生效（编码参数与实测帧率/分辨率随之变化）；**只改文案不改行为 = 不通过**；切换期间界面**不得**出现 >1s 的无响应 | [A] | Y-09 |
| UX-TC-065 | U11①；`06` §7.4 / **§7.4.0** | 三端 | 构造「设定 60 / 实测 52」场次（注入丢包或压 CPU） | 1. 设定帧率优先 60fps；2. 制造实测降到 52fps；3. 读 HUD 帧率数字与颜色；4. 四模式各重复一次 | ① HUD 显示**实测 52**、**不得显示设定的 60**（延迟/码率/分辨率同理，与 `05` §8.5 采样 CSV 偏差 ≤1 单位）；② **颜色按 `06` §7.4.0 派生，不用固定阈值**：帧率优先下 52 < 60−2 → **红**；同一「52」在均衡下应为正常色；画质优先下「分辨率变 1600×900 或 QP>22」才是红。**「红」= 当前模式不变量被破，这一语义必须三端一致** | [A] | PR-7；Y-02 |
| UX-TC-066 | U11②；`06` §7.6.3 | And / Win | B 档链路（2.4GHz）+ 硬件编码器可用 | 1. 在 B 档下打开选择器；2. 不点任何项，只观察 | 不可达项**不隐藏**（隐藏会让用户误判能力边界）、整行 `text-tertiary`、**右侧不显示设定值**、副行替换为原因。必须同时具备：① 原因**指向物理约束**（「2.4GHz 链路无法稳定承载 60fps」/「QP22 需 ≥10Mbps 稳定」），**写「暂不支持」= Major**；② **一行内给两个出路**（「→ 改用 720p60 · 或切换到有线」），且出路能在同一屏完成；仅置灰不给原因 = Major | [A] | — |
| UX-TC-067 | U11②；`06` §7.6.3 | And / Win | 同 TC-066 | 1. 点击置灰项 5 次；2. 键盘 `Enter`/`Space` 各试 3 次；3. 观察是否弹窗/建连/跳转 | **不报错、不发请求**；按 `06` §7.6.3 只把**焦点移到「出路」链接**上（P5：一次操作只推进一步）；不弹错误框、不改选中项、不发信令。**「点了才报错」= Blocker**（校验放错了位置）；长按 / 键盘均不得绕过 | [A] | — |
| UX-TC-068 | U11③；`06` §7.6.4 | 三端 | 帧率优先模式、链路可注入丢包 | 1. 稳定投屏 10s；2. 注入足够丢包使帧率跌破设定值−2；3. 观察状态点与黄条 | 状态在 **≤2s** 内进 `degraded`（黄实心，**不是** `connected`）；黄条出现；**不得**静默继续显示「已连接 · 正在投屏」而无任何降级提示 | [A] | C-13；Y-09 |
| UX-TC-069 | U11③；`06` §7.6.4 | 三端 | 同 TC-068 | 1. 读黄条文案；2. 与实测值比对；3. 四模式各触发一次 | 文案必须点名**牺牲了哪一维 + 具体值**，如「帧率已降至 52fps 以保住画质」「分辨率已降至 720p 以保住 60fps」；**只写「画质降级」四个字 = Major**；四种模式牺牲的维度必须与 C-24 的 `degrade_order` 一致（帧率优先**不得**写「牺牲帧率」）；横幅为**常驻可折叠**形态见 UX-TC-077 | [A] | Y-15；PR-7 |
| UX-TC-070 | U11 无硬件编码器提示 | Win / And（可禁用硬编的机器） | 禁用/移除硬件编码器（仅软编可用） | 1. 打开选择器；2. 选中「帧率优先」；3. 观察是否可选中、选中后有何提示 | **选中前**即可见提示「本机无硬件编码器，无法承诺该模式」；允许选中但必须**显式标注「当前为软编，帧率不保证」**；**选中后静默降级 = Blocker**（承诺与事实不符，触碰 PR-7） | [A] | Y-14 |
| UX-TC-071 | U11 2K/4K 档位前置条件 | 三端 | 一台无 HEVC 硬解的机器 | 1. 打开分辨率档位；2. 观察 2K/4K 项；3. 再换一台有 HEVC 硬解的机器重复 | 置灰文案**同时写明两个前置条件**：「需硬件编码器 + HEVC 支持」；缺任一条件即置灰；文案**不得**只写「不支持」而不给条件；有条件的机器上可选且能出画 | [A] | — |
| UX-TC-072 | U10/U11；`06` §8.2 | 三端 | 文案可读（只读） | 1. `grep` 全量用户可见文案；2. 按 **`06` §8.2 的 7 词术语表**逐项核对；3. 专查禁用词 | **术语表是 7 个词不是 5 个**：投屏 / 观看 / 远控 / 可被发现 / 局域网直连 / **共享（仅文件传输）** / **模式**。判据：① 投屏语义只出现「投屏」；② 「共享 / Sharing」**只允许出现在文件传输语境**；③ 模式的四项**不得**被称为「画质档 / 清晰度 / 等级 / 档位」，统一叫「模式」；④ 「共享屏幕」「镜像」「串流」作投屏动词 = Major（C-22） | [A] | Y-15 |
| UX-TC-073 | U11 布局健壮性 | 三端 | 320dp 宽（小屏）/ 150% 缩放 / en-US | 1. 打开选择器；2. 截图；3. 逐项检查换行与截断 | 四项**全部完整可见**，副行不截断、不省略到不可读；不出现横向滚动；en-US 下同样不溢出；按钮高度一致（差 ≤2dp） | [M] | X-16 |
| UX-TC-074 | U11 降级提示不遮挡画面 | 三端 | 画面冻结（E-12 静止图卡） | 1. 触发 `degraded`；2. 按 §B3 脚本对视频区求 AE | 黄条出现但**视频区域 AE == 0**（不占位、不重排、不缩放画面）；退出 `degraded` 后画面 bounds 与触发前**完全一致** | [M] | — |

| **UX-TC-075** | §7.6.5 控件收敛；§7.6.7 反模式 | 三端 | U11-P0 | 1. 全量搜索设置/会话页控件与文案中的「自适应 / Auto / 自动」；2. 打开高级折叠区再找一遍；3. 走一遍首次引导 | 界面**不存在**「自适应」开关（含文案/开关/复选框任何形态）；`06` §7.6.5 明确它被四模式吸收——「自适应开」≈均衡、「自适应关」≈帧率优先。**残留任一形态 = Major**；若同时存在四模式与该开关，判 Blocker（两个控件表达同一件事，组合语义不明） | [A] | — |
| **UX-TC-076** | **U11④ 无静默降级（反向判据）** | 三端 | U11-P0 + 可读 `SessionSnapshot.Mode` + 可注入丢包 | 1. 选帧率优先跑 10s；2. 注入丢包使实测明显变差；3. 逐秒记录「实测值 / `Mode` / UI 状态」三元组 | **只要实测变差，就必须出现「`Mode` 变更」或「状态迁到 `degraded`」之一**；**`Mode` 未变 + 未进 `degraded` + 实测档位已变 = 静默降级，判失败**（`06` §9 U11④ / PR-9）。画质优先（分辨率/QP 变化）与均衡（跨阈值）各重复一次；与 `05` **TC-121** 同一探测器，两侧结论必须一致 | [A] | Y-09；Y-15 |
| **UX-TC-077** | U11③；`06` §7.6.4 | 三端 | 构造 degraded（带宽限速） | 1. 触发 `degraded`；2. 等 10s 看是否消失；3. 点「详情」；4. 恢复带宽后再看 | ① 形态是**常驻可折叠横幅，不是 toast**（10s 后仍在，不自动消失）；② 文案含三件套：现象（「网络不足」）+ **取舍（「已降分辨率至 1600×900 以保住 60fps」）** + 最多两个动作（详情 / 换链路或改模式）；③ 颜色只用 `warn` 于图标与左侧 2px 边，**不做整条黄底**，正文 `text-primary`；④ 恢复后横幅消失并转为 **4s 的 `ok` toast**「已恢复到 60fps 与 1080p」 | [A] | — |
| **UX-TC-078** | §7.6.6 无障碍与 i18n | 三端 | 读屏开启 / 键盘可用 / en-US | 1. 读屏聚焦选择器；2. 纯键盘 `Tab` 进组 + 上下键移动；3. 切 en-US 看副行；4. 抽掉颜色看选中态 | ① 整组 `radiogroup`，每行 `radio` + `aria-describedby` 指向副行；② 朗读串为「帧率优先，锁定帧率，画质随网络自动取舍，已选中，第 1 项，共 4 项」，不可达时补「不可用，<原因>」；③ `Tab` 整体进出组、上下键移动并即时选中（不逐行停留）；④ 英文副行长约 +60%，桌面容两行、移动端**允许折行但不许截断**；⑤ 选中态不靠颜色单独表达（竖条 + 单选点填充 + 背景三重冗余） | [M] | — |
| **UX-TC-079** | §7.6.7 反模式六条（评审必查） | 三端 | 设计稿或已实现界面 | 1. 逐条比对六条反模式；2. 每条给出「命中 / 未命中 + 证据截图」 | 六条**全部未命中**：① 不用「流畅/标准/高清/超清」做选项名；② 不用星级/信号格/渐变条表示模式强度；③ 不做 2×2 网格（暗示两轴正交）；④ 选中后必须显示设定值；⑤ 不可达不得静默降级到相邻模式；⑥ 不用 toast 承载 degraded。**命中任一条即评审不通过**，须改设计后重审 | [M] | X-09 |

## 3. B · 五个可直接运行的验证脚本

> **统一约定**：脚本存放目录建议 `tools/ux/`（**本文档不创建该目录与文件**），由 T06/T07 落地时按此内容创建；输出统一落 `artifacts/ux/<日期>/`。所有脚本均为**只读取证**，不改产品代码。

### 3.1 UX-S1 · 对比度全量审计（`contrast_audit.py`）

**依赖**：`python3 ≥ 3.8`（仅标准库）。**可执行性：可直接运行。**
**能复现 `06` 已核算的数字**：`#8A8880` on `#121210` = **5.28:1**（`06` 记 5.2:1 ✓）、`#C49A6C` on `#FFFFFF` = **2.57:1**（`06` 记 2.4:1，结论同为不达标）。

```python
#!/usr/bin/env python3
"""UX-S1 · WCAG 2.x 对比度全量审计（08 §3.1）
用法: python3 tools/ux/contrast_audit.py [--gate]   退出码: 0=达标 / 1=有违例
令牌源: 06 §3.1（只读引用，不改 06）
"""
import sys, itertools

DARK = {"canvas": "#121210", "surface": "#1C1C18", "surface-raised": "#262620",
        "surface-sunken": "#0C0C0A", "border-subtle": "#2A2A26", "border-strong": "#3A3A34",
        "text-primary": "#E8E6E0", "text-secondary": "#B8B6B0", "text-tertiary": "#8A8880",
        "accent": "#C49A6C", "on-accent": "#1A1A1A", "focus-ring": "#C49A6C",
        "success": "#7BA05B", "warning": "#D4A84B", "danger": "#E85D4C"}
LIGHT = {"canvas": "#F4F2EE", "surface": "#FFFFFF", "surface-raised": "#FFFFFF",
         "surface-sunken": "#E8E5DF", "border-subtle": "#E0DCD4", "border-strong": "#C9C4BA",
         "text-primary": "#1A1A18", "text-secondary": "#55534E", "text-tertiary": "#75726B",
         "accent": "#8A6A42", "on-accent": "#FFFFFF"}      # 缺 6 令牌，见 §3.7 F-2
FG = ["text-primary", "text-secondary", "text-tertiary", "accent", "success", "warning", "danger", "on-accent"]
BG = ["canvas", "surface", "surface-raised", "surface-sunken"]

# 06 §3.1/§8.1 声明「实际会同时出现」的配对：只有这些参与门禁
_FG3, _BG3 = FG[:3], BG[:3]
DECLARED_TEXT = ([(f, b) for f in _FG3 for b in _BG3] + [("text-tertiary", "surface-sunken")]
                 + [("accent", b) for b in _BG3] + [("on-accent", "accent")]
                 + [(c, "surface") for c in ("success", "warning", "danger")])
DECLARED_NONTEXT = [("border-strong", "surface"), ("border-strong", "surface-raised"),
                    ("focus-ring", "canvas"), ("focus-ring", "surface")]  # WCAG 1.4.11 ≥3:1

def _lin(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def lum(h):
    h = h.lstrip("#")
    r, g, b = (int(h[i:i + 2], 16) for i in (0, 2, 4))
    return 0.2126 * _lin(r) + 0.7152 * _lin(g) + 0.0722 * _lin(b)

def ratio(a, b):
    la, lb = lum(a), lum(b); hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)

def audit(title, tokens, pairs, need):
    print(f"\n=== {title} ==="); fails = skipped = 0
    for fg, bg in pairs:
        if fg not in tokens or bg not in tokens:
            print(f"SKIP  {fg} on {bg}  (令牌未定义，见 §3.7 F-2)"); skipped += 1; continue
        r = ratio(tokens[fg], tokens[bg]); ok = r >= need; fails += 0 if ok else 1
        print(f"{'PASS' if ok else 'FAIL'}  {fg:15s} on {bg:15s} {r:5.2f}:1  (需 {need})")
    print(f"小计: 违例 {fails} / 跳过 {skipped}"); return fails

def sweep(name, tokens):
    print(f"\n--- {name} 全组合扫描（信息性，不参与门禁）---")
    pairs = [(f, b) for f, b in itertools.product(FG, BG) if f in tokens and b in tokens]
    bad = [x for x in sorted([(f, b, ratio(tokens[f], tokens[b])) for f, b in pairs],
                             key=lambda x: x[2]) if x[2] < 4.5]
    print(f"<4.5:1 组合数: {len(bad)}")
    for f, b, r in bad:
        print(f"  {f} on {b} = {r:.2f}:1")

def main():
    gate = "--gate" in sys.argv[1:]
    fails = (audit("DARK · 文字 >=4.5:1", DARK, DECLARED_TEXT, 4.5)
             + audit("DARK · 非文字 >=3:1", DARK, DECLARED_NONTEXT, 3.0)
             + audit("LIGHT · 文字 >=4.5:1", LIGHT, DECLARED_TEXT, 4.5)
             + audit("LIGHT · 非文字 >=3:1", LIGHT, DECLARED_NONTEXT, 3.0))
    sweep("DARK", DARK); sweep("LIGHT", LIGHT)
    print(f"\n总违例: {fails}")
    sys.exit(1 if (gate and fails) else 0)

if __name__ == "__main__":
    main()
```

```bash
# 取证（不阻塞）
python3 tools/ux/contrast_audit.py | tee artifacts/ux/$(date +%Y%m%d)/UX-S1-contrast.log
# 门禁（有违例则 exit 1）
python3 tools/ux/contrast_audit.py --gate
```

### 3.2 UX-S2 · 三端截图取样一致性（`sample_tokens.py`）

**依赖**：`python3` + `pillow`（`pip install pillow`）。**可执行性：脚本可直接运行，但 `coords.json` 需人工标定一次**（坐标属 `[M]`，比对属 `[A]`）。

```python
#!/usr/bin/env python3
"""UX-S2 · 三端截图令牌取样一致性（08 §3.2）
用法: python3 tools/ux/sample_tokens.py --coords c.json --shots win=a.png,and=b.png,web=c.png
判定: 同一令牌跨端 两两 dE(CIE76) <= 2  或  每通道 RGB 差 <= 4
"""
import argparse, json, math
from PIL import Image

def patch_mean(img, x, y, k=3):
    img = img.convert("RGB"); px = img.load(); w, h = img.size
    acc, n = [0, 0, 0], 0
    for dx in range(-k, k + 1):
        for dy in range(-k, k + 1):
            xx, yy = min(max(x + dx, 0), w - 1), min(max(y + dy, 0), h - 1)
            r, g, b = px[xx, yy]; acc[0] += r; acc[1] += g; acc[2] += b; n += 1
    return tuple(round(c / n) for c in acc)

def _f(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def to_lab(rgb):
    r, g, b = (_f(c) for c in rgb)
    X = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    Y = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 1.00000
    Z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883
    g_ = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    fx, fy, fz = g_(X), g_(Y), g_(Z)
    return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz))

def de76(a, b):
    return math.dist(to_lab(a), to_lab(b))

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--coords", required=True)
    ap.add_argument("--shots", required=True, help="win=a.png,and=b.png,web=c.png")
    a = ap.parse_args()
    coords = json.load(open(a.coords, encoding="utf-8"))
    imgs = {k: Image.open(v) for k, v in (kv.split("=", 1) for kv in a.shots.split(","))}
    ends, fails = list(imgs), 0
    for token in coords[ends[0]]:
        vals = {e: patch_mean(imgs[e], *coords[e][token]) for e in ends}
        print(f"\n[{token}] " + "  ".join(f"{e}={vals[e]}" for e in ends))
        for i in range(len(ends)):
            for j in range(i + 1, len(ends)):
                x, y = ends[i], ends[j]
                d, dch = de76(vals[x], vals[y]), max(abs(vals[x][c] - vals[y][c]) for c in range(3))
                ok = d <= 2.0 or dch <= 4; fails += 0 if ok else 1
                print(f"  {x} vs {y}: dE={d:5.2f} maxdRGB={dch:3d} -> {'PASS' if ok else 'FAIL'}")
    print(f"\n总违例: {fails}")
    raise SystemExit(1 if fails else 0)

if __name__ == "__main__":
    main()
```

```jsonc
// tools/ux/coords.json  —— 每个「端 × 分辨率」需人工标定一次（[M]）
{
  "win": {"canvas": [40, 640], "surface": [320, 420], "surface-raised": [600, 700],
          "accent": [1180, 96], "text-primary": [120, 120], "border": [300, 300]},
  "and": {"canvas": [30, 1200], "surface": [300, 800], "surface-raised": [500, 1300],
          "accent": [900, 120], "text-primary": [100, 200], "border": [280, 600]},
  "web": {"canvas": [40, 700], "surface": [400, 500], "surface-raised": [700, 800],
          "accent": [1200, 100], "text-primary": [140, 160], "border": [360, 360]}
}
```

```bash
python3 tools/ux/sample_tokens.py --coords tools/ux/coords.json \
  --shots win=UX-WIN-001-20261112-01.png,and=UX-AND-001-20261112-01.png,web=UX-WEB-001-20261112-01.png
```

**关键约束（R2）**：三张截图必须同主题、同字体缩放、同 DPI 档、同一静止图卡内容，否则比对无意义。

### 3.3 UX-S3 · chrome 自动隐藏（`chrome_hide.sh`）

**依赖**：ImageMagick（v7 `magick`，v6 `convert`/`compare`）。**可执行性：可直接运行（需先满足 R3 画面冻结）。**

```bash
#!/usr/bin/env bash
# UX-S3 · chrome 自动隐藏（08 §3.3）
# 用法: ./chrome_hide.sh <会话页无水印截图前缀> <视频区域 WxH+X+Y> <命令条区域 WxH+X+Y> <顶部区域 WxH+X+Y>
set -euo pipefail
PREFIX=${1:?}; VIDEO=${2:?}; CHROME=${3:?}; TOP=${4:?}
MAGICK=$(command -v magick || command -v convert)
CMP=$(command -v magick >/dev/null && echo "magick compare" || echo compare)

crop() { $MAGICK "$1" -crop "$2" +repage "$3"; }
ae()   { $CMP -metric AE "$1" "$2" null: 2>&1 | tr -d '\r\n ' || true; }

crop "${PREFIX}-t0.png" "$VIDEO"  /tmp/ux_v0.png
crop "${PREFIX}-t0.png" "$CHROME" /tmp/ux_c0.png
crop "${PREFIX}-t0.png" "$TOP"    /tmp/ux_p0.png
crop "${PREFIX}-t3.png" "$VIDEO"  /tmp/ux_v1.png
crop "${PREFIX}-t3.png" "$CHROME" /tmp/ux_c1.png
crop "${PREFIX}-t3.png" "$TOP"    /tmp/ux_p1.png

AE_VIDEO=$(ae /tmp/ux_v0.png /tmp/ux_v1.png)
AE_CHROME=$(ae /tmp/ux_c0.png /tmp/ux_c1.png)
AE_TOP=$(ae /tmp/ux_p0.png /tmp/ux_p1.png)
echo "AE(video)=$AE_VIDEO  AE(chrome)=$AE_CHROME  AE(top)=$AE_TOP"

FAIL=0
[ "$AE_VIDEO" -eq 0 ]          || { echo "FAIL: 视频区域被 overlay 占位（AE=$AE_VIDEO 应为 0）"; FAIL=1; }
[ "$AE_CHROME" -gt 0 ]         || { echo "FAIL: 命令条未隐藏（AE=$AE_CHROME 应为 >0）"; FAIL=1; }
[ "$AE_TOP" -gt 500 ]          || { echo "WARN: 顶部区域变化像素过少（AE=$AE_TOP），核对是否取错区域"; }
exit $FAIL
```

**关键约束（R3）**：`t0`/`t3` 两帧之间的视频内容**必须冻结**（静止图卡），否则 `AE(video)` 永远不为 0、用例失效。**这是本脚本唯一的前提，也是最容易被做错的前提。**

### 3.4 UX-S4 · 断线恢复注入

**依赖**：`adb`（Android 端）、Windows 管理员权限（`netsh`）。**可执行性：Android 侧命令可直接运行；Windows 侧 `netsh` 需提权 → 该步记 `[M]`；UI 断言可自动化。**

```bash
#!/usr/bin/env bash
# UX-S4 · 断线恢复注入（08 §3.4）  F6 四阶段逐段注入
set -uo pipefail
ADB=${ADB:-adb}; SERIAL=${1:?用法: ./disconnect.sh <serial> <1|2|3|4>}; STAGE=$2
PKG=com.unilinker.android
dump_ui() { $ADB -s "$SERIAL" shell uiautomator dump /sdcard/ui.xml >/dev/null; \
            $ADB -s "$SERIAL" pull /sdcard/ui.xml "/tmp/ui-$1.xml" >/dev/null; }
assert_text() { grep -q "$1" "/tmp/ui-$2.xml" && echo "  PASS: 命中文案「$1」" \
                || { echo "  FAIL: 缺失文案「$1」"; return 1; }; }

case $STAGE in
  1) echo "== 阶段1: 网络切换 =="                       # 期望落到「重连中」→「已连接」
     $ADB -s "$SERIAL" shell svc wifi disable; sleep 3; dump_ui s1; assert_text "重连中" s1
     $ADB -s "$SERIAL" shell svc wifi enable;  sleep 8; dump_ui s1b; assert_text "已连接" s1b ;;
  2) echo "== 阶段2: 对方关闭 =="                       # 对端结束共享 →「对方已关闭共享」
     dump_ui s2; assert_text "对方" s2 ;;
  3) echo "== 阶段3: 超时（5 次退避）=="                # 18s 覆盖 0.5+1+2+4+8
     $ADB -s "$SERIAL" shell svc wifi disable; sleep 18
     dump_ui s3; assert_text "次" s3; assert_text "结束会话" s3
     $ADB -s "$SERIAL" shell svc wifi enable ;;
  4) echo "== 阶段4: 认证失效 =="                       # 对端吊销 sessionToken → 授权失效分支
     dump_ui s4; assert_text "授权" s4 ;;
  *) echo "仅支持 1..4"; exit 2 ;;
esac
```

```bash
# Windows 侧（需管理员 PowerShell / CMD）—— 该步为 [M]
netsh interface show interface                                     # 先取准确接口名
netsh interface set interface "以太网" admin=disable
netsh interface set interface "以太网" admin=enable
```

```bash
# Web 侧（Playwright 1.48.0）—— [A]；必须系统 Edge，bundled Chromium 无 H264
python3 - <<'PY'
from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    pg = p.chromium.launch(channel="msedge", headless=True).new_page()
    pg.goto("http://localhost:9527"); print("online:", pg.evaluate("navigator.onLine"))
PY
```

### 3.5 UX-S5 · 字体缩放 / 减少动画

**依赖**：`adb`、`python3`（XML 解析）、`playwright`。**可执行性：设置与结构断言可运行；「文字是否被视觉截断」不可靠自动化 → 该判据记 `[M]`。**

```bash
#!/usr/bin/env bash
# UX-S5a · 字体缩放（08 §3.5）
set -uo pipefail
ADB=${ADB:-adb}; SERIAL=${1:?用法: ./font_scale.sh <serial> <scale>}; SCALE=$2
PKG=com.unilinker.android
$ADB -s "$SERIAL" shell settings put system font_scale "$SCALE"
$ADB -s "$SERIAL" shell am force-stop $PKG
$ADB -s "$SERIAL" shell am start -n $PKG/.MainActivity >/dev/null; sleep 4
$ADB -s "$SERIAL" shell uiautomator dump /sdcard/ui.xml >/dev/null
$ADB -s "$SERIAL" pull /sdcard/ui.xml "/tmp/ui-fs$SCALE.xml" >/dev/null
python3 tools/ux/check_overlap.py "/tmp/ui-fs$SCALE.xml"
$ADB -s "$SERIAL" shell settings put system font_scale 1.0     # 复位
```

```python
#!/usr/bin/env python3
"""check_overlap.py · 字体放大后的结构性缺陷检测（[A] 部分）
检测: ① 零尺寸文本节点 ② 兄弟节点 bounds 重叠；不做视觉截断（省略号/裁字）→ 属 [M] 目视
"""
import sys, re, xml.etree.ElementTree as ET

def bounds(s):
    m = re.findall(r"\d+", s or "")
    return tuple(map(int, m)) if len(m) == 4 else None

def overlap(a, b):
    return not (a[0] + a[2] <= b[0] or b[0] + b[2] <= a[0]
                or a[1] + a[3] <= b[1] or b[1] + b[3] <= a[1])

root = ET.parse(sys.argv[1]).getroot(); bad = 0
for node in root.iter("node"):
    txt, b = (node.get("text") or "").strip(), bounds(node.get("bounds"))
    if txt and b and (b[2] <= 1 or b[3] <= 1):
        print(f"FAIL 零尺寸文本: '{txt[:24]}' {b}"); bad += 1
for parent in root.iter("node"):
    kids = [(k.get("text").strip(), bounds(k.get("bounds"))) for k in parent
            if (k.get("text") or "").strip()]
    kids = [(t, b) for t, b in kids if b]
    for i, j in ((i, j) for i in range(len(kids)) for j in range(i + 1, len(kids))):
        if overlap(kids[i][1], kids[j][1]):
            print(f"FAIL 文本重叠: '{kids[i][0][:16]}' vs '{kids[j][0][:16]}'"); bad += 1
print(f"结构违例: {bad}")
sys.exit(1 if bad else 0)
```

```bash
#!/usr/bin/env bash
# UX-S5b · 减少动画（Android）
adb -s "$SERIAL" shell settings put global animator_duration_scale 0
adb -s "$SERIAL" shell settings put global transition_animation_scale 0
adb -s "$SERIAL" shell settings put global window_animation_scale 0
# …操作后复位为 1
```

```python
# UX-S5c · 减少动画（Web，Playwright reduced_motion）—— [A]
from playwright.sync_api import sync_playwright
JS = """() => [...document.querySelectorAll('*')].map(e => [e.tagName,
  getComputedStyle(e).transitionDuration, getComputedStyle(e).animationDuration])
  .filter(([, t, a]) => (parseFloat(t) || 0) > 0.1 || (parseFloat(a) || 0) > 0.1)"""
with sync_playwright() as p:
    b = p.chromium.launch(channel="msedge", headless=True)
    pg = b.new_context(reduced_motion="reduce").new_page(); pg.goto("http://localhost:9527")
    rows = pg.evaluate(JS)
    print(f"仍带 >100ms 动画的元素数: {len(rows)}"); [print("  ", r) for r in rows[:20]]
    b.close()
```

### 3.6 依赖与可执行性矩阵（**如实**）

| 脚本 | 依赖 | 能否**直接**跑 | 需人工的前置 | 覆盖用例 |
|---|---|---|---|---|
| UX-S1 对比度 | `python3`（标准库） | **能**，复制即跑 | 令牌值须与 `06` §3.1 同步（改设计要同步改脚本） | TC-007/008/009/011/012/013 |
| UX-S2 取样一致 | `python3` + `pillow` | **能**，但 `coords.json` 须先人工标定 | 6 令牌 × 3 端 × 分辨率坐标（一次性，`[M]`）；截图需同参数（R2） | TC-001/014 |
| UX-S3 chrome 隐藏 | ImageMagick 7 或 6 | **能** | **画面必须冻结**（R3）；区域坐标需人工量一次 | TC-031 |
| UX-S4 断线注入 | `adb`；Windows 需管理员 | Android 侧**能**；Windows `netsh` 段**需提权**（`[M]`） | 需真机 + 同 LAN 对端；`uiautomator` 文案须与最终实现一致 | TC-044~047、049 |
| UX-S5 缩放/动效 | `adb`；`python3`；`playwright` | 设置与结构断言**能** | 视觉截断判定 `[M]`；`font_scale` 会影响其他 App（用专用测试机） | TC-051~055（结构部分）、TC-034 |

**依赖可用性实测（本机，`[A]` 可跑的前提）**：

| 依赖 | 检测命令 | 说明 |
|---|---|---|
| python3 | `python3 -V` | `S1/S2/S5` 必需 |
| pillow | `python3 -c "import PIL, sys; print(PIL.__version__)"` | 仅 `S2` 必需 |
| ImageMagick | `magick -version` 或 `convert -version` | `S3` 必需；v6 用 `convert/compare` 裸命令名 |
| adb | `adb version` | `S4/S5` 必需；`adb devices` 须见目标序列号 |
| playwright | `python3 -c "import playwright; print(playwright.__version__)"`（或复用 `.NET` 侧 `Microsoft.Playwright 1.48.0`） | `S4` Web 段、`S5c` 必需 |
| Edge channel | `msedge --version` | **必须系统 Edge/Chrome**；bundled Chromium 无 H264（`05` §2.4、`CLAUDE.md:23-24`）。纯 CSS/无障碍断言可用 bundled，但一旦涉及视频解码必须换 channel |

> **一句实话**：这 5 个脚本里，**S1/S3 是真正「拿来即用」的**；S2 依赖一次性坐标标定；S4/S5 的**注入与设置**能跑，但**判定**仍要人看图。我把这些差别逐条写在各脚本标题与 §3.6 表里，没有把任何一个需要人眼的部分标成 `[A]`。

### 3.7 预核算结果（脚本输出基准，供校准与开缺陷）

> 下表是 **`S1` 脚本实跑输出**（Python 3.13 / 2026-11-12，与脚本同一份代码），用于校准实现取色。**任何实现若与本表偏差 > 0.05，说明取色错或引用了非 `06` §3.1 的色值。**

| 配对 | 脚本实测 | `06` 标注 | 结论 | 处置 |
|---|---:|---:|---|---|
| `#8A8880` on `#121210` | **5.28:1** | 5.2:1 | ✅ 达标 | 无 |
| `#C49A6C` on `#FFFFFF` | **2.57:1** | 2.4:1 | ❌ 不达标 | 浅色下禁作文字色（TC-013） |
| `#E8E6E0` on `#121210` | **15.03:1** | 12.6:1 | ✅ 达标 | 若 `06` 取 `surface-raised` 则为 **12.19**，属基准差异非错误；建议标注补注背景 |
| `#B8B6B0` on `#121210` | **9.25:1** | 9.2:1 | ✅ 达标 | 无 |
| `#C49A6C` on `#121210` | **7.31:1** | 7.3:1 | ✅ 达标 | 无 |
| `#1A1A1A` on `#C49A6C` | **6.78:1** | 6.8:1 | ✅ 达标 | 无 |
| 深色状态色 on `surface`（success/warning/danger） | **5.71 / 7.73 / 4.97** | 6.2 / 8.5 / 5.4（按 `canvas`） | ✅ 达标 | 三种背景组合全部 ≥4.5，无风险 |
| **`#8A8880` on `#262620`** | **4.28:1** | 未单列 | ❌ **不达标** | F-1：深色 `text-tertiary` 在 `surface-raised`（弹层/悬浮工具条）不足 |
| **`#75726B` on `#F4F2EE`** | **4.29:1** | 未单列 | ❌ **不达标** | F-1：浅色 `text-tertiary` 在 `canvas` 上不足 → 提亮或限定用途 |
| **`#75726B` on `#E8E5DF`** | **3.82:1** | 未单列 | ❌ **不达标** | F-1：同上，`surface-sunken` 上更差 |
| **`#8A6A42` on `#F4F2EE`** | **4.45:1** | 未单列 | ❌ **差 0.05** | F-1：浅色 `accent` 作文字色在 `canvas` 上同样不足 |
| **`#3A3A34` on `#1C1C18` / `#262620`** | **1.49 / 1.33** | — | ❌ **远低于 3:1** | F-1：`border-strong` 作输入框描边不达 WCAG 1.4.11，需提亮至约 `#6A6A60` |
| **`#C9C4BA` on `#FFFFFF`** | **1.74:1** | — | ❌ **远低于 3:1** | F-1：浅色 `border-strong` 同样不达非文字 3:1 |

**三条文档侧发现（供主理人分派，本文不改 `06`）**：

| 编号 | 发现 | 影响 | 建议处置 | 等级 |
|---|---|---|---|---|
| **F-1** | `S1` 实跑（Python 3.13，`总违例 8 / 跳过 5`）：**深色文字 17 组中 1 组**不达标（`text-tertiary`/`surface-raised` **4.28**）；**浅色文字 14 组中 3 组**（`text-tertiary`/`canvas` **4.29**、/`surface-sunken` **3.82**、`accent`/`canvas` **4.45**）；**非文字 8 次判定中 4 次**（深色 `border-strong` **1.49 / 1.33**、浅色 `border-strong` **1.74 ×2**）；5 组因浅色令牌未定义 SKIP。`text-primary` 12.6 与其 `surface-raised` 值 **12.19** 相近，属基准差异非错误 | 若照 `06` 标注实现，浅色辅助文字、浅色强调文字与三端输入框描边**上线即不达标**（WCAG 1.4.3 / 1.4.11） | ① 请 UX 复核并修正 `06` §3.1 标注（补注背景基准）；② 提亮浅色 `text-tertiary` 或限定仅用于 `surface`；③ `border-strong` 提亮（深色约 `#6A6A60`、浅色约 `#8A857A`）；④ 浅色 `accent` 不在 `canvas` 上作文字色 | Major（文档）+ 实现前必闭 |
| **F-2** | 浅色分支缺 6 个令牌定义：`accent-hover`、`accent-press`、`focus-ring`、`success`、`warning`、`danger` | 浅色主题下状态色与悬停/焦点态**无权威值**，三端会各写一套（违反「三端不得各写一套」） | 请 UX 在 `06` §3.1 补齐浅色 6 令牌；补齐前 TC-008/009/011 的浅色部分记 `[X]`（令牌未定义） | Major（文档） |
| **F-3** | Windows/Android 侧无 UI 自动化基础设施（`UniLinker.E2E.Tests.csproj:12-19` 仅 Playwright/xunit/FluentAssertions） | U9 全组 + TC-010/024/035/042/048 只能 `[M]`，回归成本高 | 若要转 `[A]`：引入 FlaUI（Win）/ 保持 `uiautomator`（And）；属新增依赖，需主理人批准 | 建议（不阻塞） |

## 4. C · 现状缺陷回归基线（15 条，改后必须「不复现」）

> **这一节是本轮最容易漏的地方**：功能用例全绿、但「Start Share 还是假的」，等于没改。下表 15 条必须在**每个 UX 里程碑出口前重跑一遍**，并逐条勾选「已消失 / 仍存在」。
>
> 来源：`06` §1.4 三条「假状态」+ §1.2 Y-09~Y-15 + §1.1 四个高可见 X 项。**15 条中 9 条可自动化，6 条必须人工**——不为凑数把人工项标 `[A]`。

| 编号 | 来源 | 复现步骤 | 现状实际表现（源码实证） | 改后期望（可判定） | 标记 |
|---|---|---|---|---|---|
| **REG-01** | `06` §1.4-① / Y-09 | Win 共享页点「Start Share」，观察对端与视频区 | 按钮状态变了，**实际未开始采集**：`ShareViewModel.cs:132` `IsSharing = true` 前无采集启动确认，`_bridge.StartSharing(config)` 无返回值校验 | 点后 **≤500ms** 出现「正在采集」实证（首帧计数 > 0 或采集器状态 `Active`）；失败路径**必须**显示原因而非按钮变绿 | [A] |
| **REG-02** | `06` §1.4-② / Y-10 | Win 点「Connect」 | 只做了页面跳转/状态文本变化，**未建立 PeerConnection** | 点后显示四阶段进度且**停留 ≥200ms 于「信令」阶段**；`connectionState` 进 `connected` 才显示「已连接」 | [A] |
| **REG-03** | `06` §1.4-③ | Android 点底部「投屏」Tab | Tab 名实不符（名为投屏，实为主体页/设置），无「开始共享」入口 | Tab 名称与实际内容一致；入口命名与 `06` §8.2 术语表一致（`grep` 术语 0 违例） | [A] |
| **REG-04** | Y-11 | Android 冷启动进发现页 | 首屏**必然**显示「未发现设备」（发现未完成就下结论） | 发现未完成时显示**扫描中**（骨架/进度），**不得**先显示空状态结论；空状态仅在 ≥15s 超时后出现 | [A] |
| **REG-05** | Y-12 | Android 各页按返回键 / 全屏观看 | 无返回处理，直接退到桌面；非沉浸式（系统栏常驻挤占画面） | 返回键**逐级返回**（浮层→页→退出确认）；全屏观看时进入沉浸式（系统栏隐藏，边缘滑动唤出） | [M] |
| **REG-06** | Y-13 | Win 触发 InfoBar（长消息）/ 点托盘图标开关 | InfoBar 覆盖标题栏按钮；托盘开关**不读取真实状态**（只是翻转本地布尔） | InfoBar 出现在内容区且**不遮挡**标题栏按钮（bounds 无交集）；托盘开关状态 == 真实服务状态（外部停止服务后 UI 同步变） | [A] |
| **REG-07** | Y-14 | Win 硬件/能力面板 | 硬件状态**写死**（硬编码展示「支持 H264」等） | 面板值来自运行时探测（关掉编码器后应变为不可用）；写死值 `grep` 命中 == 0 | [A] |
| **REG-08** | Y-15 | Web 断开连接 / answer 阶段 | `web/app.js:198-200` 在 **answer 刚发出**就把按钮改成「断开」并移除 primary（未等 `connected`）；`disconnected` 时 `:130-141` 只改文案，**UI 状态不复位** | 按钮只在 `connectionState==='connected'` 后才显示「断开」；`disconnected/failed` 后按钮、状态点、能力提示**全部复位**，且给出重连/结束两个动作 | [A] |
| **REG-09** | X-09 | 三端连接页 | 信号格是**装饰性色阶**（`web/styles.css:57-72` 四档颜色按 class 写死，非真实质量） | 信号指示要么反映真实指标（延迟/丢包档位），要么**移除**；纯装饰 = Major（会误导用户排障） | [A] |
| **REG-10** | X-12 | 三端各列表/发现页空态 | 无空状态设计（空白页或纯文字一句） | 每个列表/发现/历史页有**空状态**（图标 + 一句解释 + 一个动作）；无空状态的页面数 == 0 | [A] |
| **REG-11** | X-14 | 同一故障（如端口不通）在 Win/And/Web 各触发一次 | 三端文案与视觉反馈**三套**（一端 toast、一端红字、一端静默） | 同一故障三端**文案语义一致**（可用同一术语）+ 反馈通道一致（不再一端 toast 一端红字）；文案 diff 需在 `06` §8.2 字典内 | [A] |
| **REG-12** | X-20 | 三端冷启动 | 启动闪白/闪系统色（未设置窗口背景色，先白后暗） | 首帧即为 `canvas` 色（取样 ΔE ≤ 2 于 `canvas`），**无白帧**；Win 需设 `Window.SystemBackdrop`/根容器背景 | [A] |
| **REG-13** | X-21 | Win 全界面 | 使用 Fluent 默认强调色（系统蓝），**无品牌色**（`AppStyles.xaml:7-11` 全部 `ThemeResource`） | 自绘控件全部走品牌令牌；系统控件保留但**强调色映射到 `accent`**（按钮主色取样 ΔE ≤ 2 于 `#C49A6C`） | [A] |
| **REG-14** | X-06 | 三端切浅色主题 | 无浅色分支（`Theme.kt:208` 只有一套 `IndustrialDesign` 常量；Web 只有暗色） | 浅色主题可切换且**令牌齐全**（补齐 F-2 后）；切换后无「深色残留」（取样 ΔE ≤ 2 于浅色 `canvas`）；系统主题变更 3s 内跟随 | [A] |
| **REG-15** | Y-02 | 会话中找「零流量」证据 | 无任何可见证据（只有文档里的承诺） | 会话页 ≤2 次点击可见「公网连接 0」（同 TC-027），且数值可被抓包核对（同 TC-028） | [A] |

**回归执行规则**：

1. 每条**必须**在改动前后各跑一次，报告写「前：存在 / 后：已消失」，**不允许只写「后：正常」**。
2. 任一 REG 项「仍存在」→ 该里程碑 UX 出口**不得签字**（与 `05` §11.5 一票否决同源）。
3. **REG-01/02/08 是「假状态」三兄弟**，优先级最高：功能演示再漂亮，这三条不复现即整体不通过。

## 5. D · 里程碑出口与报告

### 5.1 M1 UX 出口清单（`06` §10 第 1 行展开）

> M1 主题：双 Android 同 LAN 单向出帧 10min。UX 侧只验「**看得懂、看得见**」，不验美观。

| # | 出口条件（可勾选） | 判定 | 证据 |
|---|---|---|---|
| M1-UX-1 | 三端会话页品牌令牌一致 | TC-001 18/18 组比对通过 | `UX-S2` 输出 + 3 张截图 |
| M1-UX-2 | 三端视频舞台与信箱黑边正确 | TC-014 通过；黑边 RGB ≤ 8 | 2 张截图 |
| M1-UX-3 | 「已连接」「无画面」可区分 | TC-023 通过（文案 + `frameCount=0` 可见） | 截图 + 诊断面板 |
| M1-UX-4 | 零流量证据 ≤2 点击可见 | TC-027、TC-028 通过 | 截图 + 抓包号（`05` §13.3） |
| M1-UX-5 | 首屏时间达标 | TC-015/016 通过（≤3000ms） | 录屏 + `am start -W` 日志 |
| M1-UX-6 | 「假状态」三条已消失 | REG-01/02/03 全部「已消失」 | 回归清单签字 |
| M1-UX-7 | 骨架屏/空状态齐全 | TC-017、REG-10 通过 | 截图 |
| **一票否决** | 任一 UX-TC 判 Blocker 未闭 / REG-01~03 仍存在 / `[P]` 项被写成「功能通过」 | — | — |

### 5.2 M4 UX 出口清单（授权 + 撤销）

| # | 出口条件 | 判定 | 证据 |
|---|---|---|---|
| M4-UX-1 | 授权遮罩四项信息齐全 | TC-036 通过（缺项=Blocker） | 遮罩截图 |
| M4-UX-2 | 默认最小权限 | TC-037 通过（默认「仅观看」） | 截图 + 控件树 |
| M4-UX-3 | 不可误关 | TC-038 通过 | 录屏 |
| M4-UX-4 | 撤销 ≤1s | TC-041 **20/20 ≤1000ms**（取最大值） | `05` §13.6 `SEC-*` + 对端日志 |
| M4-UX-5 | 被控常驻感知 | TC-042 三处齐全 | 截图 ×3 |
| M4-UX-6 | 拒绝闭环 | TC-040 两端文案一致 | 两端截图 |
| M4-UX-7 | 核对码语义正确 | TC-043 通过 | 两端截图 + 鉴权日志 |
| M4-UX-8 | 控制可定位（失败注入） | TC-020/021/022 至少 3/3 通过 | 三阶段截图 |
| **一票否决** | 撤销 >1000ms 任一次 / 默认勾选「允许控制」/ 常驻提示缺失 | — | — |

### 5.3 M5 UX 出口清单（性能可读 + 弱网）

| # | 出口条件 | 判定 | 证据 |
|---|---|---|---|
| M5-UX-1 | 状态条数据真实可核对 | TC-028 通过（界面 vs 抓包偏差 ≤2%） | 截图 + `LAT-*` |
| M5-UX-2 | 三端对比度全达标 | TC-007/008/009/011/012/013 全部通过（含 F-1 修正后复跑） | `UX-S1 --gate` 退出码 0 |
| M5-UX-3 | chrome 自动隐藏不重排 | TC-031、TC-033 通过（AE(video)==0） | `UX-S3` 输出 |
| M5-UX-4 | 断线恢复全链路 | TC-044~047、049 通过 | 录屏 + `UX-S4` 输出 |
| M5-UX-5 | 弱网主观可接受 | 3%/5% 丢包下 W-06 双人签字（`05` §4.7） | 双人签字表 |
| M5-UX-6 | 文案一致 | TC-056~060 通过 | `grep` 日志 + 截图 |
| M5-UX-7 | 15 条回归基线全「已消失」 | REG-01~15 = 15/15 | 回归清单签字 |
| **M5-UX-8**（v1.1 新增） | **四模式可见性全绿（`06` §9 U11 / 任务 UI-09）** | UX-TC-061~079 全通过（默认均衡 / 副行文案逐字对 `06` §7.6.2 / 3s 生效 / HUD 实测值 + 颜色按 §7.4.0 派生 / 不可达前置置灰且不可点穿 / degraded 横幅写明取舍且非 toast / 无硬编显式提示 / 2K·4K 双前置 / 术语 7 词 / 无「自适应」开关 / **静默降级探测器不触发** / 反模式六条未命中）；对应 `05` TC-102~119 与 **TC-121** 同时全绿 | 三端截图（中/EN）+ HUD 采样 CSV + 置灰态截图 + 三元组日志 |
| **一票否决** | 任一对比度违例未闭 / `AE(video)!=0` / 回归项仍存在 / **不可达组合可点击且点了才报错** / **HUD 显示设定值而非实测值** / **静默降级**（`Mode` 未变 + 未进 `degraded` + 实测变差） / **四模式与「自适应」开关并存** | — | — |

### 5.4 UX-RPT 报告模板（沿用 `05` §13 风格）

**报告号格式**：`UX-RPT-<里程碑>-<YYYYMMDD>-<序号>`，例 `UX-RPT-M5-20261112-001`。

| 字段 | 内容要求 |
|---|---|
| **报告号 / 里程碑 / 日期 / 构建号** | 构建号取 git short sha + 构建时间；三端构建号分别列（Win/And/Web） |
| **设备与系统 / 屏幕 / 缩放** | 每台一行：Win 版本（`winver`）/ Android 型号 + ROM + API level / 浏览器名+版本；像素分辨率 + `densityDpi` + Win 缩放比（125%/150%）；Android `font_scale`、Win 文本缩放、Web `zoom` |
| **主题** | 深色 / 浅色（两者若都跑则分行） |
| **用例通过率** | `UX-TC` **79 条**：通过/失败/阻塞数；分 **U1~U11** 小节汇总；`[A]/[M]/[P]/[X]` 各多少条（v1.1：U11 = 19 条，[A] 15 / [M] 4；U11 须逐条标注所挂的 `06` §9 U11①②③④） |
| **未通过项 + 缺陷等级** | 每条：用例号 / 期望 / 实测 / 等级（Blocker/Major/Minor，`05` §11.3）/ 责任端 / 责任里程碑 |
| **回归基线结果** | REG-01~15 逐条「已消失 / 仍存在」，**必须写改动前状态** |
| **脚本执行记录** | `UX-S1`~`S5` 各：是否执行、退出码、输出文件路径、跳过的原因（未执行必须写原因，禁止留空） |
| **截图存放路径** | `artifacts/ux/<日期>/UX-<端>-<用例号>-<日期>-<序号>.png`（R1 命名），并给总索引 CSV |
| **`[P]` 项声明** | 逐条写「降级提示已验证（非功能通过）」，并注明平台限制原因 |
| **默认假设声明** | 引用 `01` §6 的默认答案项（Q9/Q11/Q13/Q14），标注「基于默认假设」 |
| **盲区声明** | 本轮未覆盖的 §6 条目逐条列出并说明替代手段 |
| **签字** | QA（严过关）/ 工程师 / 架构师 / 主理人；一票否决项核对结果 |

**截图索引 CSV 模板**：

```csv
case_id,end,theme,font_scale,resolution,dpi,file,result
UX-TC-001,WIN,dark,1.0,1920x1080,150%,artifacts/ux/20261112/UX-WIN-001-20261112-01.png,PASS
UX-TC-001,WEB,dark,1.0,1440x900,100%,artifacts/ux/20261112/UX-WEB-001-20261112-01.png,FAIL
```

### 5.5 设备矩阵（UX 专项，最小 4 台 + 1 浏览器）

| # | 设备 | 规格要求 | 为什么必须 | 覆盖用例 |
|---|---|---|---|---|
| **D-1** | Win11 主机 | 1920×1080 或更高；可切 125%/150% 缩放；可切深/浅色 | Windows 端品牌令牌首次落地（现状无品牌色，`06` §1.1 X-21）+ DPI 缩放是布局溢出高发区 | TC-001/002/015/017/018/033/034/051~054 |
| **D-2** | Android **API < 33（Android 12 及以下）** | 中端机，API 26~32 | **MediaProjection 授权行为与 targetSdk 35 不同**：API 34+ 强制 FGS `mediaProjection` 类型与每次弹窗，老版本无此约束 → 必须有一台「老系统」证明向后兼容 | TC-016/019/036~043 |
| **D-3** | Android **API ≥ 34（Android 14+）** | targetSdk 35 行为样本，中端机 | 强制「每次共享都弹窗 + 单次 token」是 `06` §6 F5 与 `05` §7 的核心差异点，**只有 14+ 能验** | TC-036~041、TC-050 |
| **D-4** | Android 大屏（平板/折叠）优先 | 4:3 或 21:9 宽高比 | 信箱黑边、chrome 隐藏、悬浮工具条位置在非 16:9 上易错（`05` §12 E-3 同源） | TC-014/031/033/035 |
| **D-5** | Edge（系统安装） | Edge ≥ 120 或 Chrome ≥ 120 | **Playwright bundled Chromium 无 H264**，浏览器观看端与视频相关断言必须走系统 channel（`05` §2.4、`CLAUDE.md:23-24`） | TC-001/006/013/055/059 |

> **不允许的替代**：Android 模拟器**不可**用于 U7 授权、U6 chrome 隐藏、U3 首屏时间、U8 断线恢复的真机判定（`05` §12.3 已定；模拟器的 MediaProjection 弹窗、系统栏、ROM 行为与真机不一致）。

## 6. E · 盲点声明（这套 UX 验收测不到什么）

| # | 盲点 | 为什么测不到 | 替代手段 | 残余风险 |
|---|---|---|---|---|
| **B-1** | **主观审美**（「好不好看」） | 像素断言只能验一致性，验不了品味；ΔE ≤2 也可能是「一致地难看」 | 5 人可用性测试 + 偏好排序（三端并排，A/B 强制选择）+ 设计评审签字 | 中：审美偏差不影响功能，但影响留存 |
| **B-2** | **真实用户的理解成本** | TC-025 只测「能否指出阶段」，测不到「用户是否理解为什么」 | SUS 问卷（10 题，目标 ≥70 分）+ 任务成功率 + 首次求助时间 | 高：`06` §6 F2 四阶段的文案是否真的能被非技术用户读懂，只有真实用户能回答 |
| **B-3** | **厂商 ROM 通知栏差异** | 「常驻通知 + 停止按钮」（TC-042）在 MIUI/EMUI/ColorOS 上的折叠、限权、被清理行为各不相同，无法穷举 | ≥2 品牌 ROM 各验一遍 + 记录 ROM 版本与设置项截图；已知差异登记为 `[P]`（只验降级提示） | 高：部分 ROM 会静默折叠常驻通知，用户可能**看不到正在被控**——这是安全相关盲区，须在报告中显式声明 |
| **B-4** | **低端机掉帧下的主观流畅度** | 帧率/延迟可量化，但「用起来卡不卡」是感官判断；软编在低端机上会同时拖慢 UI 线程 | 低端机专项走查（录屏 60fps）+ 主线程卡顿帧统计（`Choreographer` 掉帧数）+ 双人主观打分 | 中 |
| **B-5** | **长时间使用后的视觉疲劳** | 对比度达标 ≠ 8 小时盯屏舒适；暖灰 + 铜金在长时间使用下的疲劳度无量化标准 | 浅色主题作为可选降级（F-2 补齐后）+ 用户自述反馈 | 中 |
| **B-6** | **无障碍读屏的真实体验** | TC-055 只验「有名称」，验不到「播报顺序是否可理解」「动态状态是否播报」 | TalkBack / Narrator 人工走查 1 轮（录屏）+ 视障用户可用性访谈（可选） | 高：名称齐全但顺序错乱对读屏用户等于不可用 |
| **B-7** | **跨端「像同一个产品」的整体感** | 逐令牌比对通过，但间距节奏、动效曲线、图标风格的整体一致性无法用 6 个取样点覆盖 | 三端并排全流程录屏，设计评审 1 轮主观打分（≥4/5 通过） | 中 |
| **B-8** | **`prefers-reduced-motion` 的完整覆盖** | S5c 只统计 `transition/animation` 时长，测不到 JS 驱动的动画（`requestAnimationFrame`、Canvas 位移） | 代码层 `grep` 动画入口 + 人工开启减少动画后走查 6 条核心流程 | 中：JS 动画是最常见的遗漏 |
| **B-9**（v1.1） | **用户是否认同「牺牲的那一维」** | U11 只能验「有没有说清牺牲了什么」，验不了「用户是否接受这个取舍」——同为 `degraded`，有人愿意丢分辨率保帧率，有人相反 | 5 人可用性测试：给同一劣化场次，让用户为四种模式各打「可接受/不可接受」+ 追问理由；据此校 `04` 的 `degradationPreference` 默认值 | 中：取舍偏好因人/因任务而异，硬编码的默认偏好必然对一部分人是错的 |

> **盲区不是免责声明**：B-3 与 B-6 已触及「用户能否发现被控」与「残障用户能否使用」，属**必须在报告中显式声明并请用户确认接受**的项，不能只在文末写一句「未覆盖」。

## 7. 与 `05` 的追溯关系（避免两套口径）

| 本文内容 | `05` 对应 | 关系 |
|---|---|---|
| UX-TC-001~060 | `05` §5 TC-001~085（`05` v1.1 已扩至 TC-001~120，U1~U10 仍只对应前 85 条） | **不重叠**：`05` 测功能与合规判据，本文测视觉/交互/感知；唯一交叉是 `05` §4.7 的 W-01~W-14 人工走查，本文将其 UX 部分细化并补自动化 |
| **UX-TC-061~079（U11）** | **`05` TC-102~TC-119 + TC-121** | **互补且一一对应**：本文验「承诺是否可见、是否撒谎」（文案/置灰/降级文案/HUD 取实测值/静默降级探测），`05` 验「承诺是否兑现」（≥58fps/60s、QP ≤22、功耗 ≤40%）。**两者缺一即该模式不成立**——只过本文 = 界面说了但没做到；只过 `05` = 做到了但用户看不见（违反 PR-7）。**UX-TC-076 与 `05` TC-121 是同一探测器的两个视角，结论必须一致** |
| U11 ↔ `06` §7.6 | `06` §7.6.1~§7.6.7 / §7.4.0 / §8.2 / §9 U11 | **设计侧唯一依据**（已落地）。U11 的「对应判据」列已逐条指向 `06` 小节；`README` §7.6 C-24 降为上游依据 |
| U11 ↔ 任务 | `06` §10 **UI-09**（M2 出口） | 实现归属 UI-09（**不引 UI-02**：UI-02 只提供容器位，模式契约的界面表达是独立任务）；`06` §10 明确「M2 出口必须包含 UI-03/UI-09 完成，U11 通过」 |
| REG-01~15 | `05` §2 现状盘点、§14 RK-12 | 本文把「现状缺陷」从盘点变成**可回归的清单** |
| `UX-RPT-*` | `05` §13.5 `MIL-*` | `UX-RPT` 是 `MIL-*` 的**子报告**，报告号在 `MIL-*` 的「证据报告号清单」中被引用 |
| `[A]/[M]/[P]/[X]` / 里程碑 / 缺陷分级 | `05` §1.2、§11.2、§11.3、`03` §6 | **完全沿用**：无新增标记语义、无新增里程碑、无新增缺陷等级 |

## 附录 A · 用例号速查

| 分组 | 区间 | 条数 | 主要内容 |
|---|---|---:|---|
| U1 品牌一致性 | UX-TC-001~006 | 6 | 令牌取样 / 圆角 / 零硬编码 / 等宽数字 / 语义色双通道 / 无 emoji 图标 |
| U2 对比度 | UX-TC-007~014 | 8 | 深色 / 浅色 / 非文字 / 焦点环 / 状态色 / 大字例外 / 浅色禁铜金 / 舞台固定色 |
| U3 首屏 | UX-TC-015~018 | 4 | Win 冷启动 / And 冷启动 / 骨架屏 / 自检不阻塞 |
| U4 连接可定位 | UX-TC-019~026 | 8 | 四阶段逐段失败 / 阶段视觉 / 可用性测试 / 文案三件套 |
| U5 零流量可见 | UX-TC-027~030 | 4 | 点击可达 / 数据真实 / 负向告警 / 禁无证据声明 |
| U6 chrome 隐藏 | UX-TC-031~035 | 5 | 自动隐藏 / 唤出 / 不重排 / 常驻条 / 手势边界 |
| U7 被控授权 | UX-TC-036~043 | 8 | 四项信息 / 最小权限 / 不可误关 / 始终允许治理 / 拒绝闭环 / ≤1s / 常驻提示 / 核对码 |
| U8 断线恢复 | UX-TC-044~050 | 7 | 不黑屏 / 退避 / 恢复 / 4 类原因 / 注入落点 / 逃生 / 释放 |
| U9 键盘可达 | UX-TC-051~055 | 5 | 全键盘任务 / 焦点环 / Tab 序 / 快捷键 / 读屏标签 |
| U10 文案 | UX-TC-056~060 | 5 | 术语 / 无英文残留 / 数字单位 / 中英切换 / 错误结构 |
| **U11 绝对模式**（v1.1） | **UX-TC-061~079** | **19** | 四选一默认均衡 / 副行文案逐字对 §7.6.2 / 滑杆折叠 / 3s 生效 / HUD 实测值 + §7.4.0 派生阈值 / 前置置灰 + 物理原因 + 两个出路 / 置灰不可点穿且只移焦点 / degraded 横幅写明取舍 / 无硬编显式提示 / 2K·4K 双前置 / 术语 7 词 / 布局 / 不遮挡画面 / **无「自适应」开关** / **静默降级探测器** / 横幅形态（非 toast + 恢复 ok toast） / 无障碍与 i18n / 反模式六条 |
| **合计** | **UX-TC-001~079** | **79** | U1~U11 共 11 组（[A] 63 / [M] 16） |

## 附录 B · 缺陷编号反查（本文引用到的现状缺陷）

| 缺陷主题 | 缺陷号 | `06` 位置（证据行） | 涉及用例 | 回归条目 |
|---|---|---|---|---|
| 品牌/色板/圆角 | X-01、X-21、X-02、X-06 | §1.1（引 `Theme.kt:208` `CORNER_RADIUS = 4`） | TC-001/002/003/007/008/009/013/014 | REG-13、REG-14 |
| 字体 / 图标 | X-04、X-08、X-19 | §1.1 | TC-004/006 | — |
| 状态反馈 / 空态 / 冷启动 | X-09、X-12、X-14、X-20 | §1.1（引 `web/styles.css:57-72`） | TC-005/015/016/017/019/023/024/028/047 | REG-09~12 |
| 文案与 Web 状态复位 | X-13、Y-15 | §1.1 / §1.2（引 `web/app.js:130-141,198-200`） | TC-026/040/046/048/057/058/060 | REG-08 |
| **「假状态」三兄弟** | Y-09、Y-10、§1.4-③ | §1.4（引 `ShareViewModel.cs:132`） | TC-023 | REG-01、REG-02、REG-03 |
| 零流量无证据 | Y-02 | §1.2 | TC-027/028/030 | REG-15 |
| 发现 / 返回 / 托盘 / 硬件 | Y-11、Y-12、Y-13、Y-14 | §1.2 | TC-019 | REG-04~07 |

## 附录 C · 本文的证据与实测说明

| 主题 | 证据 |
|---|---|
| 设计依据 / 标记与报告格式 | `docs/redesign/06-uiux-redesign.md`（v1.0，只读引用，未修改）；`docs/redesign/05-test-strategy.md` §1.2、§13.5 |
| 测试基础设施现状 | `tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj:12-19`（Playwright 1.48.0 / xunit 2.9.2 / FluentAssertions 7.0.0，**无 FlaUI/WinAppDriver**，实测 grep 命中 0）；channel 要求见 `Fixtures/PlaywrightFixture.cs:22-50`、`CLAUDE.md:23-24` |
| Android 包名/入口 | `android/app/build.gradle.kts:8,12`（`namespace`/`applicationId` = `com.unilinker.android`）；`AndroidManifest.xml:26`（`.MainActivity`） |
| 现状缺陷源码行 | 本文引用处均标 `文件:行号`，其中 `Theme.kt:68,208`、`ShareViewModel.cs:132`、`web/app.js:130-141,198-200`、`web/styles.css:57-72` 经本轮 Read 实测确认 |
| 对比度预核算 | 按 WCAG 2.2 相对亮度公式手算（sRGB 分量线性化阈值 0.04045、权重 0.2126/0.7152/0.0722），结果见 §3.7；**以脚本输出为最终依据** |

> **状态**：**v1.1**（2026-11-14）。v1.1 只做一件事：按 **`06` §7.6（模式选择器，已落地）** 补 **U11 绝对模式（UX-TC-061~079，19 条：[A] 15 / [M] 4）**，判据显式挂到 `06` §9 **U11**、任务挂 **UI-09**、HUD 阈值挂 §7.4.0、文案逐字对 §7.6.2、术语对 §8.2（7 词）；并连带更新 §1.2（新增 R7）、§2 总览、§5.3 M5 出口、§5.4 报告模板、§7 追溯、§6 盲点 B-9、附录 A。v1.0（2026-11-12）的 U1~U10、REG-01~15、脚本声明**一字未改**。本文未修改 `06`/`05`/`03`/`04`、未改任何源码、未做 git 操作。
> **U11 的生效前提**：模式规格以 C-24 为准；`06` 一旦补齐模式选择器章节，**只允许细化用例步骤，不允许放宽判据**（尤其「不可达必须前置置灰」与「HUD 必须显示实测值」两条）。
> **生效前提**：`06` 为唯一设计依据；若 `06` 修订，`06` §3.1 令牌表、§6 F1~F6、§9 U1~U10 的变更须同步回本文（§3.1 脚本常量、§2 用例、§5 出口）。
> **待主理人分派**：§3.7 的 **F-1**（`06` §3.1 四处对比度标注与实际不符，其中 3 处构成实际不达标）、**F-2**（浅色主题缺 6 个令牌）、**F-3**（无 UI 自动化基础设施，建议引入 FlaUI）——三项均属 `06`/依赖侧，按边界本文不改，请裁决责任人与里程碑。
