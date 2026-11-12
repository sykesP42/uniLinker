# 07 · 三端 UI 组件级规格与令牌骨架

> 本文是 `06-uiux-redesign.md` 的**代码级落地层**：`06` 定"长什么样、为什么"，本文定"写成什么、放哪个文件、怎么验证"。
> 唯一设计依据：`06`（**本文不得修改 `06`，与 `06` 冲突以 `06` 为准**）。协议与状态契约：`03` §2 / §11（**以 `03` 为准**）。实现约束：`04`。

## 0. 阅读指引与硬约束

### 0.1 与既有文档的关系

| 文档 | 本文如何依赖它 | 冲突时以谁为准 |
|---|---|---|
| `03` §2 | 共享类型（`TransportState`/`SessionSnapshot`/`UiLinkMetrics`）；本文所有组件的状态入参**只允许**取自 §2.6 与 §11.2 | **`03`** |
| `03` §11.3 | UI 五态真值表。本文 `StatusDot`/`LinkHUD`/`ErrorBanner` 的状态映射**逐条照抄**，不新增态 | **`03`** |
| `03` §11.4 | 三端文件落位表。本文 A 部分的三个令牌文件路径与它逐字一致 | **`03`** |
| `03` §11.5 | `ModernMainScreen.kt` 拆分边界。本文 §D.2 只在它之上补"行号→去向"映射 | **`03`** |
| `06` §3 | 令牌数值唯一来源。本文任何十六进制/字号/间距**不得自造** | **`06`** |
| `06` §7 | 12 个组件的视觉与状态摘要，本文是其展开 | **`06`** |
| `04` | 构建命令、WGC/编码踩坑、`WindowsPackageType=None` 等实现约束 | `04` |
| `05` | UI 验收（U1~U10）执行细则 | `05` |

### 0.2 三条本文自加的硬约束（供 QA 直接判定）

1. **`07` 不含任何协议字段**。组件只消费 `UiConnectionState` / `UiLinkMetrics` / `SessionSnapshot`；若某组件需要新数据，先在 `03` §2.5 增补 optional 字段，**不得**在 UI 层私造（`03` §11.1 R-UI-4）。
2. **`07` 的代码片段是可粘贴骨架，不是示例**。所有文件路径均已给出；片段**无省略号、无占位分支、无"稍后实现"**，粘贴即可编译（前提：同节声明的 `import` 齐全）。
3. **一个令牌一个真源**。三端令牌文件里的值必须字面相等（§A.5 有可执行的校验清单）。业务代码中出现十六进制常量即违规（`06` §3 首句）。

---

# A. 三端令牌文件（可直接落地的骨架）

## A.1 令牌清单与三端命名映射

命名前缀统一：Windows `Ul`、Android `Ul`、Web `--ul-`（`06` §3.4 末注）。**旧常量（`Copper`/`BackgroundDeep`/`IndustrialDesign`）保留一版并标 `@Deprecated`，S2 阶段删除**（见 §E.1）。

### A.1.1 色彩（深色为默认值）

| 语义令牌（`06` §3.1 行号） | WinUI | Android | CSS | 深色值 |
|---|---|---|---|---|
| `color.canvas` (:164) | `UlColorCanvas` | `UlColors.canvas` | `--ul-color-canvas` | `#121210` |
| `color.surface` (:165) | `UlColorSurface` | `.surface` | `--ul-color-surface` | `#1C1C18` |
| `color.surface-raised` (:166) | `UlColorSurfaceRaised` | `.surfaceRaised` | `--ul-color-surface-raised` | `#262620` |
| `color.surface-sunken` (:167) | `UlColorSurfaceSunken` | `.surfaceSunken` | `--ul-color-surface-sunken` | `#0C0C0A` |
| `color.border-subtle` (:168) | `UlColorBorderSubtle` | `.borderSubtle` | `--ul-color-border-subtle` | `#2A2A26` |
| `color.border-strong` (:169) | `UlColorBorderStrong` | `.borderStrong` | `--ul-color-border-strong` | `#3A3A34` |
| `color.text-primary` (:170) | `UlColorTextPrimary` | `.textPrimary` | `--ul-color-text-primary` | `#E8E6E0` |
| `color.text-secondary` (:171) | `UlColorTextSecondary` | `.textSecondary` | `--ul-color-text-secondary` | `#B8B6B0` |
| `color.text-tertiary` (:172) | `UlColorTextTertiary` | `.textTertiary` | `--ul-color-text-tertiary` | `#8A8880` |
| `color.accent` (:173) | `UlColorAccent` | `.accent` | `--ul-color-accent` | `#C49A6C` |
| `color.accent-hover` (:174) | `UlColorAccentHover` | `.accentHover` | `--ul-color-accent-hover` | `#D6AE7E` |
| `color.accent-press` (:175) | `UlColorAccentPress` | `.accentPress` | `--ul-color-accent-press` | `#AC8557` |
| `color.on-accent` (:176) | `UlColorOnAccent` | `.onAccent` | `--ul-color-on-accent` | `#1A1A1A` |
| `color.success` (:177) | `UlColorSuccess` | `.success` | `--ul-color-success` | `#7BA05B` |
| `color.warning` (:178) | `UlColorWarning` | `.warning` | `--ul-color-warning` | `#D4A84B` |
| `color.danger` (:179) | `UlColorDanger` | `.danger` | `--ul-color-danger` | `#E85D4C` |
| `color.focus-ring` (:180) | `UlColorFocusRing` | `.focusRing` | `--ul-color-focus-ring` | `#C49A6C` |

> 与现状的**唯一**差异：`text-tertiary` 由 `#7A7872`（`Theme.kt:40`，4.2:1）上调为 `#8A8880`（5.2:1，`06` X-05 修正）。S1 的像素级回归必须**豁免**这一处（§E.2）。

### A.1.2 排版

| 语义令牌 | Windows（px / 行高） | Android 角色 | Web（px） | 字重 |
|---|---|---|---|---|
| `text.display` | `UlFontSizeDisplay` 28 / 36 | `headlineMedium` 28/36 | 24 / 32 | 600 |
| `text.title` | `UlFontSizeTitle` 20 / 28 | `titleLarge` 22/28 | 18 / 24 | 600 |
| `text.subtitle` | `UlFontSizeSubtitle` 16 / 22 | `titleMedium` 16/24 | 16 / 22 | 600 |
| `text.body` | `UlFontSizeBody` 14 / 20 | `bodyMedium` 14/20 | 14 / 20 | 400 |
| `text.body-strong` | 同上 14 / 20 | `bodyMedium` 14/20 | 14 / 20 | 600 |
| `text.caption` | `UlFontSizeCaption` 12 / 16 | `bodySmall` 12/16 | 12 / 16 | 400 |
| `text.mono` | `UlFontSizeMono` 13 / 20 | `bodySmall` 12/16 + `Monospace` | 13 / 18 | 500 |

> Web 的 `text.display` 取 24/32、`text.title` 取 18/24——这是 `06` §3.2 表格的给定值（Web 列独立于 Windows/Android 列，**不要**为了"统一"改成 28/20）。

### A.1.3 间距 / 圆角 / 层级 / 动效

| 类别 | Windows | Android | CSS | 值 |
|---|---|---|---|---|
| 间距 | `UlSpace4/8/12/16/24/32/48`（`Thickness`） | `UlSpace.x1..x12`（`Dp`） | `--ul-space-1..12` | 4/8/12/16/24/32/48 |
| 圆角 | `UlRadiusXs/Sm/Md/Lg/Full`（`CornerRadius`） | `UlRadius.xs..full` | `--ul-radius-xs..full` | 4/8/12/16/999 |
| 层级 | `UlElev1/2/3`（`ThemeShadow` 用，见 §A.2 注） | `UlElev.1/2/3`（`Dp` shadow elevation） | `--ul-elev-1..3`（`box-shadow`） | 见下 |
| 动效 | `UlMotionFast/Base/Panel/PageMs`（`x:Double` 毫秒） | `UlMotion.fast/base/panel/page`（`Int` ms） | `--ul-dur-fast..page` | 100/160/240/320 |
| 曲线 | `UlEaseStandard/Emphasized/Exit`（`x:String`） | `UlEase.standard/emphasized/exit`（`CubicBezierEasing`） | `--ul-ease-*` | 见下 |

`elev` 三端值（`06` §3.3 :246-248）：`1 = 0 1px 2px rgba(0,0,0,.24)`、`2 = 0 8px 24px rgba(0,0,0,.32)`、`3 = 0 16px 48px rgba(0,0,0,.40)`。
`ease`（:253-255）：`standard = cubic-bezier(0.2,0,0,1)`、`emphasized = cubic-bezier(0.05,0.7,0.1,1)`、`exit = cubic-bezier(0.3,0,1,1)`。

## A.2 Windows：`src/UniLinker.WinUI/Styles/Tokens.xaml`（新建）

**结构决策**：`ThemeDictionaries` 内**只放 `Color`**；`SolidColorBrush` 放在主题字典之外，用 `Color="{ThemeResource UlColorXxx}"` 引用。这样避免同一十六进制在 Dark/Light 各写两遍（4 份）导致漂移。
**回退方案**：若某控件在系统主题切换瞬间不刷新（`ThemeResource` 未重新解析），按 WinUI `generic.xaml` 的做法把 `SolidColorBrush` 也搬进两个 `ThemeDictionary` 内并写字面值，同时在 §A.5 增加"`Color` 与 `Brush` 十六进制一致"的校验（已列入）。

