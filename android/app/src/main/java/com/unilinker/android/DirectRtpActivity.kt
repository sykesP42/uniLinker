package com.unilinker.android

import android.graphics.SurfaceTexture
import android.os.Bundle
import android.view.Surface
import android.view.TextureView
import android.view.View
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.zIndex
import com.unilinker.android.mediatier.CaptureTier
import com.unilinker.android.mediatier.DirectRtpClient
import com.unilinker.android.mediatier.H264SurfaceDecoder
import com.unilinker.android.ui.theme.UniLinkerTheme
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Direct-RTP receiver screen (M3, docs/spec/M3-direct-rtp.md).
 *
 * UX: fullscreen immersive on entry (status + nav bars hidden), the video
 * surface fills the screen, everything else lives in a floating ball that
 * auto-dims after 3 s of inactivity — dragging it a little (or tapping)
 * toggles a translucent panel with the live HUD and the capture tier
 * controls. Launch via `adb shell am start -n
 * com.unilinker.android/.DirectRtpActivity --es connect "ip:9527"`.
 */
class DirectRtpActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        setContent {
            UniLinkerTheme {
                DirectRtpScreen(intent?.getStringExtra("connect"))
            }
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemBars()
    }

    private fun hideSystemBars() {
        // API 26-compatible immersive sticky (WindowInsetsController is 30+).
        @Suppress("DEPRECATION")
        window.decorView.systemUiVisibility = (
            View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                or View.SYSTEM_UI_FLAG_FULLSCREEN
                or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
            )
    }
}

private val resOptions = listOf(1280 to 800, 1920 to 1200, 2560 to 1600)
private val brOptions = listOf(4000, 8000, 16000)
private val fpsOptions = listOf(30, 60)

