package com.unilinker.android.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

/**
 * 连接质量等级
 */
enum class ConnectionQuality {
    Excellent,  // 4格 - 优秀
    Good,       // 3格 - 良好
    Fair,       // 2格 - 一般
    Poor        // 1格 - 较差
}

/**
 * 信号强度指示器 - 四格信号条
 *
 * @param quality 连接质量等级
 * @param modifier 修饰符
 */
@Composable
fun SignalIndicator(
    quality: ConnectionQuality,
    modifier: Modifier = Modifier
) {
    val activeColor = when (quality) {
        ConnectionQuality.Excellent -> Color(0xFF22C55E)  // Green
        ConnectionQuality.Good -> Color(0xFF84CC16)       // Lime
        ConnectionQuality.Fair -> Color(0xFFF59E0B)       // Amber
        ConnectionQuality.Poor -> Color(0xFFEF4444)       // Red
    }

    val inactiveColor = Color(0xFF374151)  // Gray-700

    Row(
        modifier = modifier,
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(2.dp)
    ) {
        // 四格信号条，高度递增
        val barHeights = listOf(6.dp, 9.dp, 12.dp, 15.dp)
        val activeCount = when (quality) {
            ConnectionQuality.Excellent -> 4
            ConnectionQuality.Good -> 3
            ConnectionQuality.Fair -> 2
            ConnectionQuality.Poor -> 1
        }

        barHeights.forEachIndexed { index, height ->
            Box(
                modifier = Modifier
                    .width(3.dp)
                    .height(height)
                    .clip(RoundedCornerShape(1.dp))
                    .background(
                        if (index < activeCount) activeColor else inactiveColor
                    )
            )
        }
    }
}

/**
 * 根据网络指标计算连接质量
 *
 * @param rttMs 往返时间（毫秒）
 * @param packetLossPercent 丢包率（百分比）
 * @param bitrateKbps 码率（Kbps）
 * @return 连接质量等级
 */
fun calculateConnectionQuality(
    rttMs: Int,
    packetLossPercent: Double,
    bitrateKbps: Int
): ConnectionQuality {
    var score = 4

    // RTT 评分
    if (rttMs > 100) score--
    if (rttMs > 200) score--

    // 丢包率评分
    if (packetLossPercent > 1.0) score--
    if (packetLossPercent > 5.0) score--

    // 码率评分
    if (bitrateKbps < 2000) score--

    return when (score.coerceIn(1, 4)) {
        4 -> ConnectionQuality.Excellent
        3 -> ConnectionQuality.Good
        2 -> ConnectionQuality.Fair
        else -> ConnectionQuality.Poor
    }
}
