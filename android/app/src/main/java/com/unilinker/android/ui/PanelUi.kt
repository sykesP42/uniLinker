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
import androidx.compose.foundation.clickable
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

/** Connection lifecycle for the status line — replaces the old
 *  startsWith("Error")/contains("传输") magic-string matching that broke
 *  whenever a copy tweak moved a word. */
enum class PanelState { Idle, Connecting, Streaming, Error }

/** Raw sender errors mapped to copy a human can act on. */
internal fun friendlyError(raw: String?): String {
    val r = raw ?: return "未知错误"
    return when {
        r.contains("ECONNREFUSED", true) || r.contains("ConnectException", true) ||
            r.contains("connect failed", true) ->
            "无法连接分享端——请确认地址端口正确、分享端已启动"
        r.contains("fingerprint mismatch", true) ->
            "设备指纹与首次配对不符——分享端证书已变化，需重新配对"
        r.contains("pairing", true) -> "配对探测失败——请确认分享端已启动"
        r.contains("404") -> "信令服务不可用（404）"
        r.contains("WRONG_VERSION_NUMBER", true) -> "端口指向了非 TLS 监听——请使用配对端口 9528"
        else -> r
    }
}

/** Quality presets as (resIdx, brIdx, fpsIdx) — index-based so the tables
 *  stay owned by the activity; 极致 pairs the 32M budget with 60fps. */
internal data class QualityPreset(val label: String, val res: Int, val br: Int, val fps: Int)
internal val QualityPresets = listOf(
    QualityPreset("流畅", 0, 0, 0),
    QualityPreset("均衡", 0, 1, 0),
    QualityPreset("高清", 1, 2, 0),
    QualityPreset("极致", 0, 2, 1),
)

/** Receiver-side diagnostics snapshot for the 诊断 section (polled at 1 Hz
 *  by the section itself while expanded — not on the hot recompose path). */
data class DiagStats(
    val medium: String,
    val received: Int,
    val lost: Int,
    val tileAssembled: Int,
    val tileDropped: Int,
    val tileOrphans: Int,
    val authFailures: Int,
)

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
    state: PanelState,
    statusDetail: String,
    transient: String?,
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
    diagProvider: (() -> DiagStats?)? = null,
    onRefresh: (() -> Unit)? = null,
    chaosLoss: Int = 0,
    chaosDup: Int = 0,
    chaosLagMs: Int = 0,
    onChaos: (Int, Int, Int) -> Unit = { _, _, _ -> },
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
    // Status line: a transient note (transport switching, chaos injection)
    // overrides the lifecycle state for a few seconds; otherwise the state
    // maps to steady copy + color. Streaming appends the link medium.
    val mediumLabel = diagProvider?.invoke()?.medium
    val (statusText, statusColor) = when {
        transient != null -> transient to StatusWarning
        state == PanelState.Streaming -> "投屏中 · $mediumLabel" to StatusSuccess
        state == PanelState.Connecting -> "连接中…" to StatusWarning
        state == PanelState.Error -> "连接失败：${friendlyError(statusDetail)}" to StatusError
        else -> "未连接" to TextSecondary
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

            // Quality presets — selected derived from the CURRENT tier, so a
            // manual tier change re-aligns the highlight (the old exact-triple
            // chips went all-dark the moment anything cycled).
            SectionLabel("画质")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                QualityPresets.forEachIndexed { i, p ->
                    PresetChip(p.label, resIdx == p.res && brIdx == p.br && fpsIdx == p.fps,
                        Modifier.weight(1f)) { onPreset(p.res, p.br, p.fps) }
                }
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
                TransportChip("SIPSorcery · 稳定", transportMode == "sipsorcery", Modifier.weight(1f)) { onTransport("sipsorcery") }
                TransportChip("Native RTX · 抗丢包", transportMode == "rtx", Modifier.weight(1f)) { onTransport("rtx") }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TransportChip("Tile · 低延迟推荐", transportMode == "tile", Modifier.fillMaxWidth()) { onTransport("tile") }
            }
            Text(
                "切换后自动重连生效",
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
                if (onRefresh != null) {
                    Button(
                        onClick = onRefresh,
                        modifier = Modifier.weight(1f).height(40.dp),
                        enabled = streaming,
                        shape = RoundedCornerShape(8.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = ChipBg, contentColor = TextPrimary,
                            disabledContainerColor = ChipBg,
                            disabledContentColor = TextTertiary),
                    ) { Text("刷新画面", fontWeight = FontWeight.SemiBold) }
                }
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

            if (diagProvider != null) {
                DiagnosticsSection(diagProvider)
            }

            ChaosSection(chaosLoss, chaosDup, chaosLagMs, onChaos)
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

/** Collapsible diagnostics section: receiver-side counters polled at 1 Hz
 *  WHILE expanded (self-contained polling — the hot recompose path never
 *  touches it). Drop/orphan/auth counters turn amber when non-zero. */
@Composable
private fun DiagnosticsSection(provider: () -> DiagStats?) {
    var expanded by remember { mutableStateOf(false) }
    var diag by remember { mutableStateOf<DiagStats?>(null) }

    LaunchedEffect(expanded) {
        while (expanded) {
            diag = provider()
            delay(1000)
        }
    }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(6.dp))
            .background(ChipBg)
            .clickable { expanded = !expanded }
            .padding(horizontal = 10.dp, vertical = 6.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text("诊断", style = MaterialTheme.typography.bodySmall, color = TextPrimary)
        Text(
            if (expanded) "收起 ▲" else "展开 ▼",
            style = MaterialTheme.typography.labelSmall,
            color = Copper,
        )
    }

    if (!expanded) return
    val d = diag ?: return
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(6.dp))
            .background(ChipBg)
            .padding(horizontal = 10.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        DiagRow("介质", d.medium, TextPrimary)
        DiagRow("已收 / 丢包", "${d.received} / ${d.lost}", TextPrimary)
        DiagRow("Tile 已组装", "${d.tileAssembled}", TextPrimary)
        DiagRow("Tile 丢弃", "${d.tileDropped}",
            if (d.tileDropped > 0) StatusWarning else TextPrimary)
        DiagRow("Tile 孤儿", "${d.tileOrphans}",
            if (d.tileOrphans > 0) StatusWarning else TextPrimary)
        DiagRow("认证失败", "${d.authFailures}",
            if (d.authFailures > 0) StatusWarning else TextPrimary)
    }
}