```xml
<!-- src/UniLinker.WinUI/Styles/Tokens.xaml -->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ═══ 主题色：Default = Dark（媒体场景默认），Light 跟随系统 ═══ -->
  <ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Default">
      <Color x:Key="UlColorCanvas">#FF121210</Color>
      <Color x:Key="UlColorSurface">#FF1C1C18</Color>
      <Color x:Key="UlColorSurfaceRaised">#FF262620</Color>
      <Color x:Key="UlColorSurfaceSunken">#FF0C0C0A</Color>
      <Color x:Key="UlColorBorderSubtle">#FF2A2A26</Color>
      <Color x:Key="UlColorBorderStrong">#FF3A3A34</Color>
      <Color x:Key="UlColorTextPrimary">#FFE8E6E0</Color>
      <Color x:Key="UlColorTextSecondary">#FFB8B6B0</Color>
      <Color x:Key="UlColorTextTertiary">#FF8A8880</Color>
      <Color x:Key="UlColorAccent">#FFC49A6C</Color>
      <Color x:Key="UlColorAccentHover">#FFD6AE7E</Color>
      <Color x:Key="UlColorAccentPress">#FFAC8557</Color>
      <Color x:Key="UlColorOnAccent">#FF1A1A1A</Color>
      <Color x:Key="UlColorSuccess">#FF7BA05B</Color>
      <Color x:Key="UlColorWarning">#FFD4A84B</Color>
      <Color x:Key="UlColorDanger">#FFE85D4C</Color>
      <Color x:Key="UlColorFocusRing">#FFC49A6C</Color>
      <!-- 🔶 N-1/N-2/N-3 见 §A.6 -->
      <Color x:Key="UlColorScrim">#66000000</Color>
      <Color x:Key="UlColorSkeletonBase">#FF1C1C18</Color>
      <Color x:Key="UlColorSkeletonSheen">#1AFFFFFF</Color>
    </ResourceDictionary>
    <ResourceDictionary x:Key="Light">
      <Color x:Key="UlColorCanvas">#FFF4F2EE</Color>
      <Color x:Key="UlColorSurface">#FFFFFFFF</Color>
      <Color x:Key="UlColorSurfaceRaised">#FFFFFFFF</Color>
      <Color x:Key="UlColorSurfaceSunken">#FFE8E5DF</Color>
      <Color x:Key="UlColorBorderSubtle">#FFE0DCD4</Color>
      <Color x:Key="UlColorBorderStrong">#FFC9C4BA</Color>
      <Color x:Key="UlColorTextPrimary">#FF1A1A18</Color>
      <Color x:Key="UlColorTextSecondary">#FF55534E</Color>
      <Color x:Key="UlColorTextTertiary">#FF75726B</Color>
      <Color x:Key="UlColorAccent">#FF8A6A42</Color>
      <Color x:Key="UlColorAccentHover">#FF7A5C38</Color>
      <Color x:Key="UlColorAccentPress">#FF6B4E2E</Color>
      <Color x:Key="UlColorOnAccent">#FFFFFFFF</Color>
      <Color x:Key="UlColorSuccess">#FF4C7A32</Color>
      <Color x:Key="UlColorWarning">#FF8C6A17</Color>
      <Color x:Key="UlColorDanger">#FFB23B2C</Color>
      <Color x:Key="UlColorFocusRing">#FF8A6A42</Color>
      <Color x:Key="UlColorScrim">#66000000</Color>
      <Color x:Key="UlColorSkeletonBase">#FFE8E5DF</Color>
      <Color x:Key="UlColorSkeletonSheen">#33FFFFFF</Color>
    </ResourceDictionary>
  </ResourceDictionary.ThemeDictionaries>

  <!-- ═══ 画刷：引用上面的 Color，随主题刷新 ═══ -->
  <SolidColorBrush x:Key="UlBrushCanvas"         Color="{ThemeResource UlColorCanvas}" />
  <SolidColorBrush x:Key="UlBrushSurface"        Color="{ThemeResource UlColorSurface}" />
  <SolidColorBrush x:Key="UlBrushSurfaceRaised"  Color="{ThemeResource UlColorSurfaceRaised}" />
  <SolidColorBrush x:Key="UlBrushSurfaceSunken"  Color="{ThemeResource UlColorSurfaceSunken}" />
  <SolidColorBrush x:Key="UlBrushBorderSubtle"   Color="{ThemeResource UlColorBorderSubtle}" />
  <SolidColorBrush x:Key="UlBrushBorderStrong"   Color="{ThemeResource UlColorBorderStrong}" />
  <SolidColorBrush x:Key="UlBrushTextPrimary"    Color="{ThemeResource UlColorTextPrimary}" />
  <SolidColorBrush x:Key="UlBrushTextSecondary"  Color="{ThemeResource UlColorTextSecondary}" />
  <SolidColorBrush x:Key="UlBrushTextTertiary"   Color="{ThemeResource UlColorTextTertiary}" />
  <SolidColorBrush x:Key="UlBrushAccent"         Color="{ThemeResource UlColorAccent}" />
  <SolidColorBrush x:Key="UlBrushAccentHover"    Color="{ThemeResource UlColorAccentHover}" />
  <SolidColorBrush x:Key="UlBrushAccentPress"    Color="{ThemeResource UlColorAccentPress}" />
  <SolidColorBrush x:Key="UlBrushOnAccent"       Color="{ThemeResource UlColorOnAccent}" />
  <SolidColorBrush x:Key="UlBrushSuccess"        Color="{ThemeResource UlColorSuccess}" />
  <SolidColorBrush x:Key="UlBrushWarning"        Color="{ThemeResource UlColorWarning}" />
  <SolidColorBrush x:Key="UlBrushDanger"         Color="{ThemeResource UlColorDanger}" />
  <SolidColorBrush x:Key="UlBrushFocusRing"      Color="{ThemeResource UlColorFocusRing}" />
  <SolidColorBrush x:Key="UlBrushScrim"          Color="{ThemeResource UlColorScrim}" />
  <SolidColorBrush x:Key="UlBrushSkeletonBase"   Color="{ThemeResource UlColorSkeletonBase}" />

  <!-- ═══ 字号（06 §3.2 Windows 列）═══ -->
  <x:Double x:Key="UlFontSizeDisplay">28</x:Double>
  <x:Double x:Key="UlFontSizeTitle">20</x:Double>
  <x:Double x:Key="UlFontSizeSubtitle">16</x:Double>
  <x:Double x:Key="UlFontSizeBody">14</x:Double>
  <x:Double x:Key="UlFontSizeCaption">12</x:Double>
  <x:Double x:Key="UlFontSizeMono">13</x:Double>
  <x:Double x:Key="UlLineHeightDisplay">36</x:Double>
  <x:Double x:Key="UlLineHeightTitle">28</x:Double>
  <x:Double x:Key="UlLineHeightSubtitle">22</x:Double>
  <x:Double x:Key="UlLineHeightBody">20</x:Double>
  <x:Double x:Key="UlLineHeightCaption">16</x:Double>
  <x:Double x:Key="UlLineHeightMono">20</x:Double>

  <!-- ═══ 字体族：严禁外链，全部系统内置（06 §3.2 :227）═══ -->
  <FontFamily x:Key="UlFontFamilyDisplay">Segoe UI Variable Display</FontFamily>
  <FontFamily x:Key="UlFontFamilyText">Segoe UI Variable Text</FontFamily>
  <FontFamily x:Key="UlFontFamilyMono">Cascadia Mono, Consolas</FontFamily>

  <!-- ═══ 间距（Thickness）═══ -->
  <Thickness x:Key="UlSpace4">4</Thickness>
  <Thickness x:Key="UlSpace8">8</Thickness>
  <Thickness x:Key="UlSpace12">12</Thickness>
  <Thickness x:Key="UlSpace16">16</Thickness>
  <Thickness x:Key="UlSpace24">24</Thickness>
  <Thickness x:Key="UlSpace32">32</Thickness>
  <Thickness x:Key="UlSpace48">48</Thickness>
  <x:Double x:Key="UlSpace4Value">4</x:Double>
  <x:Double x:Key="UlSpace8Value">8</x:Double>
  <x:Double x:Key="UlSpace12Value">12</x:Double>
  <x:Double x:Key="UlSpace16Value">16</x:Double>
  <x:Double x:Key="UlSpace24Value">24</x:Double>

  <!-- ═══ 圆角（06 §3.3 :238-242）═══ -->
  <CornerRadius x:Key="UlRadiusXs">4</CornerRadius>
  <CornerRadius x:Key="UlRadiusSm">8</CornerRadius>
  <CornerRadius x:Key="UlRadiusMd">12</CornerRadius>
  <CornerRadius x:Key="UlRadiusLg">16</CornerRadius>
  <CornerRadius x:Key="UlRadiusFull">999</CornerRadius>

  <!-- ═══ 尺寸（🔶 N-4 见 §A.6）═══ -->
  <x:Double x:Key="UlSizeDot">8</x:Double>
  <x:Double x:Key="UlSizeControlDesktop">40</x:Double>
  <x:Double x:Key="UlSizeControlMobile">44</x:Double>
  <x:Double x:Key="UlSizeIconEmpty">64</x:Double>
  <x:Double x:Key="UlSizeTouchMin">48</x:Double>

  <!-- ═══ 动效：毫秒数值（x:Double），Storyboard 处由代码折算 ═══ -->
  <!-- 决策：WinUI 3 未对 <Duration> 提供可靠 XAML 资源实例化，故统一用 ms 数值 + 代码转换 -->
  <x:Double x:Key="UlMotionFastMs">100</x:Double>
  <x:Double x:Key="UlMotionBaseMs">160</x:Double>
  <x:Double x:Key="UlMotionPanelMs">240</x:Double>
  <x:Double x:Key="UlMotionPageMs">320</x:Double>
  <x:Double x:Key="UlMotionBreatheMs">1600</x:Double>
  <x:Double x:Key="UlMotionShimmerMs">1500</x:Double>

  <!-- ═══ 缓动曲线：字符串令牌，唯一允许的字面量例外（见 §A.6 白名单）═══ -->
  <x:String x:Key="UlEaseStandard">0.2,0,0,1</x:String>
  <x:String x:Key="UlEaseEmphasized">0.05,0.7,0.1,1</x:String>
  <x:String x:Key="UlEaseExit">0.3,0,1,1</x:String>
</ResourceDictionary>
```

**接线（同一批改动，S1 阶段做）**：`App.xaml` 的 `MergedDictionaries` 中把 `Tokens.xaml` 排在 `AppStyles.xaml` **之前**；`AppStyles.xaml` 内 6 个 `Style` 的 `{ThemeResource CardBackgroundFillColorDefaultBrush}` 等系统键替换为 `UlBrush*`（`AppStyles.xaml:7-11` 即 X-21 证据）。

## A.3 Android：`ui/theme/Tokens.kt`（新建）+ `Theme.kt` 瘦身

**分工**：`Tokens.kt` 只放**值**（`UlColors` + `UlTypography` + 尺寸/动效常量 + `LocalUlColors`）；`Theme.kt` 只留 `UniLinkerTheme {}` 组合入口与 M3 `ColorScheme` 装配。

```kotlin
// android/app/src/main/java/com/unilinker/android/ui/theme/Tokens.kt
package com.unilinker.android.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.ColorScheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** 非 M3 令牌（06 §3.1 深色列）。M3 角色由 [ulDarkScheme]/[ulLightScheme] 承担。 */
data class UlColors(
    val canvas: Color,
    val surface: Color,
    val surfaceRaised: Color,
    val surfaceSunken: Color,
    val borderSubtle: Color,
    val borderStrong: Color,
    val textPrimary: Color,
    val textSecondary: Color,
    val textTertiary: Color,
    val accent: Color,
    val accentHover: Color,
    val accentPress: Color,
    val onAccent: Color,
    val success: Color,
    val warning: Color,
    val danger: Color,
    val focusRing: Color,
    val scrim: Color,          // 🔶 N-1
    val skeletonBase: Color,   // 🔶 N-2
    val skeletonSheen: Color,  // 🔶 N-2
)

val ulDarkColors = UlColors(
    canvas = Color(0xFF121210), surface = Color(0xFF1C1C18),
    surfaceRaised = Color(0xFF262620), surfaceSunken = Color(0xFF0C0C0A),
    borderSubtle = Color(0xFF2A2A26), borderStrong = Color(0xFF3A3A34),
    textPrimary = Color(0xFFE8E6E0), textSecondary = Color(0xFFB8B6B0),
    textTertiary = Color(0xFF8A8880),
    accent = Color(0xFFC49A6C), accentHover = Color(0xFFD6AE7E),
    accentPress = Color(0xFFAC8557), onAccent = Color(0xFF1A1A1A),
    success = Color(0xFF7BA05B), warning = Color(0xFFD4A84B), danger = Color(0xFFE85D4C),
    focusRing = Color(0xFFC49A6C), scrim = Color(0x66000000),
    skeletonBase = Color(0xFF1C1C18), skeletonSheen = Color(0x1AFFFFFF),
)

val ulLightColors = UlColors(
    canvas = Color(0xFFF4F2EE), surface = Color(0xFFFFFFFF),
    surfaceRaised = Color(0xFFFFFFFF), surfaceSunken = Color(0xFFE8E5DF),
    borderSubtle = Color(0xFFE0DCD4), borderStrong = Color(0xFFC9C4BA),
    textPrimary = Color(0xFF1A1A18), textSecondary = Color(0xFF55534E),
    textTertiary = Color(0xFF75726B),
    accent = Color(0xFF8A6A42), accentHover = Color(0xFF7A5C38),
    accentPress = Color(0xFF6B4E2E), onAccent = Color(0xFFFFFFFF),
    success = Color(0xFF4C7A32), warning = Color(0xFF8C6A17), danger = Color(0xFFB23B2C),
    focusRing = Color(0xFF8A6A42), scrim = Color(0x66000000),
    skeletonBase = Color(0xFFE8E5DF), skeletonSheen = Color(0x33FFFFFF),
)

/** M3 全角色补齐（修 X-02/X-03/X-06）。未列角色由 M3 基线提供会污染成紫灰，故全部显式给值。 */
private fun ulScheme(c: UlColors, dark: Boolean): ColorScheme {
    val s = if (dark) darkColorScheme() else lightColorScheme()
    return s.copy(
        primary = c.accent, onPrimary = c.onAccent,
        primaryContainer = c.accent.copy(alpha = 0.15f), onPrimaryContainer = c.textPrimary,
        inversePrimary = c.accent,
        secondary = c.textSecondary, onSecondary = c.canvas,
        secondaryContainer = c.surfaceRaised, onSecondaryContainer = c.textPrimary,
        tertiary = c.accentPress, onTertiary = c.onAccent,
        tertiaryContainer = c.surfaceRaised, onTertiaryContainer = c.textPrimary,
        background = c.canvas, onBackground = c.textPrimary,
        surface = c.surface,
        onSurface = c.textPrimary,                 // X-02 修复：主文字，不是次文字
        surfaceVariant = c.surfaceRaised, onSurfaceVariant = c.textSecondary,
        surfaceTint = c.accent,
        surfaceBright = if (dark) Color(0xFF303029) else Color(0xFFFFFFFF),
        surfaceDim = if (dark) Color(0xFF0C0C0A) else Color(0xFFE8E5DF),
        surfaceContainerLowest = if (dark) Color(0xFF0C0C0A) else Color(0xFFFFFFFF),
        surfaceContainerLow = c.canvas, surfaceContainer = c.surface,
        surfaceContainerHigh = c.surfaceRaised, surfaceContainerHighest = c.surfaceRaised,
        outline = c.borderStrong, outlineVariant = c.borderSubtle,
        error = c.danger, onError = c.onAccent,
        errorContainer = c.danger.copy(alpha = 0.15f), onErrorContainer = c.danger,
        inverseSurface = c.textPrimary, inverseOnSurface = c.canvas,
        scrim = c.scrim,
    )
}

val ulDarkScheme: ColorScheme = ulScheme(ulDarkColors, dark = true)
val ulLightScheme: ColorScheme = ulScheme(ulLightColors, dark = false)

/** 语义字号（06 §3.2 Android 列）。数值取自 M3 十五档，不新增第四套阶梯。 */
private fun ulText(size: Int, line: Int, weight: FontWeight, mono: Boolean = false) =
    TextStyle(
        fontFamily = if (mono) FontFamily.Monospace else FontFamily.Default,
        fontWeight = weight, fontSize = size.sp, lineHeight = line.sp,
    )

val ulTypography = Typography(
    displayLarge = ulText(57, 64, FontWeight.Normal),
    displayMedium = ulText(45, 52, FontWeight.Normal),
    displaySmall = ulText(36, 44, FontWeight.Normal),
    headlineLarge = ulText(32, 40, FontWeight.SemiBold),   // X-05：原 18sp 上调
    headlineMedium = ulText(28, 36, FontWeight.SemiBold),  // = text.display
    headlineSmall = ulText(24, 32, FontWeight.SemiBold),
    titleLarge = ulText(22, 28, FontWeight.SemiBold),      // = text.title
    titleMedium = ulText(16, 24, FontWeight.SemiBold),     // = text.subtitle
    titleSmall = ulText(14, 20, FontWeight.SemiBold),
    bodyLarge = ulText(16, 24, FontWeight.Normal),
    bodyMedium = ulText(14, 20, FontWeight.Normal),        // = text.body / body-strong
    bodySmall = ulText(12, 16, FontWeight.Normal),         // = text.caption
    labelLarge = ulText(14, 20, FontWeight.SemiBold),
    labelMedium = ulText(12, 16, FontWeight.SemiBold),
    labelSmall = ulText(11, 16, FontWeight.Medium),        // X-05：原 10sp 上调
)

/** 数值专用：等宽 + tabular（修 X-04，06 §3.2 :231） */
val UlTextMono: TextStyle = ulText(13, 20, FontWeight.Medium, mono = true)

object UlSpace {
    val x1 = 4.dp; val x2 = 8.dp; val x3 = 12.dp; val x4 = 16.dp
    val x6 = 24.dp; val x8 = 32.dp; val x12 = 48.dp
}

object UlRadius {
    val xs = 4.dp; val sm = 8.dp; val md = 12.dp; val lg = 16.dp; val full = 999.dp
}

object UlSize {          // 🔶 N-4
    val dot = 8.dp
    val controlDesktop = 40.dp
    val controlMobile = 44.dp
    val iconEmpty = 64.dp
    val touchMin = 48.dp
}

object UlMotion {
    const val fast = 100; const val base = 160
    const val panel = 240; const val page = 320
    const val breathe = 1600; const val shimmer = 1500   // 🔶 N-5
}

object UlEase {
    val standard = androidx.compose.animation.core.CubicBezierEasing(0.2f, 0f, 0f, 1f)
    val emphasized = androidx.compose.animation.core.CubicBezierEasing(0.05f, 0.7f, 0.1f, 1f)
    val exit = androidx.compose.animation.core.CubicBezierEasing(0.3f, 0f, 1f, 1f)
}

val LocalUlColors = staticCompositionLocalOf { ulDarkColors }

/** Theme.kt 瘦身后仅剩此入口（06 §3.4） */
@Composable
fun UniLinkerTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val colors = if (darkTheme) ulDarkColors else ulLightColors
    CompositionLocalProvider(LocalUlColors provides colors) {
        androidx.compose.material3.MaterialTheme(
            colorScheme = if (darkTheme) ulDarkScheme else ulLightScheme,
            typography = ulTypography,
            content = content,
        )
    }
}
```

