package com.unilinker.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.zIndex
import com.unilinker.android.ui.theme.BackgroundSurface
import com.unilinker.android.ui.theme.BackgroundVariant
import com.unilinker.android.ui.theme.BorderStrong
import com.unilinker.android.ui.theme.Copper
import com.unilinker.android.ui.theme.StatusError
import com.unilinker.android.ui.theme.StatusSuccess
import com.unilinker.android.ui.theme.StatusWarning
import com.unilinker.android.ui.theme.TextPrimary
import com.unilinker.android.ui.theme.TextSecondary
import com.unilinker.android.ui.theme.TextTertiary
import kotlinx.coroutines.delay

/**
 * Floating ball + settings panel for the DirectRtp receiver screen.
 *
 * Retheme (panel overhaul): every surface uses the app-wide copper
 * IndustrialColorScheme tokens — the panel previously carried ~20 inline
 * blue-accent colors that clashed with the rest of the app. Copy lives with
 * the composables; state and handlers stay in DirectRtpActivity.
 */

// Copper panel palette (single source — no inline hex in the composables).
internal val PanelBg = BackgroundSurface.copy(alpha = 0.96f)
internal val PanelBorder = BorderStrong
internal val ChipBg = BackgroundVariant
internal val ChipSelectedBg = Copper.copy(alpha = 0.18f)
internal val OkBg = StatusSuccess.copy(alpha = 0.16f)
internal val DangerBg = StatusError.copy(alpha = 0.16f)

/** Draggable floating ball; auto-dims after 3 s without touch. Tap = toggle. */
@Composable
fun FloatingBall(expanded: Boolean, streaming: Boolean, onToggle: () -> Unit) {
    var offsetX by remember { mutableStateOf(0f) }
    var offsetY by remember { mutableStateOf(0f) }
    var lastTouch by remember { mutableLongStateOf(0L) }
    var dimmed by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        while (true) {
            delay(500)
            dimmed = !expanded && System.currentTimeMillis() - lastTouch > 3000
        }
    }

    Box(modifier = Modifier.fillMaxSize().zIndex(3f)) {
        Box(
            modifier = Modifier
                .align(Alignment.BottomEnd)
                .padding(16.dp)
                .offset { IntOffset(offsetX.toInt(), offsetY.toInt()) }
                .size(52.dp)
                .clip(CircleShape)
                .background(
                    if (dimmed) TextTertiary.copy(alpha = 0.20f)
                    else TextSecondary.copy(alpha = 0.55f)
                )
                // Tap and drag as SEPARATE pointer inputs: a tap toggles the
                // panel, a drag moves the ball. (An earlier single
                // detectDragGestures with a tiny-drag tap heuristic never fired
                // its toggle — taps must use detectTapGestures.)
                .pointerInput(Unit) {
                    detectTapGestures(onTap = { onToggle() })
                }
                .pointerInput(Unit) {
                    detectDragGestures(
                        onDrag = { change, drag ->
                            change.consume()
                            offsetX += drag.x
                            offsetY += drag.y
                            lastTouch = System.currentTimeMillis()
                        },
                    )
                },
            contentAlignment = Alignment.Center,
        ) {
            // Copper ring marks a live stream, gray otherwise.
            Box(
                Modifier
                    .size(40.dp)
                    .clip(CircleShape)
                    .background(Color.Transparent)
                    .border(
                        2.dp,
                        when {
                            streaming -> Copper
                            dimmed -> TextTertiary.copy(alpha = 0.4f)
                            else -> TextSecondary.copy(alpha = 0.8f)
                        },
                        CircleShape,
                    ),
            )
            Text(
                text = if (expanded) "✕" else "◎",
                color = if (dimmed) TextTertiary.copy(alpha = 0.5f) else TextPrimary,
                style = MaterialTheme.typography.titleMedium,
            )
        }
    }
}

/** Translucent settings panel shown when the ball is expanded.
 *
 *  Copper retheme: section labels carry the copper accent, chips tint with
 *  Copper when selected, status colors come from the industrial status set.
 *  Everything scrollable — the panel outgrew a single screen long ago.
 */
