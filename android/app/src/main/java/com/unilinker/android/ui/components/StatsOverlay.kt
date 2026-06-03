package com.unilinker.android.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * 流统计信息数据类
 */
data class StreamStats(
    val width: Int = 0,
    val height: Int = 0,
    val fps: Int = 0,
    val bitrateKbps: Int = 0,
    val latencyMs: Int = 0,
    val packetLossPercent: Double = 0.0,
    val rttMs: Int = 0
) {
    val resolution: String get() = "${width}x${height}"
    val bitrateDisplay: String get() = if (bitrateKbps >= 1000) {
        String.format("%.1f Mbps", bitrateKbps / 1000.0)
    } else {
        "$bitrateKbps Kbps"
    }
}

/**
 * 实时统计信息覆盖层
 *
 * @param stats 流统计信息
 * @param modifier 修饰符
 */
@Composable
fun StatsOverlay(
    stats: StreamStats,
    modifier: Modifier = Modifier
) {
    Row(
        modifier = modifier
            .clip(RoundedCornerShape(8.dp))
            .background(Color.Black.copy(alpha = 0.6f))
            .padding(horizontal = 12.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(16.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        StatItem(
            label = "分辨率",
            value = stats.resolution,
            valueColor = Color.White
        )
        StatItem(
            label = "FPS",
            value = stats.fps.toString(),
            valueColor = when {
                stats.fps >= 30 -> Color(0xFF22C55E)
                stats.fps >= 20 -> Color(0xFFF59E0B)
                else -> Color(0xFFEF4444)
            }
        )
        StatItem(
            label = "码率",
            value = stats.bitrateDisplay,
            valueColor = Color.White
        )
        StatItem(
            label = "延迟",
            value = "${stats.latencyMs}ms",
            valueColor = when {
                stats.latencyMs < 50 -> Color(0xFF22C55E)
                stats.latencyMs < 100 -> Color(0xFFF59E0B)
                else -> Color(0xFFEF4444)
            }
        )
    }
}

@Composable
private fun StatItem(
    label: String,
    value: String,
    valueColor: Color
) {
    Column(horizontalAlignment = Alignment.Start) {
        Text(
            text = value,
            fontSize = 13.sp,
            fontWeight = FontWeight.Medium,
            color = valueColor,
            fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace
        )
        Text(
            text = label,
            fontSize = 10.sp,
            color = Color(0xFF9CA3AF)
        )
    }
}
