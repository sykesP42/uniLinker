package com.unilinker.android.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.Typography
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

private val DarkColors = darkColorScheme(
    // 统一为 Indigo 主色，与 Desktop 保持一致
    primary = Color(0xFF6366F1),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFF4F46E5),
    onPrimaryContainer = Color(0xFFE0E7FF),
    secondary = Color(0xFF818CF8),
    surface = Color(0xFF1A1A1A),
    onSurface = Color(0xFFE5E5E5),
    background = Color(0xFF0A0A0A),
    onBackground = Color(0xFFE5E5E5),
    surfaceVariant = Color(0xFF262626),
    onSurfaceVariant = Color(0xFFA3A3A3),
    error = Color(0xFFEF4444),
    outline = Color(0xFF525252),
)

private val UniLinkerTypography = Typography(
    headlineLarge = TextStyle(fontWeight = FontWeight.Bold, fontSize = 28.sp),
    headlineMedium = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 20.sp),
    titleMedium = TextStyle(fontWeight = FontWeight.Medium, fontSize = 16.sp),
    bodyMedium = TextStyle(fontSize = 14.sp, lineHeight = 20.sp),
    labelSmall = TextStyle(fontSize = 11.sp, color = Color(0xFF666666)),
)

@Composable
fun UniLinkerTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = DarkColors,
        typography = UniLinkerTypography,
        content = content,
    )
}