@Composable
fun FloatingPanel(
    statusText: String,
    resIdx: Int,
    brIdx: Int,
    fpsIdx: Int,
    resLabel: String,
    brLabel: String,
    fpsLabel: String,
    streaming: Boolean,
    transportMode: String,
    hudFpsOn: Boolean,
    hudMbpsOn: Boolean,
    hudJitterOn: Boolean,
    hudLossOn: Boolean,
    hudResOn: Boolean,
    onHudFps: (Boolean) -> Unit,
    onHudMbps: (Boolean) -> Unit,
    onHudJitter: (Boolean) -> Unit,
    onHudLoss: (Boolean) -> Unit,
    onHudRes: (Boolean) -> Unit,
    onCycleRes: () -> Unit,
    onCycleBr: () -> Unit,
    onCycleFps: () -> Unit,
    onPreset: (Int, Int, Int) -> Unit,
    onReconnect: () -> Unit,
    onDisconnect: () -> Unit,
    onTransport: (String) -> Unit,
) {
    val statusColor = when {
        statusText.startsWith("Streaming") || statusText.contains("传输") -> StatusSuccess
        statusText.startsWith("Error") || statusText.contains("失败") -> StatusError
        statusText.startsWith("Connecting") -> StatusWarning
        else -> TextSecondary
    }

    Box(modifier = Modifier.fillMaxSize().zIndex(2f)) {
        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(12.dp)
                .fillMaxWidth(0.58f)
                .clip(RoundedCornerShape(12.dp))
                .background(PanelBg)
                .border(1.dp, PanelBorder, RoundedCornerShape(12.dp))
                .padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            // Header
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    "uniLinker",
                    style = MaterialTheme.typography.titleMedium,
                    color = TextPrimary,
                    fontWeight = FontWeight.Bold,
                )
                Spacer(Modifier.width(8.dp))
                Text("投屏", style = MaterialTheme.typography.labelMedium, color = Copper)
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(8.dp).clip(CircleShape).background(statusColor))
                Spacer(Modifier.width(6.dp))
                Text(statusText, style = MaterialTheme.typography.bodySmall, color = statusColor)
            }

            // Quality presets
            SectionLabel("画质预设")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                PresetChip("流畅", resIdx == 0 && brIdx == 0 && fpsIdx == 0, Modifier.weight(1f)) { onPreset(0, 0, 0) }
                PresetChip("均衡", resIdx == 0 && brIdx == 1 && fpsIdx == 0, Modifier.weight(1f)) { onPreset(0, 1, 0) }
                PresetChip("高清", resIdx == 1 && brIdx == 2 && fpsIdx == 0, Modifier.weight(1f)) { onPreset(1, 2, 0) }
            }

            // Advanced tiers
            SectionLabel("高级档位")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TierChip(resLabel, Modifier.weight(1f), onCycleRes)
                TierChip(brLabel, Modifier.weight(1f), onCycleBr)
                TierChip(fpsLabel, Modifier.weight(1f), onCycleFps)
            }

            // Transport (remote switch, auto-reconnect applies it)
            SectionLabel("传输方式")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TransportChip("SIPSorcery", transportMode == "sipsorcery", Modifier.weight(1f)) { onTransport("sipsorcery") }
                TransportChip("Native RTX", transportMode == "rtx", Modifier.weight(1f)) { onTransport("rtx") }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TransportChip("Tile（USB 直连）", transportMode == "tile", Modifier.fillMaxWidth()) { onTransport("tile") }
            }
            Text(
                "切换后自动重连生效 · SIPSorcery 稳定 / RTX 抗丢包 / Tile 低延迟高画质",
                style = MaterialTheme.typography.labelSmall, color = TextSecondary,
            )

            // HUD visibility
            SectionLabel("HUD 显示项")
            HudToggleRow("帧率", hudFpsOn, onHudFps)
            HudToggleRow("码率", hudMbpsOn, onHudMbps)
            HudToggleRow("抖动", hudJitterOn, onHudJitter)
            HudToggleRow("丢包", hudLossOn, onHudLoss)
            HudToggleRow("分辨率", hudResOn, onHudRes)

            // Connection
            SectionLabel("连接")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                Button(
                    onClick = onReconnect,
                    modifier = Modifier.weight(1f).height(40.dp),
                    shape = RoundedCornerShape(8.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = OkBg, contentColor = StatusSuccess),
                ) { Text("重连", fontWeight = FontWeight.SemiBold) }
                Button(
                    onClick = onDisconnect,
                    modifier = Modifier.weight(1f).height(40.dp),
                    enabled = streaming,
                    shape = RoundedCornerShape(8.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = DangerBg, contentColor = StatusError,
                        disabledContainerColor = ChipBg,
                        disabledContentColor = TextTertiary),
                ) { Text("断开", fontWeight = FontWeight.SemiBold) }
            }
        }
    }
}

/** Section header: copper label with a leading tick. */
@Composable
internal fun SectionLabel(text: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(3.dp, 12.dp).background(Copper))
        Spacer(Modifier.width(6.dp))
        Text(text, style = MaterialTheme.typography.labelMedium, color = Copper,
            fontWeight = FontWeight.SemiBold)
    }
}

/** Quality-preset chip: copper-tinted when selected, outlined when not. */
@Composable
internal fun PresetChip(label: String, selected: Boolean, modifier: Modifier, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        modifier = modifier.height(36.dp),
        shape = RoundedCornerShape(8.dp),
        border = if (selected) androidx.compose.foundation.BorderStroke(1.5.dp, Copper) else null,
        colors = ButtonDefaults.buttonColors(
            containerColor = if (selected) ChipSelectedBg else ChipBg,
            contentColor = if (selected) TextPrimary else TextSecondary),
    ) {
        Text(label, fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal)
    }
}

/** Advanced tier cycle chip. */
@Composable
internal fun TierChip(label: String, modifier: Modifier, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        modifier = modifier.height(36.dp),
        shape = RoundedCornerShape(8.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = ChipBg, contentColor = TextPrimary),
    ) { Text(label) }
}

/** Transport chip: strong selected state (copper border + tint). */
@Composable
internal fun TransportChip(label: String, selected: Boolean, modifier: Modifier, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        modifier = modifier.height(38.dp),
        shape = RoundedCornerShape(8.dp),
        border = if (selected) androidx.compose.foundation.BorderStroke(1.5.dp, Copper) else null,
        colors = ButtonDefaults.buttonColors(
            containerColor = if (selected) ChipSelectedBg else ChipBg,
            contentColor = if (selected) TextPrimary else TextSecondary),
    ) {
        Text(label, fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal)
    }
}

/** One HUD-metric visibility row: label + Switch, copper-styled. */
@Composable
internal fun HudToggleRow(label: String, state: Boolean, onToggle: (Boolean) -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(6.dp))
            .background(ChipBg)
            .padding(horizontal = 10.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, style = MaterialTheme.typography.bodySmall, color = TextPrimary)
        Switch(
            checked = state,
            onCheckedChange = { onToggle(it) },
            modifier = Modifier.size(34.dp, 20.dp),
        )
    }
}
