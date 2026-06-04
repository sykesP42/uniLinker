package com.unilinker.android.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.unilinker.android.ui.theme.*

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
 * 工业风统计信息覆盖层
 *
 * - 无圆角
 * - 暖灰背景
 * - 单色系文字
 */
@Composable
fun StatsOverlay(
    stats: StreamStats,
    modifier: Modifier = Modifier
) {
    Row(
        modifier = modifier
            .background(BackgroundSurface.copy(alpha = 0.9f))
            .padding(horizontal = 16.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(20.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        IndustrialStatItem(
            label = "分辨率",
            value = stats.resolution,
            valueColor = TextPrimary
        )
        IndustrialStatItem(
            label = "FPS",
            value = stats.fps.toString(),
            valueColor = when {
                stats.fps >= 30 -> StatusSuccess
                stats.fps >= 20 -> StatusWarning
                else -> StatusError
            }
        )
        IndustrialStatItem(
            label = "码率",
            value = stats.bitrateDisplay,
            valueColor = TextPrimary
        )
        IndustrialStatItem(
            label = "延迟",
            value = "${stats.latencyMs}ms",
            valueColor = when {
                stats.latencyMs < 50 -> StatusSuccess
                stats.latencyMs < 100 -> StatusWarning
                else -> StatusError
            }
        )
    }
}

@Composable
private fun IndustrialStatItem(
    label: String,
    value: String,
    valueColor: androidx.compose.ui.graphics.Color
) {
    Column(horizontalAlignment = Alignment.Start) {
        Text(
            text = value,
            fontSize = 13.sp,
            fontWeight = FontWeight.Bold,
            color = valueColor,
            fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace
        )
        Spacer(modifier = Modifier.height(2.dp))
        Text(
            text = label,
            fontSize = 10.sp,
            color = TextTertiary
        )
    }
}