@Composable
fun DirectRtpScreen(initialAddress: String?) {
    val context = LocalContext.current
    var address by remember { mutableStateOf(initialAddress ?: "") }
    var client by remember { mutableStateOf<DirectRtpClient?>(null) }
    var decoder by remember { mutableStateOf<H264SurfaceDecoder?>(null) }
    var surfaceReady by remember { mutableStateOf(false) }
    var statusText by remember { mutableStateOf("Idle") }
    var decodedCount by remember { mutableIntStateOf(0) }
    val scope = rememberCoroutineScope()
    var frameJob by remember { mutableStateOf<Job?>(null) }

    val prefs = remember { (context as android.app.Activity).getPreferences(android.content.Context.MODE_PRIVATE) }
    var resIdx by remember { mutableIntStateOf(prefs.getInt("tier_res", 0)) }
    var brIdx by remember { mutableIntStateOf(prefs.getInt("tier_br", 1)) }
    var fpsIdx by remember { mutableIntStateOf(prefs.getInt("tier_fps", 0)) }
    var hudText by remember { mutableStateOf("") }
    var ballExpanded by remember { mutableStateOf(false) }

    var hudFps by remember { mutableStateOf(0.0) }
    var hudMbps by remember { mutableStateOf(0.0) }
    var hudJitter by remember { mutableStateOf(0.0) }

    // HUD dashboard: per-metric switches (persisted) + draggable position.
    data class HudItem(val key: String, val label: String, val default: Boolean)
    val hudItems = listOf(
        HudItem("fps", "帧率", true),
        HudItem("mbps", "码率", true),
        HudItem("jitter", "抖动", true),
        HudItem("loss", "丢包", true),
        HudItem("res", "分辨率", true),
    )
    val hudOn = hudItems.associate { it.key to mutableStateOf(prefs.getBoolean("hud_" + it.key, it.default)) }
    var hudOffsetX by remember { mutableIntStateOf(prefs.getInt("hud_x", 0)) }
    var hudOffsetY by remember { mutableIntStateOf(prefs.getInt("hud_y", 0)) }
    fun persistHud() {
        prefs.edit()
            .putBoolean("hud_fps", hudOn["fps"]!!.value)
            .putBoolean("hud_mbps", hudOn["mbps"]!!.value)
            .putBoolean("hud_jitter", hudOn["jitter"]!!.value)
            .putBoolean("hud_loss", hudOn["loss"]!!.value)
            .putBoolean("hud_res", hudOn["res"]!!.value)
            .putInt("hud_x", hudOffsetX)
            .putInt("hud_y", hudOffsetY)
            .apply()
    }
    fun hudLines(): List<String> {
        val st = client?.receiverStats
        val lines = mutableListOf<String>()
        if (hudOn["fps"]!!.value) lines.add("帧率 %.0f fps".format(hudFps))
        if (hudOn["mbps"]!!.value) lines.add("码率 %.1f Mbps".format(hudMbps))
        if (hudOn["jitter"]!!.value) lines.add("抖动 %.0f ms".format(hudJitter))
        if (hudOn["loss"]!!.value) {
            val total = st?.first?.plus(st.second ?: 0) ?: 0
            val loss = st?.second ?: 0
            lines.add("丢包 %.2f%% (%d/%d)".format(if (total > 0) loss * 100.0 / total else 0.0, loss, total))
        }
        if (hudOn["res"]!!.value) {
            val r = decoder?.streamSize?.value
            lines.add("分辨率 " + (r?.let { "${it.first}×${it.second}" } ?: "—"))
        }
        lines.add("帧数 $decodedCount")
        return lines
    }

    fun toggleConnect() {
        if (client != null) {
            client?.stop()
            client = null
            statusText = "Idle"
            return
        }
        val parsed = address.split(":")
        if (parsed.size != 2) {
            statusText = "Error: address must be ip:port"
            return
        }
        prefs.edit()
            .putInt("tier_res", resIdx)
            .putInt("tier_br", brIdx)
            .putInt("tier_fps", fpsIdx)
            .apply()
        val tier = CaptureTier(resOptions[resIdx].first, resOptions[resIdx].second, brOptions[brIdx], fpsOptions[fpsIdx])
        val pairingPort = parsed.getOrNull(2)?.toIntOrNull() ?: 9528
        val newClient = DirectRtpClient(parsed[0], parsed[1].toIntOrNull() ?: 9527)
        newClient.pinnedFingerprint = prefs.getString("pinned_fp", null)
        android.util.Log.d("DirectRtp", "toggle: pinned=${newClient.pinnedFingerprint}")
        client = newClient
        statusText = "Connecting…"
        scope.launch {
            // M4 pairing: first contact probes the sender's TLS certificate;
            // the human compares the 6-digit SAS code with the code shown on
            // the sender (pairing.txt / sender UI) and confirms. The
            // fingerprint is then pinned — every later connection verifies it.
            if (newClient.pinnedFingerprint == null) {
                android.util.Log.d("DirectRtp", "pairing: probing…")
                val probe = newClient.probePairing()
                android.util.Log.d("DirectRtp", "pairing: probe done ok=${probe != null}")
                if (probe == null) {
                    statusText = "Error: ${newClient.error.value}"
                    client = null
                    return@launch
                }
                val (sasCode, fingerprint) = probe
                val confirmed = withContext(kotlinx.coroutines.Dispatchers.Main) {
                    kotlinx.coroutines.suspendCancellableCoroutine { cont ->
                        val dlg = android.app.AlertDialog.Builder(context as android.app.Activity)
                            .setTitle("配对确认")
                            .setMessage(
                                "请在分享端核对配对码是否为:\n\n    $sasCode\n\n设备指纹:\n$fingerprint\n\n两端一致后点击确认建立信任。"
                            )
                            .setPositiveButton("确认一致") { _, _ -> cont.resume(true, null) }
                            .setNegativeButton("不一致") { _, _ -> cont.resume(false, null) }
                            .setOnCancelListener { cont.resume(false, null) }
                            .show()
                        cont.invokeOnCancellation { dlg.dismiss() }
                    }
                }
                if (!confirmed) {
                    statusText = "Error: 配对被拒绝"
                    client = null
                    return@launch
                }
                newClient.pinFingerprint(fingerprint)
                prefs.edit().putString("pinned_fp", fingerprint).apply()
            }
            val ok = newClient.connect(tier)
            if (!ok) {
                statusText = "Error: ${newClient.error.value}"
                client = null
                return@launch
            }
            statusText = "Streaming"
            frameJob?.cancel()
            frameJob = scope.launch {
                newClient.frames.collect { au ->
                    if (surfaceReady) {
                        decoder?.submitAnnexB(au, System.nanoTime() / 1000)
                        decodedCount++
                    }
                }
            }
        }
    }

    // Deep-link auto-connect (mirrors MainActivity's connectFired behavior):
    // `am start --es connect "ip:port"` starts streaming without touching the UI.
    LaunchedEffect(initialAddress) {
        if (!initialAddress.isNullOrBlank()) toggleConnect()
    }

    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        // Video surface fills the screen; every configured tier is 16:10, the
        // same ratio as the panel display, so fill = correct aspect.
        // TextureView, not SurfaceView: it composes as a NORMAL view, so the
        // HUD/floating ball sit above it without Z-order tricks, and Compose
        // can overlay anything. (setZOrderOnTop(true) made the video cover
        // the entire Compose layer — HUD was invisible.)
        AndroidView(
            modifier = Modifier.fillMaxSize(),
            factory = { ctx ->
                android.view.TextureView(ctx).apply {
                    surfaceTextureListener = object : TextureView.SurfaceTextureListener {
                        override fun onSurfaceTextureAvailable(
                            st: SurfaceTexture,
                            width: Int,
                            height: Int,
                        ) {
                            decoder?.close()
                            decoder = H264SurfaceDecoder(Surface(st))
                            surfaceReady = true
                        }

                        override fun onSurfaceTextureSizeChanged(
                            st: SurfaceTexture,
                            width: Int,
                            height: Int,
                        ) {}

                        override fun onSurfaceTextureDestroyed(st: SurfaceTexture): Boolean {
                            surfaceReady = false
                            decoder?.close()
                            decoder = null
                            return true
                        }

                        override fun onSurfaceTextureUpdated(st: SurfaceTexture) {}
                    }
                }
            },
        )

        // HUD stat updater: rolling decoded-fps, wire bitrate, jitter — the
        // dashboard reads these raw values through its per-metric switches.
        if (client != null) {
            LaunchedEffect(Unit) {
                var lastFrames = 0
                var lastBytes = 0L
                var lastTime = System.nanoTime()
                while (true) {
                    kotlinx.coroutines.delay(500)
                    val now = System.nanoTime()
                    val dtSec = (now - lastTime) / 1e9
                    val st = client?.receiverStats ?: continue
                    val dFrames = decodedCount - lastFrames
                    val dBytes = st.third - lastBytes
                    lastFrames = decodedCount
                    lastBytes = st.third
                    lastTime = now
                    if (dtSec <= 0) continue
                    hudFps = dFrames / dtSec
                    hudMbps = dBytes * 8 / dtSec / 1e6
                    hudJitter = client?.receiverJitter ?: 0.0
                }
            }
        }

        // HUD dashboard — default TOP-END, draggable, per-metric switches.
        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(10.dp)
                .offset { IntOffset(hudOffsetX, hudOffsetY) }
                .background(Color(0x55000000), RoundedCornerShape(8.dp))
                .padding(horizontal = 8.dp, vertical = 5.dp)
                .pointerInput(Unit) {
                    detectDragGestures(
                        onDrag = { change, drag ->
                            change.consume()
                            hudOffsetX += drag.x.toInt()
                            hudOffsetY += drag.y.toInt()
                        },
                        onDragEnd = { persistHud() },
                    )
                },
            verticalArrangement = Arrangement.spacedBy(1.dp),
        ) {
            hudLines().forEach { line ->
                Text(line, style = MaterialTheme.typography.labelSmall, color = Color(0xFFC8E6C9))
            }
        }

        if (client == null) {
            Text(
                text = statusText,
                modifier = Modifier
                    .align(Alignment.Center)
                    .background(Color(0x66000000))
                    .padding(horizontal = 10.dp, vertical = 4.dp),
                style = MaterialTheme.typography.bodyMedium,
                color = if (statusText.startsWith("Error")) Color.Red else Color.White,
            )
            Button(
                onClick = { toggleConnect() },
                modifier = Modifier.align(Alignment.Center).padding(top = 60.dp),
            ) {
                Text("Connect (direct RTP)")
            }
        }

        // Floating ball: drag anywhere, auto-dims after 3 s, tap toggles panel.
        FloatingBall(
            expanded = ballExpanded,
            streaming = client != null,
            onToggle = { ballExpanded = !ballExpanded },
        )

        if (ballExpanded) {
            FloatingPanel(
                statusText = statusText,
                hudText = hudText,
                decodedCount = decodedCount,
                resIdx = resIdx,
                brIdx = brIdx,
                fpsIdx = fpsIdx,
                streaming = client != null,
                onCycleRes = {
                    resIdx = (resIdx + 1) % resOptions.size
                    prefs.edit().putInt("tier_res", resIdx).apply()
                },
                onCycleBr = {
                    brIdx = (brIdx + 1) % brOptions.size
                    prefs.edit().putInt("tier_br", brIdx).apply()
                },
                onCycleFps = {
                    fpsIdx = (fpsIdx + 1) % fpsOptions.size
                    prefs.edit().putInt("tier_fps", fpsIdx).apply()
                },
                onReconnect = {
                    client?.stop()
                    client = null
                    toggleConnect()
                },
                onDisconnect = {
                    client?.stop()
                    client = null
                    ballExpanded = false
                    statusText = "Idle"
                },
            )
        }
    }
}