**同名迁移（`Theme.kt:29-49` → `Tokens.kt`）**：`Copper → ulDarkColors.accent`、`BackgroundDeep → .canvas`、`BackgroundSurface → .surface`、`BackgroundVariant → .surfaceRaised`、`BorderWeak → .borderSubtle`、`StatusError/Warning/Success → .danger/.warning/.success`。旧 `val` 保留但加 `@Deprecated("Use LocalUlColors", ReplaceWith("LocalUlColors.current.canvas"))`，S2 末删除。
**`themes.xml` 同步（X-20）**：`res/values/themes.xml:3-7` 启动底色 `#0D0D0D` → `#121210`，与 `canvas` 一致，消除冷启动闪色。

## A.4 Web：`web/tokens.css`（新建）

**接入顺序（S1）**：`index.html` 的 `<head>` 中 `tokens.css` 必须在 `styles.css` **之前**；随后把 `index.html:8-175` 的内联 `<style>` 逐条迁入 `tokens.css`（X-18）或删除。**禁止任何 `@import`/`@font-face` 外链**（`06` §3.2 :221-224，离线局域网会阻塞首屏）。

```css
/* web/tokens.css —— 三端令牌的 Web 端真源。禁止外链字体。 */
:root {
  /* 色彩（06 §3.1 深色列，默认） */
  --ul-color-canvas: #121210;
  --ul-color-surface: #1C1C18;
  --ul-color-surface-raised: #262620;
  --ul-color-surface-sunken: #0C0C0A;
  --ul-color-border-subtle: #2A2A26;
  --ul-color-border-strong: #3A3A34;
  --ul-color-text-primary: #E8E6E0;
  --ul-color-text-secondary: #B8B6B0;
  --ul-color-text-tertiary: #8A8880;
  --ul-color-accent: #C49A6C;
  --ul-color-accent-hover: #D6AE7E;
  --ul-color-accent-press: #AC8557;
  --ul-color-on-accent: #1A1A1A;
  --ul-color-success: #7BA05B;
  --ul-color-warning: #D4A84B;
  --ul-color-danger: #E85D4C;
  --ul-color-focus-ring: #C49A6C;
  /* 🔶 N-1/N-2：06 未给令牌名，见 §A.6 */
  --ul-color-scrim: rgba(0, 0, 0, .40);
  --ul-color-skeleton-base: #1C1C18;
  --ul-color-skeleton-sheen: rgba(255, 255, 255, .10);

  /* 字体族（系统字体，含 CJK 回落 —— 修 X-08） */
  --ul-font-sans: -apple-system, "Segoe UI Variable Text", "Segoe UI", Roboto,
                  "Helvetica Neue", "PingFang SC", "Microsoft YaHei", sans-serif;
  --ul-font-mono: ui-monospace, "Cascadia Mono", Consolas, monospace;
  --ul-font-display: "Segoe UI Variable Display", var(--ul-font-sans);

  /* 排版（06 §3.2 Web 列） */
  --ul-text-display: 24px/32px;   /* 配合 font-weight 600 使用 */
  --ul-text-title: 18px/24px;
  --ul-text-subtitle: 16px/22px;
  --ul-text-body: 14px/20px;
  --ul-text-caption: 12px/16px;
  --ul-text-mono: 13px/18px;

  /* 间距 */
  --ul-space-1: 4px;  --ul-space-2: 8px;  --ul-space-3: 12px; --ul-space-4: 16px;
  --ul-space-6: 24px; --ul-space-8: 32px; --ul-space-12: 48px;

  /* 圆角 */
  --ul-radius-xs: 4px;  --ul-radius-sm: 8px;  --ul-radius-md: 12px;
  --ul-radius-lg: 16px; --ul-radius-full: 999px;

  /* 层级 */
  --ul-elev-1: 0 1px 2px rgba(0, 0, 0, .24);
  --ul-elev-2: 0 8px 24px rgba(0, 0, 0, .32);
  --ul-elev-3: 0 16px 48px rgba(0, 0, 0, .40);

  /* 动效 */
  --ul-dur-fast: 100ms; --ul-dur-base: 160ms;
  --ul-dur-panel: 240ms; --ul-dur-page: 320ms;
  --ul-dur-breathe: 1600ms; --ul-dur-shimmer: 1500ms;   /* 🔶 N-5 */
  --ul-ease-standard: cubic-bezier(0.2, 0, 0, 1);
  --ul-ease-emphasized: cubic-bezier(0.05, 0.7, 0.1, 1);
  --ul-ease-exit: cubic-bezier(0.3, 0, 1, 1);

  /* 尺寸（🔶 N-4） */
  --ul-size-dot: 8px;
  --ul-size-control: 40px;      /* 桌面；移动端媒体查询覆盖为 44px */
  --ul-size-icon-empty: 64px;
  --ul-size-touch-min: 48px;
  --ul-size-device-card-min-h: 72px;

  /* 层级顺序（🔶 N-7：Web 需要显式 stacking order，三端对齐） */
  --ul-z-hud: 10; --ul-z-toolbar: 20; --ul-z-sheet: 100; --ul-z-toast: 1000;
}

@media (prefers-color-scheme: light) {
  :root {
    --ul-color-canvas: #F4F2EE;
    --ul-color-surface: #FFFFFF;
    --ul-color-surface-raised: #FFFFFF;
    --ul-color-surface-sunken: #E8E5DF;
    --ul-color-border-subtle: #E0DCD4;
    --ul-color-border-strong: #C9C4BA;
    --ul-color-text-primary: #1A1A18;
    --ul-color-text-secondary: #55534E;
    --ul-color-text-tertiary: #75726B;
    --ul-color-accent: #8A6A42;     /* 浅色下铜金必须加深（06 §3.1 :195） */
    --ul-color-accent-hover: #7A5C38;
    --ul-color-accent-press: #6B4E2E;
    --ul-color-on-accent: #FFFFFF;
    --ul-color-success: #4C7A32;
    --ul-color-warning: #8C6A17;
    --ul-color-danger: #B23B2C;
    --ul-color-focus-ring: #8A6A42;
    --ul-color-skeleton-base: #E8E5DF;
    --ul-color-skeleton-sheen: rgba(255, 255, 255, .20);
  }
}

@media (prefers-reduced-motion: reduce) {
  :root {
    --ul-dur-fast: 100ms; --ul-dur-base: 100ms;   /* 降级为 ≤100ms 透明度变化 */
    --ul-dur-panel: 100ms; --ul-dur-page: 100ms;
    --ul-dur-shimmer: 0ms;
  }
  .ul-skeleton { animation: none !important; }
  .ul-device-card, .ul-toast, .ul-error-banner { transition: none !important; animation: none !important; }
}

/* 焦点环：2px + 偏移 2px（06 §8.1 :635） */
:focus-visible { outline: 2px solid var(--ul-color-focus-ring); outline-offset: 2px; }

/* 数值等宽（修 X-04/X-09） */
.ul-num { font-family: var(--ul-font-mono); font-variant-numeric: tabular-nums; }

/* 全局基线 */
body {
  font-family: var(--ul-font-sans);
  background: var(--ul-color-canvas);
  color: var(--ul-color-text-primary);
  -webkit-font-smoothing: antialiased;
}
```

## A.5 三端一致性校验清单（可执行）

| # | 校验项 | 命令 / 判定 | 通过条件 |
|---|---|---|---|
| V1 | 令牌键齐全 | 分别取 `UlColor\w+`、`UlColors\.\w+`、`--ul-color-[\w-]+` 的键集，映射到 §A.1.1 的语义名后比对 | 三端语义名集合**完全相等**（缺失即失败） |
| V2 | 值相等 | 抽取三端同一语义名的十六进制，统一转 `#RRGGBB` 比对 | 全部相等；`Light` 分支同样比对 |
| V3 | 无字面量 | `grep -rnE '#[0-9A-Fa-f]{6,8}' src/UniLinker.WinUI/Views web/app.js android/app/src/main/java` | 命中数 = 0（`Tokens.*`/`tokens.css` 除外） |
| V4 | 无语号字面量 | `grep -rn 'FontSize=' src/UniLinker.WinUI/Views` 与 `grep -rn 'fontSize = [0-9]' android/.../ui` | 命中数 = 0（X-15 的 117 处、X-16 的 60 处清零） |
| V5 | 无外链字体 | `grep -rn 'fonts.googleapis\|@font-face\|cdn\.' web/` | 命中数 = 0 |
| V6 | Color 与 Brush 一致 | 若采纳 §A.2 回退方案，比对同一主题内 `UlColorX` 与 `UlBrushX` 的十六进制 | 全部相等 |
| V7 | `onSurface` 修正 | 检查 `ulScheme` 的 `onSurface` 绑定 | 必须绑 `textPrimary`（X-02） |

## A.6 建议新增令牌登记（已裁决：C-15 → `06` §3.5 的 T-1~T-8）

> **裁决结果（2026-11-12，C-15）**：N-1~N-8 全部采纳，已在 `06` §3.5 重编号为 **T-1~T-8** 并升为正式令牌。其中 **N-8 的 `success` / `warning` 未采纳本文候选值** —— `#4C7A32` 在 `#F4F2EE` 上实测仅 4.55:1、`#8C6A17` 仅 4.52:1，余量不足，已下调为 `#4A7530`（4.85:1）与 `#856312`（4.95:1）。下表保留为**登记溯源**，实现时数值一律以 `06` §3.5 为准。

| 编号 | 建议令牌 | 值 | 06 未覆盖的原因 | 本文如何使用 |
|---|---|---|---|---|
| N-1 | `color.scrim` | `rgba(0,0,0,.40)` | `06` §7.5 只写"遮罩 40%"，未给令牌名 | 底部弹层、被控授权遮罩 |
| N-2 | `color.skeleton-base` / `color.skeleton-sheen` | `#1C1C18` / `#1AFFFFFF` | `06` §7.5 只写"canvas 底 + shimmer" | 骨架屏（B.4） |
| N-3 | `color.<status>-soft` | 状态色 20% alpha | `06` §7.1 :547、§7.5 :616 用到 20% alpha，未给令牌 | 状态点外圈、危险按钮底 |
| N-4 | `size.dot`=8 / `size.control-desktop`=40 / `size.control-mobile`=44 / `size.icon-empty`=64 / `size.touch-min`=48 | 同左 | `06` 在多处给了数值但未纳入令牌表 | 组件尺寸，替代散落魔法数 |
| N-5 | `dur.breathe`=1600ms / `dur.shimmer`=1500ms | 同左 | `06` §7.1 :546、§7.5 :622 给了值未给令牌 | 状态点呼吸、骨架 shimmer |
| N-6 | `size.device-card-min-h`=72px | 72 | `06` §7.2 :556 写"高度 ≥72px" | 设备卡最小高度 |
| N-7 | `z.hud`/`z.toolbar`/`z.sheet`/`z.toast` | 10/20/100/1000 | 三端均有遮挡问题，`06` 未定义 stacking 顺序 | Web/Compose 的 `zIndex`、XAML `Canvas.ZIndex` |
| N-8 | 浅色分支补充 6 色：`accent-hover` `#7A5C38`、`accent-press` `#6B4E2E`、`success` `#4C7A32`、`warning` `#8C6A17`、`danger` `#B23B2C`、`focus-ring` `#8A6A42` | 同左 | `06` §3.1 浅色表（:184-196）只给了 11 个，缺这 6 个；直接沿用深色值在白底不达标（如 `warning #D4A84B` 在白底约 2.0:1） | 三个令牌文件的 Light 分支 |
| W-1 | 字面量白名单（唯一例外） | `UlEase*` 字符串令牌 | 缓动曲线无法作为 WinUI 资源类型安全实例化 | `Tokens.xaml` 末段 |

> 🔶 **N-8 的 6 个浅色值是按同色相调整明度得到的候选值，目标 ≥4.5:1**，**必须**用对比度工具实测复核后才可定稿；`05`/`08` 的对比度用例应把它们列入。这是本文唯一"非 `06` 来源"的数值，已在此集中登记，避免散落。

---

# B. 组件规格（12 个）

> 统一约定：**props 名三端同义**（WinUI 用 PascalCase 依赖属性、Compose 用 camelCase 参数、Web 用 data-attribute/JSON），因此下表只写语义名。
> `state` 一律取 `UiConnectionState` ∈ `{idle, connecting, connected, degraded, error}`（`03` §11.3），**任何组件不得自造第六态**。

## B.1 StatusDot 状态点

**语义**：把「会话处于什么状态」压缩成一个 8px 的点，**永远与文字标签同时出现**（`06` §7.1 :551）。
**统一 props**

