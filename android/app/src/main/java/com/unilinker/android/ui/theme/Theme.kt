package com.unilinker.android.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.Typography
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/**
 * UniLinker Industrial Theme
 *
 * 极简工业风设计系统：
 * - 暖灰基调 + 铜金色功能色
 * - JetBrains Mono 字体（技术感）
 * - 统一 4.dp 微圆角
 * - 无装饰元素
 */

// ═══════════════════════════════════════════════════════════════
// COLOR SYSTEM: Industrial Warm Gray + Copper
// ═══════════════════════════════════════════════════════════════

// Primary: Copper (温暖工业感)
val Copper = Color(0xFFC49A6C)
val OnCopper = Color(0xFF1A1A1A)

// Background layers: Warm gray gradient
val BackgroundDeep = Color(0xFF121210)      // 主背景
val BackgroundSurface = Color(0xFF1C1C18)   // 卡片背景
val BackgroundVariant = Color(0xFF262620)   // 分区背景

// Text: Warm gray scale
val TextPrimary = Color(0xFFE8E6E0)         // 主文字
val TextSecondary = Color(0xFFB8B6B0)       // 次文字
val TextTertiary = Color(0xFF7A7872)        // 辅助文字

// Borders
val BorderStrong = Color(0xFF3A3A34)        // 强边框
val BorderWeak = Color(0xFF2A2A26)          // 弱边框

// Status colors (only for alerts)
val StatusError = Color(0xFFE85D4C)         // 暖红 - 错误
val StatusWarning = Color(0xFFD4A84B)       // 琥珀金 - 等待
val StatusSuccess = Color(0xFF7BA05B)       // 苔藓绿 - 成功

private val IndustrialColorScheme = darkColorScheme(
    // Primary
    primary = Copper,
    onPrimary = OnCopper,
    primaryContainer = Copper.copy(alpha = 0.15f),
    onPrimaryContainer = TextPrimary,

    // Secondary
    secondary = TextSecondary,
    onSecondary = BackgroundDeep,
    secondaryContainer = BackgroundVariant,
    onSecondaryContainer = TextSecondary,

    // Background layers
    background = BackgroundDeep,
    onBackground = TextPrimary,
    surface = BackgroundSurface,
    onSurface = TextSecondary,
    surfaceVariant = BackgroundVariant,
    onSurfaceVariant = TextTertiary,

    // Outlines
    outline = BorderStrong,
    outlineVariant = BorderWeak,

    // Error
    error = StatusError,
    onError = TextPrimary,
    errorContainer = StatusError.copy(alpha = 0.15f),
    onErrorContainer = StatusError,

    // Inverse
    inverseSurface = TextPrimary,
    inverseOnSurface = BackgroundDeep,
    inversePrimary = Copper
)

// ═══════════════════════════════════════════════════════════════
// TYPOGRAPHY: JetBrains Mono
// ═══════════════════════════════════════════════════════════════

private val IndustrialTypography = Typography(
    // 标题：Bold，不大
    headlineLarge = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Bold,
        fontSize = 18.sp,
        letterSpacing = 0.sp,
        color = TextPrimary
    ),
    headlineMedium = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Bold,
        fontSize = 16.sp,
        letterSpacing = 0.sp,
        color = TextPrimary
    ),
    headlineSmall = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.SemiBold,
        fontSize = 14.sp,
        letterSpacing = 0.sp,
        color = TextPrimary
    ),

    // 标题组件
    titleLarge = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.SemiBold,
        fontSize = 16.sp,
        letterSpacing = 0.sp,
        color = TextPrimary
    ),
    titleMedium = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Medium,
        fontSize = 14.sp,
        letterSpacing = 0.sp,
        color = TextPrimary
    ),
    titleSmall = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Medium,
        fontSize = 13.sp,
        letterSpacing = 0.sp,
        color = TextSecondary
    ),

    // 正文
    bodyLarge = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 14.sp,
        letterSpacing = 0.sp,
        color = TextPrimary
    ),
    bodyMedium = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 13.sp,
        lineHeight = 18.sp,
        letterSpacing = 0.sp,
        color = TextPrimary
    ),
    bodySmall = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 12.sp,
        letterSpacing = 0.sp,
        color = TextSecondary
    ),

    // 标签
    labelLarge = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Medium,
        fontSize = 12.sp,
        letterSpacing = 0.5.sp,
        color = TextSecondary
    ),
    labelMedium = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Medium,
        fontSize = 11.sp,
        letterSpacing = 0.5.sp,
        color = TextTertiary
    ),
    labelSmall = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 10.sp,
        letterSpacing = 0.5.sp,
        color = TextTertiary
    )
)

// ═══════════════════════════════════════════════════════════════
// THEME COMPOSABLE
// ═══════════════════════════════════════════════════════════════

@Composable
fun UniLinkerTheme(
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = IndustrialColorScheme,
        typography = IndustrialTypography,
        content = content,
    )
}

// ═══════════════════════════════════════════════════════════════
// DESIGN CONSTANTS
// ═══════════════════════════════════════════════════════════════

object IndustrialDesign {
    // 圆角：统一 4.dp（几乎直角）
    const val CORNER_RADIUS = 4

    // 间距
    const val SPACING_UNIT = 8          // 基础单位
    const val SPACING_ELEMENT = 8       // 元素间距
    const val SPACING_ZONE = 16         // 区内间距
    const val SPACING_EDGE = 16         // 边缘边距

    // 字号区间
    const val FONT_SIZE_MIN = 10
    const val FONT_SIZE_MAX = 18

    // 动效时长
    const val ANIM_INSTANT = 0
    const val ANIM_FAST = 100
    const val ANIM_NORMAL = 150
    const val ANIM_SLOW = 300

    // 状态指示器
    const val STATUS_INDICATOR_SIZE = 48
}