private fun hideBallKeyboard(context: android.content.Context) {
    val imm = context.getSystemService(android.content.Context.INPUT_METHOD_SERVICE)
        as? android.view.inputmethod.InputMethodManager
    (context as? android.app.Activity)?.currentFocus?.let {
        imm?.hideSoftInputFromWindow(it.windowToken, 0)
    }
}

/** Draggable floating ball; auto-dims after 3 s without touch. Tap = toggle. */
@Composable
private fun FloatingBall(expanded: Boolean, streaming: Boolean, onToggle: () -> Unit) {
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
            .background(if (dimmed) Color(0x33C0C0C0) else Color(0x99C0C0C0))
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
            Text(
                text = if (expanded) "✕" else "◎",
                color = if (dimmed) Color(0x66FFFFFF) else Color.White,
                style = MaterialTheme.typography.titleMedium,
            )
        }
    }
}

/** Translucent settings + parameters panel shown when the ball is expanded. */
@Composable
private fun FloatingPanel(
    statusText: String,
    hudText: String,
    decodedCount: Int,
    resIdx: Int,
    brIdx: Int,
    fpsIdx: Int,
    streaming: Boolean,
    onCycleRes: () -> Unit,
    onCycleBr: () -> Unit,
    onCycleFps: () -> Unit,
    onReconnect: () -> Unit,
    onDisconnect: () -> Unit,
) {
    Box(modifier = Modifier.fillMaxSize().zIndex(2f)) {
        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(16.dp)
                .fillMaxWidth(0.44f)
                .clip(RoundedCornerShape(10.dp))
                .background(Color(0xE6141414))
                .padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Text("uniLinker · Direct", style = MaterialTheme.typography.titleSmall, color = Color.White)
            Text(statusText, style = MaterialTheme.typography.bodySmall, color = Color(0xFF9BE29B))
            Text(hudText.ifEmpty { "no stats yet" }, style = MaterialTheme.typography.bodySmall, color = Color(0xFFBDBDBD))
            Text("decoded frames: $decodedCount", style = MaterialTheme.typography.bodySmall, color = Color(0xFFBDBDBD))

            Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
                Button(onClick = onCycleRes, modifier = Modifier.weight(1f)) {
                    Text("${resOptions[resIdx].first}×${resOptions[resIdx].second}")
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
                Button(onClick = onCycleBr, modifier = Modifier.weight(1f)) { Text("${brOptions[brIdx] / 1000} Mbps") }
                Button(onClick = onCycleFps, modifier = Modifier.weight(1f)) { Text("${fpsOptions[fpsIdx]} fps") }
            }
            Text(
                "tier changes apply on the next connect (断开重连生效)",
                style = MaterialTheme.typography.labelSmall,
                color = Color(0xFF9E9E9E),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
                Button(onClick = onReconnect, modifier = Modifier.weight(1f)) { Text("Reconnect") }
                Button(
                    onClick = onDisconnect,
                    modifier = Modifier.weight(1f),
                    enabled = streaming,
                ) { Text("Disconnect") }
            }
        }
    }
}