| prop | 类型 | 必填 | 说明 | 默认 |
|---|---|---|---|---|
| `state` | `UiConnectionState` | ✅ | 唯一状态入参，来自 `ISessionStateSource.Ui` | — |
| `variant` | `default` / `reconnecting` / `failed` | ⬜ | `degraded` 的细分（`03` §11.3 :1211） | `default` |
| `label` | `string` | ✅ | 必须提供的可见文案；为空则**不渲染组件**（断言失败） | — |
| `liveRegion` | `bool` | ⬜ | 状态变化是否读屏播报 | `true` |

**状态表**（颜色/形态全部来自 `06` §7.1 :543-549 + `03` §11.3 真值表）

| `state` | `variant` | 颜色令牌 | 形态 | 动画 | 必须同时显示的文案 |
|---|---|---|---|---|---|
| `idle` | — | `text-tertiary` | 实心 8px | 无 | 未连接 / 已发现 · 未连接 / 会话已结束 |
| `connecting` | — | `accent` | 实心 8px | 呼吸 `dur.breathe`，opacity 1→0.35，`ease.standard` | 正在连接 / 配对中 / 正在建立会话 |
| `connected` | — | `success` | 实心 8px + 外圈 2px `success` 20% alpha | 无 | 已连接 / 已连接 · 正在投屏 |
| `degraded` | `default` | `warning` | 实心 8px | 无 | 画质降级 |
| `degraded` | `reconnecting` | `warning` | 实心 8px | **不呼吸** | 连接中断，正在重连 (n/5) |
| `error` | `failed` | `danger` | 实心 8px | 无 | 原因分类 |

**实现要点**

| 端 | 文件（`03` §11.4） | 关键实现 / 陷阱 |
|---|---|---|
| Windows | `Controls/StatusDot.xaml(.cs)` | `Ellipse Width/Height={StaticResource UlSizeDot}`；外圈用第二层 `Ellipse Stroke=UlBrushSuccess StrokeThickness=2` + `Opacity=0.2`；呼吸用 `Storyboard` + `DoubleAnimation` 在 `Opacity` 上 `RepeatBehavior="Forever"` + `AutoReverse="True"`，时长由 `UlMotionBreatheMs` 折算；`AutomationProperties.Name` 绑 `label` |
| Android | `ui/components/StatusDot.kt` | `Box(Modifier.size(UlSize.dot).background(color, CircleShape))`；呼吸用 `rememberInfiniteTransition` + `animateFloat(initialValue=1f, targetValue=.35f, animationSpec=infiniteRepeatable(tween(UlMotion.breathe), RepeatMode.Reverse))`；**`reconnecting` 必须跳过 Transition**（否则违反 `03` §11.3 :1211 的"不呼吸"） |
| Web | `web/app.js` + `tokens.css` | `<span class="ul-dot" data-state="connected">` + `::after` 伪元素做外圈；呼吸 `@keyframes ul-breathe`，`animation-duration: var(--ul-dur-breathe)`；`aria-live="polite"` 放在**文字**节点而非点上 |

**动画关键片段（Android，`reconnecting` 不呼吸的写法）**

```kotlin
@Composable
fun StatusDot(state: UiConnectionState, variant: DotVariant, label: String) {
    require(label.isNotBlank()) { "StatusDot 禁止无文案单独使用（06 §7.1）" }
    val c = LocalUlColors.current
    val color = when (state) {
        UiConnectionState.Idle -> c.textTertiary
        UiConnectionState.Connecting -> c.accent
        UiConnectionState.Connected -> c.success
        UiConnectionState.Degraded -> c.warning
        UiConnectionState.Error -> c.danger
    }
    val breathing = state == UiConnectionState.Connecting
        && variant != DotVariant.Reconnecting          // 03 §11.3: 重连不呼吸
    val alpha = if (breathing) {
        val t = rememberInfiniteTransition(label = "dot")
        t.animateFloat(1f, 0.35f, infiniteRepeatable(
            tween(UlMotion.breathe, easing = UlEase.standard), RepeatMode.Reverse)).value
    } else 1f
    StatusDotVisual(color, alpha, showRing = state == UiConnectionState.Connected)
}
```

**无障碍**：点本身 `semantics { invisibleToUser() }`（避免读屏念两次空节点）；文案节点 `liveRegion = LiveRegionMode.Polite`。
**反例**：只有一个红点没有文字；把 `reconnecting` 做成红灯（`03` §11.3 明确要求 `degraded` + 黄）；用指标（丢包）驱动点变色（R-UI-4，X-09 复活）。

## B.2 Button（4 变体）

**语义**：`primary` 推进一步、`secondary` 提供旁路、`danger` 只用于"断开/撤销授权"、`icon` 承载工具栏操作（`06` §7.5 :614-616）。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `variant` | `primary`/`secondary`/`danger`/`icon` | ✅ | 无 `default`；不确定就用 `secondary` |
| `label` | `string` | ✅（`icon` 除外） | 术语按 `06` §8.2 词汇表（投屏/观看/远控） |
| `icon` | `IconSource` | ⬜ | `icon` 变体必填 |
| `loading` | `bool` | ⬜ | **loading 时宽度不变**，仅图标换 16px spinner |
| `enabled` | `bool` | ⬜ | 禁用态 60% 不透明（`06` §7.2 同规则） |
| `accessibilityLabel` | `string` | `icon` 必填 | `AutomationProperties.Name` / `contentDescription` / `aria-label` |

**变体表**

| 变体 | 填充 | 描边 | 文字 | 尺寸 | 悬停/按下 |
|---|---|---|---|---|---|
| `primary` | `accent` 实心 | 无 | `on-accent` | 桌面 40 / 移动 44，`radius.sm` | `accent-hover` / `accent-press` |
| `secondary` | 透明 | `border-strong` 1px | `text-primary` | 同上 | 底 `surface-raised` |
| `danger` | `danger` 20% alpha | 无 | `danger`（同色文字） | 同上 | alpha→28% |
| `icon` | 透明 | 无 | — | 32×32 热区（`06` §8.1 桌面 ≥32），图标 16px | 底 `surface-raised` |

**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | 基于 `Button` 重模板（`ControlTemplate`），**不要**用 `Style` 只改颜色——WinUI 默认模板含 `ThemeResource` 的 hover/press 视觉状态，必须一并替换；`Icon` 变体用 `Button` + `Content=FontIcon`，`Padding` 归零后靠 `MinWidth/MinHeight=32` 保热区 |
| Android | `Button(shape = RoundedCornerShape(UlRadius.sm), contentPadding = PaddingValues(0.dp), modifier = Modifier.height(UlSize.controlMobile).fillMaxWidth())`；**不要**依赖 M3 默认 `ButtonDefaults`（其高度 40dp、圆角 full，与令牌冲突）；loading 用 `CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)` 替换图标位，`Text` 保留占位保证宽度不变 |
| Web | `<button class="ul-btn ul-btn--primary" data-loading="false">`；`height: var(--ul-size-control)`；`:disabled { opacity:.6; cursor:default }`；`transition: background var(--ul-dur-fast) var(--ul-ease-standard)` |

**无障碍**：最小热区（桌面 32、移动 48，视觉可 40）；`loading=true` 时设 `aria-busy` 并 `disabled` 但**不移除焦点**；`danger` 不额外弹确认（确认由调用方页面负责）。
**反例**：铜金大色块当卡片背景（`06` §3.1 :200 禁止）；loading 时按钮收缩导致布局跳动；用 emoji 当图标（X-19）。

## B.3 TextField 输入框

**语义**：手输 IP:端口、设备名搜索、配对 PIN（P1）。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `value` / `onValueChange` | `string` / `(string)->Unit` | ✅ | 受控组件 |
| `placeholder` | `string` | ⬜ | 示例格式：`192.168.1.23:9527` |
| `error` | `string?` | ⬜ | 非空即错误态，**下方必须显示该文案** |
| `helper` | `string?` | ⬜ | 常态提示（如"局域网内 IPv4 地址"） |
| `keyboardType` | `text` / `number` / `uri` | ⬜ | 移动端键盘类型 |
| `onSubmit` | `()->Unit` | ⬜ | 回车/完成即提交 |

**状态表**

| 态 | 描边 | 底 | 附加 |
|---|---|---|---|
| `default` | `border-strong` 1px | `surface-sunken` | 高度 40（桌面）/44（移动） |
| `focused` | `accent` 1.5px + 外发光 2px `accent` 12% alpha | 同 | 光标 `accent` |
| `error` | `danger` 1px | 同 | 下方 `text.caption` + `danger` 文案 |
| `disabled` | `border-subtle` 1px | `surface` | 60% 不透明，`text-tertiary` 占位 |

**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | `TextBox` 重模板（`TextControlElevationBorderFocusedBrush` 是系统键，**不随品牌色**，必须换 `UlBrushAccent`）；外发光用外层 `Border` + `ThemeShadow` 不可行于输入框 → 用 `Border` 双层（外 2px 半透明 + 内 1.5px） |
| Android | **不要用 `OutlinedTextField`**（其 label 浮动/floating 高度与 40dp 冲突）；用 `BasicTextField` + 自绘 `Box`（现状 `ModernMainScreen.kt:542-637` 已是这个路子，保留并令牌化）；`Modifier.height(UlSize.controlMobile)`，`decorationBox` 里画描边与错误文案 |
| Web | `<input class="ul-field">` + 包裹 `<div class="ul-field-wrap" data-error>`；聚焦辉光用 `box-shadow: 0 0 0 2px rgba(196,154,108,.12)` —— **用 `color-mix` 或显式 alpha，不要写死 hex**（若必须写死，登入 §A.6 白名单） |

**无障碍**：`error` 必须通过 `aria-describedby` / `semantics { error(msg) }` 关联；`placeholder` 不得替代标签（无可见标签时用 `accessibilityLabel`）。
**反例**：错误只用红框无文字；`type=number` 导致 IP 的点号无法输入（用 `text` + `keyboardType=Number` 但允许 `.`）。

## B.4 Skeleton 骨架屏

**语义**：告诉用户"正在加载"而非"没有内容"（X-12）。**不得出现布局跳动**——骨架的尺寸必须等于真实内容尺寸。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `shape` | `device-list` / `text-block` / `hud` | ✅ | 决定骨架结构 |
| `count` | `int` | ⬜ | `device-list` 的行数，默认 3 |
| `animated` | `bool` | ⬜ | 尊重 `prefers-reduced-motion` / 系统"减少动画" |

**令牌**：底 `color.surface`（N-2 `skeleton-base`）、高光 `skeleton-sheen`、圆角 `radius.sm`（设备卡）/`radius.md`（面板）、时长 `dur.shimmer`(1500ms)、曲线 `ease.standard`。

**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | `SkeletonBars.xaml`：`Rectangle` + `LinearGradientBrush` 的 `Offset` 做 `Storyboard` 平移；**`Rectangle` 必须设 `Height`/`Width` 显式值**，否则 `Auto` 高度为 0，shimmer 不可见（最常见 bug） |
| Android | `Brush.linearGradient` + `rememberInfiniteTransition` 动画 `startOffset`/`endOffset`；或直接用 `Modifier.placeholder` 风格自绘。**不要在 `LazyColumn` 里给骨架设 `weight(1f)`**，否则骨架高度随父容器变化导致跳动 |
| Web | `.ul-skeleton { background: linear-gradient(90deg, var(--ul-color-skeleton-sheen) 25%, ...); background-size: 200% 100%; animation: ul-shimmer var(--ul-dur-shimmer) infinite }` 与 `.ul-skeleton { height: var(--ul-size-device-card-min-h) }` 成对出现 |

**无障碍**：骨架容器 `aria-hidden="true"`；同时在容器外提供 `role="status"` 的"正在加载设备列表"文案，否则读屏用户完全无感知。
**反例**：骨架 3 行、真实列表 8 行导致跳；shimmer 2s 循环超过 3 次（`06` §8.1 :638 限制自动播放循环，骨架属允许例外但需 `reduced-motion` 关闭）。

## B.5 EmptyState 空状态

**语义**：`06` §7.5 :621——64px 线性图标 + 一句说明 + 一个主操作 + 最多两个次操作。**必须区分"没找到"与"在找"与"出错"三种空**（X-12 的核心）。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `kind` | `no-result` / `scanning` / `feature-off` / `error` | ✅ | 四种语义不可混用 |
| `title` | `string` | ✅ | `text.subtitle`，一句 |
| `reason` | `string?` | `error`/`no-result` 必填 | P3 的"为什么"（如"防火墙可能拦截了 9527"） |
| `primaryAction` | `{label, onClick}` | ✅（`scanning` 除外） | 一个 |
| `secondaryActions` | `[{label, onClick}]` | ⬜ | 最多 2 个，超出即设计错误 |

**四态表**

| `kind` | 图标 | 标题示例 | 动作 |
|---|---|---|---|
| `no-result` | `search_off` | 未发现设备 | [重新扫描] [手动输入 IP] |
| `scanning` | `radar`（旋转，1.6s） | 正在搜索局域网设备… | 无（保留 [停止] 为次操作） |
| `feature-off` | `visibility_off` | 本机当前不可被发现 | [开启可被发现] [如何设置？] |
| `error` | `error_outline` | 搜索失败 | [重试] [检查防火墙] |

**实现要点**：三端均为"垂直居中 Column/Box + `space.4` 间距"；图标 `UlSizeIconEmpty`(64) 且 `text-tertiary`；**禁止**给空状态加 `Card` 背景（`06` §5 各屏线框均为平面）。
**无障碍**：`title` 作为 `AccessibilityView="Content"` 焦点停靠点；`scanning` 态 `liveRegion` 播报一次"正在搜索"。
**反例**：空白页（什么都不显示）；"暂无数据"四个字无动作；把 `scanning` 与 `no-result` 用同一个 UI（用户无法判断是否还在找）。

