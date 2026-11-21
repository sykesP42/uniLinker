package com.unilinker.android.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import com.unilinker.android.ui.theme.BackgroundDeep
import com.unilinker.android.ui.theme.BorderStrong
import com.unilinker.android.ui.theme.Copper
import com.unilinker.android.ui.theme.StatusError
import com.unilinker.android.ui.theme.TextSecondary
import com.unilinker.android.ui.theme.TextTertiary

/**
 * HUD dashboard + real-time charts for the DirectRtp receiver screen.
 *
 * RollingSamples is a fixed-capacity ring pushed at the HUD poll cadence
 * (500 ms) — a 60-slot ring is a 30 s window. The charts are pure Canvas
 * polylines (no chart library): each trace normalizes to its own window
 * max, so fps and Mbps can share one plot despite different units.
 */

class RollingSamples(private val capacity: Int = 60) {
    private val fps = FloatArray(capacity)
    private val mbps = FloatArray(capacity)
    private val loss = FloatArray(capacity)
    private var count = 0
    private var head = 0

    /** Snapshot-state version bumped on every push: the Canvas draw lambdas
     *  read it, so a push invalidates the DRAW phase directly — the charts
     *  track the ring live with zero recomposition (a skipped composable
     *  with an unchanged params list would never redraw otherwise). */
    var version by androidx.compose.runtime.mutableIntStateOf(0)
        private set

    fun push(f: Float, m: Float, l: Float) {
        fps[head] = f; mbps[head] = m; loss[head] = l
        head = (head + 1) % capacity
        if (count < capacity) count++
        version++
    }

    /** Oldest→newest copy of one track; size = min(count, capacity). */
    fun track(index: Int): FloatArray {
        val out = FloatArray(count)
        val start = if (count < capacity) 0 else head
        for (i in 0 until count) {
            val v = when (index) {
                0 -> fps[(start + i) % capacity]
                1 -> mbps[(start + i) % capacity]
                else -> loss[(start + i) % capacity]
            }
            out[i] = v
        }
        return out
    }
}

/** Draw one polyline across the canvas, normalized to the track max. */
private fun androidx.compose.ui.graphics.drawscope.DrawScope.drawTrace(
    values: FloatArray,
    color: Color,
    strokePx: Float,
) {
    if (values.size < 2) return
    var max = 0f
    for (v in values) if (v > max) max = v
    if (max <= 0f) max = 1f
    val w = size.width
    val h = size.height
    val path = Path()
    for (i in values.indices) {
        val x = i * w / (values.size - 1)
        val y = h - (values[i] / max) * (h - strokePx) - strokePx
        if (i == 0) path.moveTo(x, y) else path.lineTo(x, y)
    }
    // no fill: overlapping traces would muddy each other
    drawPath(path, color, style = Stroke(width = strokePx))
}

/** Mini two-trace chart (fps copper + Mbps secondary) for the HUD card. */
@Composable
fun MiniChart(samples: RollingSamples, modifier: Modifier = Modifier) {
    Canvas(modifier = modifier.height(44.dp).fillMaxWidth()) {
        @Suppress("UNUSED_EXPRESSION") samples.version // draw-phase state read → live redraw
        val w = size.width
        val h = size.height
        val stroke = 2.dp.toPx()
        // baseline
        drawLine(BorderStrong, Offset(0f, h - 1), Offset(w, h - 1), 1f)
        drawTrace(samples.track(0), Copper, stroke)
        drawTrace(samples.track(1), TextSecondary, stroke)
    }
}

/** Full three-trace chart card for the panel (fps / Mbps / loss %). */
@Composable
fun PanelChart(samples: RollingSamples, modifier: Modifier = Modifier) {
    Column(modifier = modifier) {
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Legend("帧率", Copper)
            Legend("码率", TextSecondary)
            Legend("丢包", StatusError)
        }
        Canvas(
            modifier = Modifier
                .height(110.dp)
                .fillMaxWidth()
                .background(BackgroundDeep.copy(alpha = 0.6f), RoundedCornerShape(6.dp)),
        ) {
            @Suppress("UNUSED_EXPRESSION") samples.version // draw-phase state read → live redraw
            val w = size.width
            val h = size.height
            val stroke = 2.dp.toPx()
            // quarter gridlines for scale reading
            for (q in 1..3) {
                val y = h * q / 4
                drawLine(BorderStrong, Offset(0f, y), Offset(w, y), 1f)
            }
            drawTrace(samples.track(0), Copper, stroke)
            drawTrace(samples.track(1), TextSecondary, stroke)
            drawTrace(samples.track(2), StatusError, stroke)
        }
    }
}

@Composable
private fun Legend(label: String, color: Color) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Canvas(Modifier.size(10.dp, 3.dp)) { drawRect(color) }
        Spacer(Modifier.width(4.dp))
        Text(label, style = MaterialTheme.typography.labelSmall, color = TextTertiary)
    }
}

/** The HUD card: metric rows + optional mini chart, draggable, copper. */
@Composable
fun HudDashboard(
    rows: List<Triple<String, String, Color>>,
    samples: RollingSamples?,
    chartOn: Boolean,
    mediumOn: Boolean,
    medium: String,
    offsetX: Int,
    offsetY: Int,
    sampleTick: Int = 0,
    modifier: Modifier = Modifier,
    onDrag: (Float, Float) -> Unit,
    onDragEnd: () -> Unit,
) {
    Column(
        modifier = modifier
            .offset { IntOffset(offsetX, offsetY) }
            .width(190.dp)
            .background(BackgroundDeep.copy(alpha = 0.67f), RoundedCornerShape(8.dp))
            .padding(horizontal = 8.dp, vertical = 5.dp)
            .pointerInput(Unit) {
                detectDragGestures(
                    onDrag = { change, drag ->
                        change.consume()
                        onDrag(drag.x, drag.y)
                    },
                    onDragEnd = onDragEnd,
                )
            },
        verticalArrangement = Arrangement.spacedBy(1.dp),
    ) {
        rows.forEach { (label, value, color) ->
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
        if (mediumOn) {
            Row(modifier = Modifier.fillMaxWidth()) {
                Text("介质", modifier = Modifier.weight(1f),
                    style = MaterialTheme.typography.labelSmall, color = TextTertiary)
                Text(medium,
                    style = MaterialTheme.typography.labelSmall, color = TextSecondary)
            }
        }
        if (chartOn && samples != null) {
            Spacer(Modifier.height(2.dp))
            // sampleTick re-runs this block on every poll tick so the mini
            // chart tracks the ring buffer live (same-object redraws need
            // the state bump).
            @Suppress("UNUSED_EXPRESSION") sampleTick
            MiniChart(samples)
        }
    }
}