@Composable
private fun DiagRow(label: String, value: String, color: Color) {
    Row(modifier = Modifier.fillMaxWidth()) {
        Text(
            label,
            modifier = Modifier.weight(1f),
            style = MaterialTheme.typography.labelSmall.copy(fontFeatureSettings = "tnum"),
            color = TextTertiary,
        )
        Text(
            value,
            style = MaterialTheme.typography.labelSmall.copy(fontFeatureSettings = "tnum"),
            color = color,
        )
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

/** Collapsible network-simulation section (chaos injection): live-settable
 *  loss/dup/lag sliders — a demo + acceptance tool, off by default. The
 *  header summary turns amber while anything is injected. */
@Composable
private fun ChaosSection(loss: Int, dup: Int, lagMs: Int, onChaos: (Int, Int, Int) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    val active = loss > 0 || dup > 0 || lagMs > 0

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(6.dp))
            .background(ChipBg)
            .clickable { expanded = !expanded }
            .padding(horizontal = 10.dp, vertical = 6.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (active) {
                Box(Modifier.size(6.dp).clip(CircleShape).background(StatusWarning))
                Spacer(Modifier.width(6.dp))
            }
            Text("网络模拟", style = MaterialTheme.typography.bodySmall, color = TextPrimary)
        }
        Text(
            if (expanded) "收起 ▲"
            else if (active) "丢包$loss% · 重复$dup% · 延迟${lagMs}ms"
            else "展开 ▼",
            style = MaterialTheme.typography.labelSmall,
            color = if (active) StatusWarning else Copper,
        )
    }

    if (!expanded) return
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(6.dp))
            .background(ChipBg)
            .padding(horizontal = 12.dp, vertical = 4.dp),
    ) {
        ChaosSlider("丢包", "%", loss, 0f, 30f, 29) { onChaos(it.toInt(), dup, lagMs) }
        ChaosSlider("重复", "%", dup, 0f, 20f, 19) { onChaos(loss, it.toInt(), lagMs) }
        ChaosSlider("延迟", "ms", lagMs, 0f, 500f, 9) { onChaos(loss, dup, (it / 50).toInt() * 50) }
        Text(
            "即时生效 · 用于演示与验收（高丢包会触发自愈）",
            style = MaterialTheme.typography.labelSmall,
            color = TextTertiary,
        )
        Spacer(Modifier.height(6.dp))
    }
}

@Composable
private fun ChaosSlider(label: String, unit: String, value: Int, min: Float, max: Float, steps: Int, onCommit: (Float) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(
            label,
            style = MaterialTheme.typography.bodySmall,
            color = TextPrimary,
            modifier = Modifier.width(40.dp),
        )
        androidx.compose.material3.Slider(
            value = value.toFloat(),
            onValueChange = onCommit,
            valueRange = min..max,
            steps = steps,
            modifier = Modifier.weight(1f).height(28.dp),
            colors = androidx.compose.material3.SliderDefaults.colors(
                thumbColor = Copper,
                activeTrackColor = Copper,
                inactiveTrackColor = BorderStrong,
            ),
        )
        Text(
            "$value$unit",
            style = MaterialTheme.typography.labelSmall.copy(fontFeatureSettings = "tnum"),
            color = TextSecondary,
            modifier = Modifier.width(52.dp),
        )
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