## B.6 ErrorBanner 错误横幅

**语义**：P3 三件套「现象 + 原因 + 动作」，四级严重度（`06` §7.5 :623）。**色只用于图标与左侧 2px 边**，不做大面积填充。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `severity` | `info` / `warning` / `error` / `critical` | ✅ | 映射见下表 |
| `phenomenon` | `string` | ✅ | 现象（"连接中断"） |
| `reason` | `string?` | `error`/`critical` 必填 | 原因（"对方已关闭可被发现"） |
| `actions` | `[{label, onClick}]` | `error`/`critical` 必填 | 最多 2 个 |
| `dismissible` | `bool` | ⬜ | `critical` 建议 `false` |

**严重度表**

| severity | 图标 | 图标/边色 | 背景 | 默认可关闭 |
|---|---|---|---|---|
| `info` | `info` | `text-secondary` | `surface` | ✅ |
| `warning` | `warning` | `warning` | `surface` | ✅ |
| `error` | `error_outline` | `danger` | `surface` | ✅ |
| `critical` | `block` | `danger` | `surface` + 左侧边 2px `danger` | ❌ |

**复用**：Android 侧沿用现有 `ui/components/ErrorBanner.kt:31-39` 的 `ErrorBannerData(message, suggestions, actionLabel)` 模型（`06` §1.3 明确"保留模型"），但**删除** `ModernMainScreen.kt:183-222` 的同名私有实现（X-14 的成因）。
**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | 用 `InfoBar`（复用 `NotificationService` 栈，`06` §1.3 :94）但必须关掉系统默认的蓝/黄底：`Background=UlBrushSurface`、`BorderBrush` 按 severity、`Severity` 仅决定图标；**Y-13 修复**：InfoBar 不得置于标题栏层（`MainWindow.xaml:227-233` 溢出到窗口按钮），应放入内容区顶部占位行 |
| Android | `Row` + `Box(Modifier.width(2.dp).fillMaxHeight().background(color))` + `Column(Text(phenomenon), Text(reason), Row(actions))`；进入动画 `slideInVertically(tween(UlMotion.fast))` |
| Web | `.ul-error-banner { border-left: 2px solid var(--sev-color) }`；现 `styles.css:77-100` 的 `background: rgba(...,.1)` 全填充需改为 `var(--ul-color-surface)`（对齐"色只用于图标与边"） |

**无障碍**：`critical` 用 `assertive` live region，其余 `polite`；关闭按钮 `accessibilityLabel="关闭提示"`。
**反例**：`06` §1.4 的三条"假状态"（`InfoBar IsOpen="True"` 永远 System Ready，Y-14）；一个红色感叹号没有按钮。

## B.7 Toast 轻提示

**语义**：一次性的结果反馈（"已复制地址""已断开"），不承担错误详情（那是 B.6 的职责）。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `level` | `success` / `info` / `warning` / `error` | ✅ | 仅决定图标与边框色 |
| `message` | `string` | ✅ | 一句，不换行（超长由调用方截断） |
| `durationMs` | `int` | ⬜ | 默认 4000（`06` §7.5 :619） |
| `action` | `{label, onClick}?` | ⬜ | 如"撤销" |

**规则表**

| 项 | 值 | 来源 |
|---|---|---|
| 位置 | 桌面右下（`space.4` 边距）、移动底部（避开手势区） | `06` §7.5 :619 |
| 层级 | `elev.3` | 同上 |
| 堆叠 | 最多 3 条，超出丢弃最旧 | 同上 |
| 自动消失 | 4000ms；`error` 建议 6000ms | 本文建议（`06` 只给 4000） |
| 入场/退场 | `dur.base` + `ease.emphasized` / `ease.exit` | `06` §3.3 |

**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | 直接复用 `Services/NotificationService.cs:46-84`（已有"最多 3 条、5s 消失"），把 `5s → 4s`、样式换令牌即可；**不要**新建第二套通知栈（会与 InfoBar 抢层级） |
| Android | 用 `SnackbarHostState` 但自绘 `Snackbar`（`SnackbarDefaults` 的 `inverseSurface` 在深色下是浅底，与品牌冲突）；底部加 `WindowInsets.navigationBars` padding |
| Web | 复用 `web/app.js:27-48` 的 Toast 引擎；`styles.css:127-148` 的 `min-width:280px` 改为 `max-width: min(400px, calc(100vw - var(--ul-space-8)))` 修复窄屏溢出 |

**无障碍**：`role="status"` + `aria-live="polite"`（`error` 用 `assertive`）；**可手动关闭**是硬要求（`06` §7.5 :619）；`reduced-motion` 下改为纯透明度变化。
**反例**：用 Toast 显示"连接失败，请检查防火墙并重试"（应走 ErrorBanner）；Toast 覆盖在工具栏按钮上导致点不到。

## B.8 BottomSheet 底部弹层

**语义**：承载"第二层决策"（连接方式、画质选择、被控授权确认），一次只推进一步（P5）。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `visible` | `bool` | ✅ | 受控 |
| `onDismiss` | `()->Unit` | ✅ | 下滑、点遮罩、返回键都触发 |
| `title` | `string` | ✅ | 与触发按钮文案同义 |
| `maxHeightRatio` | `float` | ⬜ | 默认 0.8（80vh） |
| `dismissible` | `bool` | ⬜ | 被控授权（A4）时**必须**可关闭（用户有权拒绝） |

**规则表**

| 项 | 值 |
|---|---|
| 圆角 | 顶部两角 `radius.lg`(16)，底部直角 |
| 遮罩 | `color.scrim` = 40% 黑（N-1） |
| 高度 | ≤80vh；内容超出时内部滚动，弹层本身不滚动 |
| 入场 | `dur.panel`(240) + `ease.emphasized` |
| 关闭手势 | 下滑 >25% 高度或速度 >500px/s **且** 内部滚动在顶部 |

**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | WinUI 无原生 BottomSheet；用 `ContentDialog` 会丢失"下滑关闭"手势，故 W7 会话内改用**右侧 `TeachingTip`/自绘 `Grid` 覆盖层**；**不要**为了统一而把桌面也做成底部弹出（P6：同语义不同密度） |
| Android | `ModalBottomSheet` + `sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)`；`shape = RoundedCornerShape(topStart = UlRadius.lg, topEnd = UlRadius.lg)`；**陷阱**：`ModalBottomSheet` 的 `scrimColor` 默认是 `scrim` 但带 32% 透明度，需显式传 `LocalUlColors.current.scrim` |
| Web | 自绘 `<div class="ul-sheet" role="dialog" aria-modal="true">` + `.ul-sheet__scrim`；用 `transform: translateY()` + `transition: transform var(--ul-dur-panel) var(--ul-ease-emphasized)`；**必须**实现焦点陷阱（弹层打开时 `Tab` 不出弹层） |

**无障碍**：`role=dialog` + `aria-modal` + 打开时焦点移入第一个可交互元素 + `Esc` 关闭 + 关闭后焦点回到触发按钮。
**反例**：弹层内再开弹层（P5 违规）；遮罩不可点关闭导致用户被困；弹层高度自适应内容导致每次高度不同（布局抖动）。

## B.9 DeviceCard 设备卡

**语义**：`06` §7.2——状态点 + 设备名 + 副行（平台 · 网络类型 · `IP:端口` · 最后在线）+ 右侧操作区。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `name` | `string` | ✅ | `text.subtitle`(16/22)，最多 2 行截断 |
| `platform` | `win` / `android` / `web` | ✅ | 决定图标，不决定颜色 |
| `linkKind` | `LinkKind` | ✅ | 取 `03` §2.6（`Lan`/`Hotspot`/`WifiDirect`/`Usb`） |
| `endpoint` | `string` | ✅ | `192.168.1.23:9527`，等宽字体 |
| `lastSeenAt` | `timestamp?` | ⬜ | 相对时间（"12 秒前"） |
| `state` | `UiConnectionState` | ✅ | 传给 B.1 |
| `selected` | `bool` | ⬜ | 左 2px `accent` 描边 |
| `disabledReason` | `string?` | ⬜ | 非空即 `disabled` 态，**必须**作为副行显示 |
| `onPrimary` / `onRemove` | `()->Unit` | ✅ / ⬜ | `Enter` / `Delete`（`06` §7.2 :558） |

**状态表**

| 态 | 视觉 | 令牌 |
|---|---|---|
| `default` | 平面卡片 | 底 `surface`，描边 `border-subtle` 1px，`radius.md` |
| `hover` | 抬升 | `elev.1` + 描边 `border-strong` |
| `selected` | 左 2px 竖条 | 竖条 `accent`，描边 `border-strong` |
| `connecting` | 展开 4 阶段点 | 4 个 6px 圆点，已过阶段 `accent` 实心、未到 `border-strong` |
| `disabled` | 60% 不透明 | 整体 `Opacity=.6`，`onPrimary` 禁用 |

**实现要点（本文最重要的一条 Windows 修复）**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | **删除 `AppStyles.xaml:47-53` 的固定 `Width=220`/`Height=160`（X-10）**，改为 `MinHeight={StaticResource UlSizeDeviceCardMinH}`(72)、`HorizontalAlignment=Stretch`、`Width=Auto`；列表容器改 `ItemsRepeater` + `UniformGridLayout`（见 §C.2）；`AutomationProperties.HelpText` 绑 `disabledReason` |
| Android | `Card(colors = CardDefaults.cardColors(containerColor = c.surface), border = BorderStroke(1.dp, c.borderSubtle), shape = RoundedCornerShape(UlRadius.md))` + `Modifier.heightIn(min = UlSize.deviceCardMinH)`；**触控热区 ≥48dp**：视觉 72dp 已满足，但右侧 `IconButton` 必须 `Modifier.size(48.dp)` |
| Web | `<div class="ul-device-card" tabindex="0" role="button">` + `min-height: var(--ul-size-device-card-min-h)`；`focus-visible` 由 `tokens.css` 统一；**删除** `styles.css:34-37` 的 `transform: translateY(-2px)` 位移悬停（与 `reduced-motion` 冲突且导致列表抖动）→ 改为 `elev.1` |

**键盘/无障碍**：卡片整体可 `Tab` 聚焦；`Enter`=连接、`Delete`=移除、`Shift+F10`=上下文菜单；读屏播报 `"{name}，{linkKind}，{endpoint}，{state 文案}"`。
**反例**：固定 220×160（长设备名截断、窄窗口溢出）；一行里塞 3 个按钮（P5）；`IP:端口` 用比例字体导致列不对齐。

## B.10 QualitySegmented 画质分段控件

**语义**：三档画质切换，**必须显示参数副行**（`06` §7.5 :618）——用户要看到"流畅=720p30 / 均衡=1080p30 / 高清=1080p60"而不是三个抽象词。
**统一 props**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `options` | `[{id, label, params}]` | ✅ | `label` 如"均衡"，`params` 如"1080p · 30fps · 4 Mbps" |
| `value` | `id` | ✅ | 受控 |
| `onChange` | `(id)->Unit` | ✅ | 触发 `MediaCapability` 更新（`03` §11.1：唯一允许的 UI 回写） |
| `disabled` | `bool` | ⬜ | 未连接时禁用并显示原因 |

**规则表**

| 项 | 值 |
|---|---|
| 布局 | 三段等宽（`weight 1f` / `grid-template-columns: repeat(3,1fr)`） |
| 选中段 | 底 `accent`，文字 `on-accent` |
| 未选中 | 底 `surface-raised`，文字 `text-secondary` |
| 圆角 | `radius.sm`（整组）+ 内段 `radius.xs` |
| 副行 | `text.caption` + `text-tertiary`（选中时 `on-accent` 70% alpha） |
| 高度 | 桌面 40 / 移动 56（含副行） |

**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | `SelectorBar`（WinAppSDK 1.5+）或自绘 `RadioButtons` + 重模板；**不要**用 `Segmented`（WinUI 无此控件）；参数副行用 `StackPanel` 两行，`FontFamily=UlFontFamilyMono` 保证数字不抖 |
| Android | 不要用 M3 `SegmentedButton`（默认高度 40、副行无处放）；用 `Row` + 三个 `Column(Box(weight 1f))`，参数用 `UlTextMono`；选中态变化补 `Modifier.semantics { selected = true }` |
| Web | `<div role="radiogroup">` + 三个 `<button role="radio" aria-checked>`；`display:grid; grid-template-columns:repeat(3,1fr)` |

**无障碍**：`radiogroup` 语义 + 左右方向键切换；`aria-checked` 必须同步；参数副行不能是唯一信息来源（`aria-label` 要含参数）。
**反例**：只有"低/中/高"三个词（用户不知道差在哪）；选中态只变文字颜色（对比度不足）；切换时连接中断但无 loading 反馈。

## B.11 LanBoundaryCard 局域网边界卡

**语义**：把"零流量"从 README 承诺变成**可核对证据**（P4 / Y-02）。四个判据全部来自真实数据。
**统一 props（全部来自 `UiLinkMetrics` 与本地接口枚举）**

| prop | 类型 | 必填 | 说明 |
|---|---|---|---|
| `localEndpoint` | `{ip, ifaceName, kind}` | ✅ | 本机接口 + 网络类型 |
| `peerEndpoint` | `{ip, sameSubnet}` | ✅ | 对端地址 + 是否同网段 |
| `sessionBytes` | `long` | ✅ | 会话字节计数 |
| `publicEgressCount` | `int` | ✅ | **出站公网连接数，仅 0 时显示绿勾** |
| `relayUsed` | `bool` | ✅ | 是否经中转（P0 恒 `false`，但仍需显示"无"） |

**判据表（四个真/假一目了然）**

| 行 | 值为"好" | 值为"坏" | 显示 |
|---|---|---|---|
| 本机 | 私有网段 | — | `192.168.1.10（Wi-Fi）` |
| 对端 | 同网段 | 不同网段 | `192.168.1.23（同一网段）` / `（跨网段）` 黄 |
| 已传输 | 任意 | — | `128.4 MB`（等宽，1 位小数） |
| 公网连接 | **0** | ≥1 | 绿勾 `已验证` / 红字 `检测到 1 个公网连接` |
| 中转/云 | `无` | 有 | `无` / 红字 `经中转：<region>` |

**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | `Controls/LanBoundaryCard.xaml`；放在 W7 统计抽屉顶部（`06` §7.3 :580）；数据来自 `UiLinkMetrics.PublicEgressCount` + `NetworkInterface.GetAllNetworkInterfaces()`；**陷阱**：`PublicEgressCount` 的取值必须排除 mDNS(5353)/DHCP/回环，否则永远不为 0（判定逻辑属 L5.3 `LinkMetricsSource`，不在组件内） |
| Android | 放 A6 统计浮层顶部；`WifiManager.connectionInfo` 取本机 IP；**必须**声明 `ACCESS_NETWORK_STATE`（若未声明则整卡显示"无法验证"而不是假绿） |
| Web | 放 X2 状态条点击展开；浏览器**无法**枚举出站连接数 → `publicEgressCount` 显示 `—`，并写明"浏览器环境不可验证"，**禁止**伪造成 0（P2） |

**无障碍**：每个判据是"标签 + 值 + 状态图标"三元组；绿勾用图标 + 文字"已验证"双通道。
**反例**：只写一行灰字"本产品不联网"（`06` §2 P4 反例）；`publicEgressCount` 取不到时默认 0（假绿，最严重）。

## B.12 LinkHUD 链路卡

**语义**：`06` §7.4——四项数值 + 30 点 sparkline；**延迟必须分三层**（§7.4.1）。
**统一 props（全部来自 `UiLinkMetrics`，全部可为 `null`）**

| prop | 类型 | 空值显示 | 预警阈值 |
|---|---|---|---|
| `latencyMs` | `double?` | `—` | >120 黄，>250 红 |
| `networkMs` / `decodeMs` / `renderMs` | `double?` | `—` | L1/L2/L3 分层 |
| `bitrateKbps` | `int?` | `—` | < 档位目标 60% 黄 |
| `lossPercent` | `double?` | `—` | >2% 黄，>5% 红 |
| `width`×`height` / `fps` | `int?` | `—` | < 目标 80% 黄 |
| `samples` | `UiLinkMetrics[]` | sparkline 空白 | 30 点 / 1s 采样 / 保留 30s |

**硬规则**

| 规则 | 说明 |
|---|---|
| 禁止 0 冒充无样本 | `null → "—"`（`03` §11.2 :1130） |
| 禁止信号格 | `06` §7.4 :593；X-09 不得复活 |
| 数值必须等宽 | `tabular-nums` / `UlFontFamilyMono`（修 X-04） |
| 必须标采样窗口 | 如"近 3s"（`06` §7.4.1 :608） |
| 分层用同色系深浅 | 不用红黄绿（`:606`） |
| 状态点不受指标驱动 | 指标只着色（`03` §11.3 (D) 硬规则） |

**分层条形图（延迟）**

| 层 | 名称 | 数据来源 | 典型值 | 着色 |
|---|---|---|---|---|
| L1 | 网络 | `networkMs`（`rttMs`/2 + 到达时差折算） | 5–20ms | `accent` 100% |
| L2 | 解码 | `decodeMs`（`MediaFeedback.decodeMs`） | 3–12ms | `accent` 70% |
| L3 | 渲染 | `renderMs`（本地计算，不占协议字段） | 2–8ms | `accent` 45% |
| — | 总延迟 | L1+L2+L3，**同一单调时基** | ≤40ms | 文字 |

**实现要点**

| 端 | 关键实现 / 陷阱 |
|---|---|
| Windows | `Controls/LinkHud.xaml`；sparkline 用 `Polyline`（30 点 → `Points` 字符串），**不要**引第三方图表库（`06` §11 黑名单）；数值区统一 `FontFamily={StaticResource UlFontFamilyMono}`——WinUI 无 `tabular-nums` 开关，靠等宽族即可保证不抖 |
| Android | `Canvas` 画 `Path`；`Modifier.semantics { stateDescription = "延迟 42 毫秒" }`；**陷阱**：每 1s 更新一次状态导致整卡重组 → 数值用 `derivedStateOf` 抽离，sparkline 用 `drawWithCache` |
| Web | `getStats` 采集已有（`web/app.js:267-333`）；用 `<canvas>` 或 `<svg><polyline>`；`font-feature-settings: "tnum"` 由 `.ul-num` 统一提供 |

**无障碍**：sparkline `aria-hidden`，数值本身提供 `aria-label`（"近 3 秒平均延迟 42 毫秒"）；展开抽屉时一次性播报三段延迟。
**反例**：只显示"延迟 42ms"一个总数（无法定位是网络/解码/渲染）；四格信号条；数值跳宽度导致每帧抖动；把瞬时值当稳定值（无采样窗口标注）。

---

# C. Windows 三条硬约束

## C.1 Mica：必须"可选 + 有回退 + 限区"

**现状证据**：`src/UniLinker.WinUI/UniLinker.WinUI.csproj:5` `TargetPlatformMinVersion=10.0.17763.0`（Win10 1809）；全仓 `MicaBackdrop|SystemBackdrop|DesktopAcrylic` 命中 **0**；`MainWindow.xaml:13` 存在自绘 32px 标题栏行；`MainWindow.xaml.cs:217-243` 在 code-behind 里写死标题栏颜色（X-21）。

| # | 约束 | 为什么 | 违反后果 |
|---|---|---|---|
| C-1a | **必须做能力探测**：`MicaController.IsSupported()`（WinAppSDK 1.6 已引，见 csproj:31） | 目标最低版本是 Win10 1809，Mica 仅 Win11 22000+ | 在 Win10 上 `SystemBackdrop = new MicaBackdrop()` **静默无效**，得到全黑或全透明窗口 |
| C-1b | **必须有实色回退**：探测失败 → `Content` 根 `Grid` 背景 = `{ThemeResource UlBrushCanvas}` | 无回退则内容区透出桌面 | 灾难性：文字与背景不可读 |
| C-1c | **限制作用区**：Mica **只作用于标题栏 32px 带**，内容区一律实色 | 内容区要放视频舞台（固定 `surface-sunken`，`06` §3.1 :203），Mica 的半透明会让视频发灰 | 视频对比度下降、观感"脏" |

```csharp
// src/UniLinker.WinUI/MainWindow.xaml.cs —— 新增，替换 :217-243 的硬编码标题栏配色
private void ApplyBackdropSafely()
{
    // C-1a：Win10（本工程 TargetPlatformMinVersion=10.0.17763.0）没有 Mica
    if (!Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
    {
        RootGrid.Background = (Brush)Application.Current.Resources["UlBrushCanvas"]; // C-1b
        return;
    }
    try
    {
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        RootGrid.Background = (Brush)Application.Current.Resources["UlBrushCanvas"];
        // C-1c：内容区仍为实色，Mica 仅通过 TitleBar 区域的透明背景透出
        TitleBarArea.Background = new SolidColorBrush(Colors.Transparent);
    }
    catch (Exception ex) when (ex is COMException or InvalidOperationException)
    {
        SystemBackdrop = null;                                                      // C-1b
        RootGrid.Background = (Brush)Application.Current.Resources["UlBrushCanvas"];
        Log.Warn($"Mica 不可用，已回退实色：{ex.Message}");                          // 不许静默
    }
}
```

**验收判据**：① Win10 上启动后 `RootGrid.Background` 非 null 且窗口无透底；② Win11 上标题栏带可见亚克力/云母纹理，内容区无；③ 系统切深/浅色后无需重启即跟随（`ThemeDictionaries` 生效）。

## C.2 自适应：`ItemsRepeater` + `UniformGridLayout` 替代固定 220×160

**现状证据**：`Styles/AppStyles.xaml:47-53` `DeviceCardStyle` 固定 `Width=220`/`Height=160`；全仓 `ItemsRepeater|UniformGridLayout` 命中 0；`VisualStateManager|AdaptiveTrigger` 命中 0（Y-08）。

| # | 约束 | 做法 |
|---|---|---|
| C-2a | **禁止固定宽高** | `MinHeight=72`（N-6）+ `Width=Auto` + `HorizontalAlignment=Stretch` |
| C-2b | **列表必须用虚拟化 + 自适应布局** | `ItemsRepeater` + `UniformGridLayout`（WinUI 3 内置，**不是** Community Toolkit） |
| C-2c | **断点由容器宽度决定，不由窗口宽度决定** | 用 `UniformGridLayout.MinItemWidth` 自动折列，**不写** `AdaptiveTrigger`（窗口宽度在抽屉展开时会变，容器宽度更准） |

```xml
<!-- Views/DevicesPage.xaml —— 替换现有 ListView/ItemsControl 容器 -->
<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
  <ItemsRepeater ItemsSource="{x:Bind ViewModel.Devices, Mode=OneWay}">
    <ItemsRepeater.Layout>
      <UniformGridLayout
          MinItemWidth="320"          <!-- 一列最小 320，窄窗自动降到 1 列 -->
          MinItemHeight="72"          <!-- 与 UlSizeDeviceCardMinH 一致（N-6） -->
          MinRowSpacing="8"
          MinColumnSpacing="8"
          ItemsStretch="Fill"
          Orientation="Horizontal" />
    </ItemsRepeater.Layout>
    <ItemsRepeater.ItemTemplate>
      <DataTemplate x:DataType="vm:DeviceItemViewModel">
        <local:DeviceCard Style="{StaticResource UlDeviceCardStyle}" />
      </DataTemplate>
    </ItemsRepeater.ItemTemplate>
  </ItemsRepeater>
</ScrollViewer>
```

**布局档位（🔶 建议值，`06` 未给具体断点；沿用 Fluent 640/1008/1408 阶梯）**

| 容器宽度 | 列数 | 说明 |
|---|---|---|
| < 640 | 1 | 窄窗 / 抽屉展开时 |
| 640–1007 | 2 | 默认 1024×680 窗口（`06` Y-08）落在这一档 |
| 1008–1407 | 3 | |
| ≥ 1408 | 4 | 最大化 |

**验收判据**：窗口从 700px 拖到 1920px，卡片**从不截断设备名**、从不横向溢出、每次折列不出现中间态闪烁；`NavigationView` 的 `CompactModeThresholdWidth=500`/`ExpandedModeThresholdWidth=800`（`MainWindow.xaml:58-59`）保持不动。

## C.3 i18n：`Resources/i18n/*.json` + 字典查表（**不得用 `.resw`**）

**现状证据**：`UniLinker.WinUI.csproj:18-22` `GeneratePri=false` / `EnableMrtTargets=false` / `SkipMakePri=true` / `WindowsPackageType=None` → MRT 资源链不可用；`Services/LocalizationService.cs:14-155` 在 `LanguageIndex` setter 内手写约 **130 行** `OnPropertyChanged(...)`；每条文案是 `public string Xxx => LanguageIndex == 0 ? "English" : "中文";` 三元表达式，**漏一个属性就半中半英**（X-13）。

| # | 约束 | 做法 | 违反后果 |
|---|---|---|---|
| C-3a | **资源文件随输出复制** | `<Content Include="Resources\i18n\*.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>`（与 `app.ico` 同机制，csproj:52-57） | 打包时 JSON 丢失，界面全空 |
| C-3b | **一次刷新，不逐属性通知** | 索引器 + 单次 `OnPropertyChanged("Item[]")` | 回到 130 行手写列表的老路（X-13 复发） |
| C-3c | **绑定必须走索引器** | `{Binding Loc[Devices.Title]}` 或 `{x:Bind Loc['Devices.Title'], Mode=OneWay}` | `x:Bind` 默认 `OneTime`，切语言后界面不变 |
| C-3d | **缺键必须可见** | 查不到返回 `!key!` 并 `Debug.WriteLine` | 静默返回空串 → 出现"无字按钮"，比半中半英更难查 |

```csharp
// src/UniLinker.WinUI/Services/LocalizationService.cs —— 结构性重写（保留类名与 Loc 暴露方式）
public sealed partial class LocalizationService : INotifyPropertyChanged
{
    private static readonly string[] Supported = ["zh-CN", "en-US"];
    private Dictionary<string, string> _map = new(StringComparer.Ordinal);
    private int _languageIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>索引器：所有 UI 文案的唯一取值入口。缺键返回 !key! 便于发现漏译。</summary>
    public string this[string key] =>
        _map.TryGetValue(key, out var v) ? v : $"!{key}!";

    public int LanguageIndex
    {
        get => _languageIndex;
        set
        {
            if (_languageIndex == value || value < 0 || value >= Supported.Length) return;
            _languageIndex = value;
            Load(Supported[value]);
            // C-3b：索引器只有 "Item[]" 一个通知名，配合 {Binding Loc[...]} 一次性刷新全部文案
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    private void Load(string culture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "i18n", $"{culture}.json");
        _map = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new()
            : new Dictionary<string, string>();
    }
}
```

```json
// src/UniLinker.WinUI/Resources/i18n/zh-CN.json（节选，键名按 06 §8.2 术语表）
{
  "Common.Ok": "确定",
  "Common.Cancel": "取消",
  "Common.Retry": "重试",
  "Nav.Dashboard": "总览",
  "Nav.Devices": "设备",
  "Nav.Settings": "设置",
  "Device.NoDevicesHint": "未发现设备。请确认对方已开启「可被发现」，并检查防火墙是否放行 9527 端口。",
  "Device.CountFormat": "共 {0} 台设备",
  "Status.Idle": "未连接",
  "Status.Connecting": "正在连接",
  "Status.Connected": "已连接",
  "Status.Degraded": "画质降级",
  "Status.Reconnecting": "连接中断，正在重连 ({0}/5)",
  "Status.Error": "连接失败",
  "Share.AwaitingViewer": "等待观看者（本机正在采集）",
  "Share.Active": "已连接 · 正在投屏"
}
```

**迁移顺序（避免半中半英过渡期）**：① 先建 JSON 与索引器；② 用脚本把 313 行里的每个 `public string Xxx => ... ? "EN" : "中";` 抽出为键（键名 = 属性名，如 `Devices → Nav.Devices`）；③ 全局替换 `{Binding Loc.Xxx}` → `{Binding Loc[Xxx]}`；④ 删除 130 行通知列表；⑤ grep 断言 `OnPropertyChanged(nameof(` 命中 0。

**验收判据**：切换语言后**所有**可见文案同时改变，无一条残留；`grep -c 'LanguageIndex == 0 ?'` = 0；故意删一个键，界面显示 `!Nav.Devices!` 而非空白。

---

# D. Android 两个必须提前踩穿的陷阱

## D.1 视频渲染器：`SurfaceView` 是**独立合成层**，不是普通 View

**现状证据**：`plugins/screenmirror/ScreenMirrorTab.kt:103-112` 用 `AndroidView { SurfaceViewRenderer(ctx) }`，且 `update = { renderer -> videoTrack.addSink(renderer) }` —— 这个写法有两个独立缺陷（见下表 T2/T4）。`03` §11.5 把 A6 定为「`SurfaceView` + 覆盖层」，`03` §11.1 的 **L16** 明确要求"视频视图形态判定门槛"，本节就是该门槛的细则。

### D.1.1 合成层机制（为什么不能像普通 View 那样处理）

`SurfaceView` 向系统申请一块**独立的 BufferQueue**，由 SurfaceFlinger 单独合成，**不在应用窗口的绘制树里**。因此 Compose/View 的绘制属性对它**全部无效**：

| 期望效果 | 对普通 View | 对 `SurfaceView` | 结论 |
|---|---|---|---|
| `Modifier.clip(RoundedCornerShape(16.dp))` | 生效 | **不生效**（圆角被忽略，四角仍是矩形） | 视频舞台**禁止**圆角 |
| `Modifier.graphicsLayer(alpha = 0.5f)` / 淡入淡出 | 生效 | **不生效**（内容不跟随 alpha） | 视频**禁止** fade/scale（恰好与 `06` §3.3 :258 一致） |
| `Modifier.scale()` / 转场动画 | 生效 | **不跟随**（动画期出现黑矩形或错位） | 会话页转场期间需先隐藏 |
| Compose 内容绘制在它上面 | 生效 | **生效**（Surface 位于窗口之下，窗口内容在上） | 覆盖层可以正常叠加 |
| `View.captureToImage()` / Compose 截图测试 | 生效 | **截不到**（得到黑块） | 视觉回归必须跳过视频区或换 TextureView |

> **关键结论**：`03` §11.5 的「`SurfaceView` + 覆盖层」**在绘制顺序上是成立的**（覆盖层在窗口层，位于 Surface 之上）。真正的坑不是"覆盖层被挡住"，而是 **① 任何作用于视频视图的视觉修饰/动画都无效；② 截图与自动化测试拿不到画面**。这两个坑会直接打穿 UI-05 的验收与视觉回归。

### D.1.2 判定门槛（`03` L16 要求的形态选择）

| 场景 | 渲染器 | 理由 | 代价 |
|---|---|---|---|
| A6 全屏观看、无动画覆盖层、无圆角 | `SurfaceViewRenderer` | 零拷贝、功耗低、延迟最低 | 牺牲截图能力 |
| A6 需要下滑工具条动画 / 圆角 / 悬浮窗 | `TextureView` | 在应用窗口内合成，`clip`/`alpha`/动画/截图全部生效 | 多一次 GPU 拷贝，延迟约 +1 帧 |
| 被控侧 A4 需要"常驻提示 + 悬浮停止" | `TextureView` | 覆盖层与动画必须精确对齐 | 同上 |
| 视觉回归（`05` 的 UI 快照） | `TextureView` | `captureToImage()` 可用 | 仅测试构建启用 |

**先决动作（30 行 spike，必须在 UI-05 开工前完成）**：确认所依赖的 `stream-webrtc-android` 版本是否提供 `org.webrtc.TextureViewRenderer`。
- 提供 → 在 `SessionScreen.kt` 用 `enum VideoRendererMode { Surface, Texture }` 切换，默认 `Surface`，`BuildConfig.DEBUG` 或 UI 测试下强制 `Texture`。
- 不提供 → 保持 `SurfaceViewRenderer`，并**放弃**圆角/淡入淡出（改为瞬时显隐），视觉回归跳过视频区，同时在 A6 加一条 1px `border-strong` 边框帮助测试定位。

### D.1.3 四个必踩的代码级陷阱

| 编号 | 陷阱 | 现状证据 | 正确写法 |
|---|---|---|---|
| **T1** | `Modifier.clip` / `alpha` 对视频无效，被误当成样式 bug 反复调 | `ScreenMirrorTab.kt:104` `Modifier.fillMaxSize()`（暂无 clip，但改造时极易加） | 视频舞台不加任何修饰符；需要视觉边界时用兄弟节点画边框 |
| **T2** | `update = { videoTrack.addSink(renderer) }` **每次重组都会 addSink 一次** | `ScreenMirrorTab.kt:111` | 用 `DisposableEffect(videoTrack, renderer)` 做 add/remove，`update` 里只做 `setScalingType` 等幂等操作 |
| **T3** | `release()` 缺失或顺序错 → `EglBase` 线程泄漏，第二次进会话黑屏 | 该文件无 `release()` 调用 | `onDispose { videoTrack?.removeSink(renderer); renderer.release() }`，**先 removeSink 再 release** |
| **T4** | `enableEdgeToEdge` 后视频画到刘海/手势区；`Modifier.clip` 兜不住 | `MainActivity.kt:34-62`（无 `enableEdgeToEdge`，Y-12） | 视频容器用 `Modifier.padding(WindowInsets.safeDrawing.asPaddingValues())` 收缩**布局**（对 SurfaceView 有效），而不是靠 clip |

```kotlin
// ui/screens/SessionScreen.kt —— T2/T3 的正确写法（同一实例，add/remove 成对）
@Composable
fun VideoSurface(track: VideoTrack?, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val renderer = remember(track) {
        SurfaceViewRenderer(context).apply {
            setEnableHardwareScaler(true)
            setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FIT)
        }
    }
    AndroidView(
        factory = { renderer },
        modifier = modifier,
        update = { /* 只放幂等操作（缩放模式、镜像），禁止 addSink */ },
    )
    DisposableEffect(track, renderer) {
        track?.addSink(renderer)                       // 只在这里绑定一次
        onDispose {
            track?.removeSink(renderer)                // T3：先解绑
            renderer.release()                         // 再释放 EglBase
        }
    }
}
```

> **关键点**：`renderer` 用 `remember(track)` 持有**唯一实例**，`factory` 只是把它交给 `AndroidView`，绑定/解绑全部收敛到 `DisposableEffect`。若在 `update` 里 `addSink`（现状 `ScreenMirrorTab.kt:111`），每次重组都会多绑一层，表现为画面撕裂与内存增长。

### D.1.4 验收判据

① 进入 A6 → 返回 → 再进入，第二次仍有画面（T3 验证）；② 拖动屏幕不让覆盖层错位（合成层验证）；③ 会话页转场期间无黑矩形；④ `05` 的视觉回归在 `Texture` 模式下能截到测试图像（`Surface` 模式允许跳过视频区并注明）。

## D.2 `ModernMainScreen.kt` 834 行的拆分落地

**现状证据**：`ui/ModernMainScreen.kt` 834 行，含 12 个 private composable（`grep '^private fun'`）。`03` §11.5 已定拆分边界，本节补**行号 → 去向**映射与迁移注意事项。

### D.2.1 行号映射表（按现有文件实测）

| 现有符号 | 行号区间 | 行数 | 去向（`03` §11.5） | 处理 |
|---|---|---|---|---|
| `ModernMainScreen` | :52-121 | 70 | `ui/ModernMainScreen.kt`（瘦身为导航壳） | 🟡 改写为 `Scaffold + NavigationBar + NavHost`，预算 ≤80 |
| `IndustrialHeader` | :128-181 | 54 | `ui/screens/DeviceScreen.kt` 内 private | 🟡 令牌化；`isConnected` 布尔不得代表状态（改读 `UiConnectionState`） |
| `IndustrialErrorBanner` | :182-222 | 41 | ❌ 删除 | 改用 `ui/components/ErrorBanner.kt`（X-14 的重复实现） |
| `IndustrialConnectionPanel` | :223-279 | 57 | `ui/screens/DeviceScreen.kt` | 🟡 拆为"可被发现开关 + 附近设备 + 手动入口"三段 |
| `IndustrialStatusIndicator` | :280-391 | 112 | `ui/components/StatusDot.kt` + `ui/components/LinkHud.kt` | 🔴 **拆成两个组件**（B.1 / B.12），删除装饰性信号条（X-09） |
| `IndustrialControlZone` | :392-448 | 57 | `ui/screens/DeviceScreen.kt` 内 | 🟡 |
| `IndustrialConnectionTabs` | :449-477 | 29 | `ui/screens/ConnectionSheet.kt` | 🔴 **不再是并列 Tab**（Y-03）：改为单一主路径「局域网直连」+ 「其他方式」打开 BottomSheet |
| `IndustrialTabButton` | :478-540 | 63 | 合并进 `ui/components/QualitySegmented.kt` 的基座 | 🔴 删除独立实现，统一到分段控件 |
| `IndustrialAddressInput` | :541-637 | 97 | `ui/components/TextField.kt` | 🟡 保留 `BasicTextField` 路子（B.3），令牌化 |
| `IndustrialDeviceList` | :638-730 | 93 | `ui/screens/DeviceScreen.kt` + `ui/components/DeviceCard.kt` | 🟡 列表壳留页面、卡片抽组件 |
| `IndustrialEmptyState` | :731-769 | 39 | `ui/components/EmptyState.kt` | 🟡 补齐 4 种 `kind`（B.5） |
| `IndustrialDeviceItem` | :770-834 | 65 | 并入 `ui/components/DeviceCard.kt` | 🔴 删除 |
| （无） | — | — | `ui/components/Skeleton.kt` / `LanBoundaryCard.kt` / `Toast.kt` / `BottomSheet.kt` / `Button.kt` | 🆕 现有文件无对应实现，全新 |

> 行数预算（`03` §11.5）：每个 `components/*.kt` ≤150 行。上表 `:280-391` 的 112 行拆成两个组件后各约 60 行，符合。

### D.2.2 状态上移（ViewModel → Screen 解耦）

**现状问题**：`ModernMainScreen` 收 11 个参数 + 一个 `content` 槽（`:53-67`），而 `MainUiState`（`MainViewModel.kt:172-179`）有 6 个字段；`isScanning`/`isConnecting` 这类**布尔标志与真实状态并存**，正是 `03` §11.1 R-UI-4 禁止的形态。

| 目标 | 做法 |
|---|---|
| 屏幕只吃"快照 + 动作" | `DeviceScreen(ui: UiConnectionState, devices: List<PeerDevice>, metrics: UiLinkMetrics, actions: DeviceActions)` —— 一次性参数 ≤4 个 |
| 状态唯一来源 | `ui/state/{SessionStateSource, LinkMetricsSource, UiStateMapper}.kt`（`03` §11.4）产出 `StateFlow`；`MainViewModel` **只做转发**，不再自行拼 `isConnecting` |
| 可预览 | 每个 Screen 接受纯数据参数，`@Preview` 直接注入，不需要 ViewModel |
| Y-11 修复 | `MainViewModel.init` 里 `loadActiveStrategy()` 之后**必须**调 `startDiscovery()`（现状 `MainViewModel.kt:32-39` 只读配置不扫描，`LanMdnsStrategy.start()` 是空实现 `core/strategies/LanMdnsStrategy.kt:39-41`） |

### D.2.3 `UiPreviews.kt` 的 7 个 preview 跟着改

**现状证据**：`ui/UiPreviews.kt:49-237`，7 个 `@Preview` 全部调用 `ModernMainScreen(...)` 并传 11 个空 lambda（如 `:64-69`）。

| # | 现有 preview | 行号 | 改造后 |
|---|---|---|---|
| 1 | `PreviewMainScreen_Standby` | :49-74 | `@Preview DeviceScreen(ui = UiConnectionState.Idle, devices = emptyList())` → 断言命中 `EmptyState(kind = NoResult)` |
| 2 | `PreviewMainScreen_Scanning` | :76-100 | `DeviceScreen(ui = Connecting, devices = emptyList())` + `isScanning` 由 `CaptureState`/扫描协程驱动 → 断言 `EmptyState(kind = Scanning)` |
| 3 | `PreviewMainScreen_WithDevices` | :102-126 | `DeviceScreen(devices = previewDevices)`，卡片走 B.9；**新增** `linkKind`/`lastSeenAt` 字段以覆盖副行 |
| 4 | `PreviewMainScreen_Connecting` | :128-152 | `DeviceCard(selected = true, connectingStage = 2)` 单独预览四阶段点 |
| 5 | `PreviewMainScreen_Connected` | :154-185 | 改调 `SessionScreen(ui = Connected, metrics = sampleMetrics)`；视频区用占位 `Box`（T1：不要放真 `SurfaceView` 进 preview） |
| 6 | `PreviewMainScreen_Error` | :187-211 | 拆两条：`DeviceScreen(ui = Error)` + `ErrorBanner(severity = Critical)` 独立预览（B.6 的四级各有 `@Preview`） |
| 7 | `PreviewMainScreen_ManualIP` | :213-237 | `ConnectionSheet(initialTab = ManualIp)`（B.8） |

**新增预览（UI-01/UI-04 交付）**：`StatusDot` 5 态 × 1 行、`LinkHUD` 有样本/无样本（`null` 显示 `—`）、`LanBoundaryCard` 正常/检测到公网、`Skeleton`、`EmptyState` 4 种、`QualitySegmented` 选中各档。**每组件至少 1 个 `@Preview`，否则 UI-01 不算完成。**

### D.2.4 拆分顺序（避免 834 行里长期半拆状态）

1. **先抽叶子**：`TextField` → `EmptyState` → `DeviceCard`（纯 UI，无状态依赖，风险最低）。
2. **再删重复**：`IndustrialErrorBanner`、`IndustrialDeviceItem`、`IndustrialTabButton` 直接删除并指向新组件。
3. **再拆屏幕**：`IndustrialConnectionPanel` + `IndustrialControlZone` → `DeviceScreen`；`IndustrialConnectionTabs` → `ConnectionSheet`。
4. **最后改壳**：`ModernMainScreen` 缩到 ≤80 行导航壳，同步改 `MainActivity.kt` 的调用点。
5. **每步单独提交**，每步后跑一次 `./gradlew :app:assembleDebug` + 现有 7 个 preview 手动确认不白屏。

---

# E. 迁移四阶段与追溯矩阵

## E.1 四阶段总表

| 阶段 | 目标 | 改动文件 | 可独立验证的中间态 | 回滚方式 |
|---|---|---|---|---|
| **S0 · 删死代码** | 清掉约 1500 行不可达代码，避免"美化从不渲染的组件" | 删 Android `ConnectionMethodPicker.kt`(401) / `DeviceListView.kt`(188) / `components/{SignalIndicator,StatsOverlay}`；删 `ScreenMirrorTab.kt` 的 `DeviceListWithControls` 分支(~55)；删 `web/webcodecs-decoder.js`(217)；删 `styles.css` 无 DOM 段(~95) | 编译通过 + `grep -rn 'ConnectionMethodPicker\|DeviceListView\|webcodecs-decoder'` = 0；App 功能与 S0 前**完全一致** | `git revert` 单个提交（S0 只删不加） |
| **S1 · 建令牌** | 三端令牌文件落地并接线，**不换任何组件** | 🆕 `Styles/Tokens.xaml`、`ui/theme/Tokens.kt`、`web/tokens.css`；🟡 `App.xaml`（合并顺序）、`Theme.kt`（瘦身）、`AppStyles.xaml`（6 个 Style 换令牌Brush）、`styles.css`（引用变量）、`themes.xml`（X-20 底色） | **像素级不变**（判据见 §E.2）；`§A.5` V1/V2/V5/V6 全绿 | 令牌文件是新增，接线改动集中在 6 个 Style；`git revert` 不影响功能 |
| **S2 · 换组件** | UI-01：12 个组件三端就位 | 🆕 三端 `components/`、Windows `Controls/`；改 `UiPreviews.kt` | 三端组件预览页可逐一对图（`06` §7）；`§A.5` V3/V4 归零（117 处 `FontSize=` / 60 处 `fontSize=`） | 组件是新增；旧页面未改，App 行为不变 |
| **S3 · 改页面** | UI-02~UI-06：壳层、W7、A1~A6、X1/X2 | 按 `03` §11.4 的 🟡 清单 | U1~U10（`06` §9）逐条验收；W7/A6/X2 三屏是重点 | 页面级 feature flag（`Settings` 里的隐藏开关）灰度一版 |

> **顺序不可交换**（`06` §1.3 :110）：S0 必须最先，S1 必须早于 S2（否则组件里又要写一遍十六进制），S2 必须早于 S3（否则页面改造时组件还在变）。

## E.2 S1「像素级一致」的判定方法

这是整个迁移唯一能"证明没改坏东西"的硬判据，必须可执行：

| 端 | 为什么 S1 后像素不变 | 验证方法 | 需豁免的差异 |
|---|---|---|---|
| **Android** | `Tokens.kt` 的 20 个色值**逐字搬自** `Theme.kt:29-49`（`Copper/BackgroundDeep/...`），`ulTypography` 除 X-05 的两档上调外等同原值 | 同一设备、同一页面（Standby + WithDevices）改前改后截图，`compare` 逐像素比对 | **仅 `text-tertiary` `#7A7872 → #8A8880`**（A.1.1）与 `headlineLarge 18→32`、`labelSmall 10→11`（X-05）；这三处须在 diff 报告中显式列出并人工确认 |
| **Windows** | S1 只新建 `Tokens.xaml` + 把 6 个 `Style` 换成 `UlBrush*`（值 = 系统原本解析出的深色值近似），**不动任何页面 XAML** | 7 张基线截图（`gui-test-screenshots/`）重截比对 | 6 个 Style 的底色从"系统灰"改为"品牌暖灰"，会有**可预期的整体色偏**——这正是 S1 的交付目标（X-21），需人工确认"只偏色、不变形" |
| **Web** | `tokens.css` 只承载变量 + 迁移内联 `<style>`，`styles.css` 改引用 | 截图比对 X1/X2 | 无（Web 的品牌色替换放在 S3 的 UI-06，S1 不换色） |

**工具**：不引入新依赖。Windows 用 PowerShell + `System.Drawing` 逐像素 diff；Android 用 `adb exec-out screencap` × 2 + 同一脚本；Web 用浏览器 devtools 截图。**判据**：Android diff 像素数 = 仅上述三处文本区域；Windows 结构性元素（位置/尺寸）零变化。

## E.3 X/Y 缺陷追溯矩阵

`06` §1.1/§1.2 的 36 条缺陷 → 本文落点 → 修复阶段。**每一行都必须有归属，无归属即为设计漏洞。**

| 编号 | 一句话 | 本文落点 | 阶段 |
|---|---|---|---|
| X-01 | 三端三套色板 | §A.1~§A.4 三端令牌；§A.5 V2 | S1 |
| X-02 | `onSurface` 用了次文字色 | §A.3 `ulScheme` 的 `onSurface = c.textPrimary`；V7 | S1 |
| X-03 | M3 `surfaceContainer*` 等未定义 | §A.3 `ulScheme` 全角色显式赋值（含 `surfaceBright/Dim/Container*`、`tertiary*`） | S1 |
| X-04 | 数值非等宽 | §A.4 `.ul-num`；B.12 硬规则；`UlTextMono` | S1/S2 |
| X-05 | 字号上限 18 / `labelSmall` 10 | §A.3 `headlineLarge 32`、`labelSmall 11` | S1 |
| X-06 | 只有 dark、不读系统主题 | §A.2 Light 分支；§A.3 `lightColorScheme` + `isSystemInDarkTheme()`；§A.4 `prefers-color-scheme` | S1 |
| X-07 | 全局单一 4dp 圆角 | §A.1.3 五档圆角（4/8/12/16/999） | S1 |
| X-08 | Web 字体栈无 CJK | §A.4 `--ul-font-sans` 含 `PingFang SC`/`Microsoft YaHei` | S1 |
| X-09 | 装饰性四格信号条 | B.1 StatusDot（真状态）+ B.12 LinkHUD（真指标）；`:57-72` 删除 | S0/S2 |
| X-10 | Windows 设备卡固定 220×160 | §C.2（`ItemsRepeater` + `MinItemWidth=320`/`MinHeight=72`）；B.9 | S2 |
| X-11 | Android 834 行单文件 | §D.2 行号映射 + 拆分顺序 | S3 |
| X-12 | 无空状态/骨架屏规范 | B.4 Skeleton、B.5 EmptyState（4 种 `kind`） | S2 |
| X-13 | 文案硬编码、无 i18n | §C.3（Windows）；Android `strings.xml`/Web `i18n/*.js` 见 `06` §8.2 | S3 |
| X-14 | 同一错误三端三种呈现 | B.6 ErrorBanner 统一四级；删 `ModernMainScreen.kt:182-222` | S2 |
| X-15 | Windows 无任何 token，117 处内联 `FontSize=` | §A.2 全套令牌；V4 断言归零 | S2 |
| X-16 | Android 定义 Typography 却不用，60 处内联 | §A.3 `ulTypography` + `UlTextMono`；V4 | S2 |
| X-17 | Android 6 个模块死代码 ~1188 行 | §E.1 S0；§D.2.1 删除项 | S0 |
| X-18 | Web 两套样式源并存 | §A.4 接入顺序 + 内联 `<style>` 迁移 | S0/S1 |
| X-19 | 三端三套图标语言（Web 用 emoji） | B.2/B.5 明确"线性图标，禁 emoji"；图标族见 `06` §7 | S2 |
| X-20 | 冷启动闪色 | §A.3 末段 `themes.xml` 底色 → `#121210` | S1 |
| X-21 | Windows 用系统强调色（蓝） | §A.2 显式覆盖；§A.2 末段接线；V3 | S1 |
| Y-01 | 首启无引导 | W6 引导三屏（`06` §4.2 / `03` §11.4 `OnboardingWindow`） | S3 |
| Y-02 | 零流量无证据 | B.11 LanBoundaryCard（四个真实判据） | S2/S3 |
| Y-03 | 连接方式三 Tab 并列无推荐 | §D.2.1 `IndustrialConnectionTabs` 删除；B.8 单一主路径 + 底部弹层 | S3 |
| Y-04 | 会话中无质量反馈 | B.12 LinkHUD（分层延迟 + sparkline + 双口径丢包） | S2 |
| Y-05 | 被控授权信息不足 | B.8 弹层（可拒绝）+ A4 四项可核对（`06` §5 F5） | S3 |
| Y-06 | 断线只报错不重连 | B.1 `degraded/reconnecting` 变体 + B.6 `critical`；`03` §11.3 ICE 0.5s 宽限 | S2 |
| Y-07 | 无键盘可达性 | §8.1 快捷键；B.2/B.9 的 `Tab`/`Enter`/`Delete`；`tokens.css` `:focus-visible` | S2/S3 |
| Y-08 | 窗口尺寸/横竖屏未适配 | §C.2 四档折列；§A.1.3 尺寸令牌；`configChanges` 后的布局重排属 S3 | S2 |
| Y-09 | Start Share 不启动采集（假状态） | B.1 的 `idle + 等待观看者（本机正在采集）`；**状态源必须来自 `CaptureState`**（`03` §11.3 (C)） | S3 |
| Y-10 | Connect 不是连接 | 四阶段点（B.9 `connecting`）→ 真会话；"在浏览器中打开"降为次操作 | S3 |
| Y-11 | Android 启动不自动扫描 | §D.2.2 末行（`init` 必须调 `startDiscovery()`） | S3 |
| Y-12 | 无返回键处理、无沉浸式 | §D.1.3 T4（`enableEdgeToEdge` + Insets）；`BackHandler` 属 A6 页面 | S3 |
| Y-13 | InfoBar 盖住窗口按钮 | §B.6 Windows 行（InfoBar 入内容区，不进标题栏层） | S2 |
| Y-14 | Windows 硬件状态写死 | §B.1（状态来自 `UiConnectionState`）；`DashboardPage.xaml:22-33` 的 `IsOpen="True"` 必须删 | S3 |
| Y-15 | Web `disconnected` 不复位 UI | B.1/B.6 的状态驱动；`web/ui/state.js` 的 `mapSnapshot`（`03` §11.2 Web 契约） | S3 |

**覆盖率自检**：X 共 21 条、Y 共 15 条，上表 36 行，**无遗漏、无重复归属**。

## E.4 与 `06` 的偏差登记（本文对 `06` 的唯一补充，不含冲突）

| # | 事项 | `06` 原文 | 本文处理 | 性质 |
|---|---|---|---|---|
| 1 | 遮罩/骨架/状态软色等 7 个令牌无名 | 只给数值或描述 | §A.6 N-1~N-7 新增令牌名 | **补充**（需 `06` 增补） |
| 2 | 浅色分支缺 6 个色值 | §3.1 :184-196 只列 11 个 | §A.6 N-8 给出候选值，标注需实测复核 | **补充**（唯一非 `06` 来源数值） |
| 3 | `degraded` 的 `reconnecting` 细分 | §7.1 只有 5 态 | B.1 增加 `variant` 参数 | **补充**，依据 `03` §11.3 :1211（契约优先） |
| 4 | Windows 布局断点 | 未给具体 px | §C.2 用 Fluent 640/1008/1408 | **补充**（沿用平台阶梯） |
| 5 | Toast `error` 时长 | 统一 4000ms | B.7 建议 `error` 6000ms | **细化**（不影响默认值） |
| 6 | 缓动曲线的 WinUI 落地 | 未规定 | §A.2 用 `x:String` 令牌（`06` §3 未覆盖资源类型约束） | **实现细节** |

**明确无冲突**：本文 A 部分的全部色值/字号/间距/圆角/动效数值均逐一取自 `06` §3.1~§3.3，未修改任何一个给定值；组件状态与 `06` §7 的表格逐条一致；所有状态枚举取 `03` §11.3，未新增第六态。

## E.5 给 QA 的交付检查清单（`05`/`08` 可直接引用）

| # | 检查项 | 判定 |
|---|---|---|
| Q1 | `§A.5` V1~V7 全部通过 | 7/7 |
| Q2 | 12 个组件三端各至少 1 个预览/快照 | 12 × 3 |
| Q3 | S1 像素级回归：Android 仅 3 处文本差异、Windows 仅色偏 | 人工确认 |
| Q4 | `ModernMainScreen.kt` ≤80 行，`components/*.kt` 各 ≤150 行 | 行数统计 |
| Q5 | 视频渲染器形态判定 spike 有结论（`Surface` 或 `Texture`） | 结论存档 |
| Q6 | 切语言后无一条残留文案，缺键显示 `!key!` | 手工 + grep |
| Q7 | 36 条 X/Y 全部有归属且未被跳过 | 对 §E.3 |
| Q8 | 业务代码零十六进制、零内联字号 | V3/V4 = 0 |


