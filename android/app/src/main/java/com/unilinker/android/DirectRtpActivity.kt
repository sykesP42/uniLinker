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
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.BorderStroke
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.border
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
                DirectRtpScreen(
                    intent?.getStringExtra("connect"),
                    intent.getIntExtra("chaos_loss", 0),
                    intent.getIntExtra("chaos_dup", 0),
                    intent.getIntExtra("chaos_lag", 0),
                )
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
// Default index 2 (16Mbps): NVENC's CBR overshoots its target ~2x, so an
// 8Mbps tier produces the 16Mbps the encoder actually emits — quality
// first, the tether link carries it easily.
private val brOptions = listOf(8000, 16000, 32000)
private val fpsOptions = listOf(30, 60)

@Composable
fun DirectRtpScreen(
    initialAddress: String?,
    chaosLoss: Int = 0,
    chaosDup: Int = 0,
    chaosLag: Int = 0,
) {
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
    var hudFpsOn by remember { mutableStateOf(prefs.getBoolean("hud_fps", true)) }
    var hudMbpsOn by remember { mutableStateOf(prefs.getBoolean("hud_mbps", true)) }
    var hudJitterOn by remember { mutableStateOf(prefs.getBoolean("hud_jitter", true)) }
    var hudLossOn by remember { mutableStateOf(prefs.getBoolean("hud_loss", true)) }
    var hudResOn by remember { mutableStateOf(prefs.getBoolean("hud_res", true)) }
    var transportMode by remember { mutableStateOf("sipsorcery") }
    var tileRenderer by remember { mutableStateOf<com.unilinker.android.mediatier.TileRenderer?>(null) }
    var wifiLock by remember { mutableStateOf<android.net.wifi.WifiManager.WifiLock?>(null) }
    var pendingSurface by remember { mutableStateOf<android.graphics.SurfaceTexture?>(null) }
    var textureViewRef by remember { mutableStateOf<android.view.TextureView?>(null) }
    val hudOn = mapOf(
        "fps" to hudFpsOn,
        "mbps" to hudMbpsOn,
        "jitter" to hudJitterOn,
        "loss" to hudLossOn,
        "res" to hudResOn,
    )
    var hudOffsetX by remember { mutableIntStateOf(prefs.getInt("hud_x", 0)) }
    var hudOffsetY by remember { mutableIntStateOf(prefs.getInt("hud_y", 0)) }
    fun persistHud() {
        prefs.edit()
            .putBoolean("hud_fps", hudFpsOn)
            .putBoolean("hud_mbps", hudMbpsOn)
            .putBoolean("hud_jitter", hudJitterOn)
            .putBoolean("hud_loss", hudLossOn)
            .putBoolean("hud_res", hudResOn)
            .putInt("hud_x", hudOffsetX)
            .putInt("hud_y", hudOffsetY)
            .apply()
    }
    // HUD metric rows: (label, value, color). Value colors encode health:
    // green = good, amber = degraded, red = bad. Fixed-width numeric fields
    // plus tabular numbers keep the panel from wobbling on every refresh.
    fun hudRows(): List<Triple<String, String, Color>> {
        val st = client?.receiverStats
        val rows = mutableListOf<Triple<String, String, Color>>()
        if (hudFpsOn) {
            val v = hudFps
            val c = when { v >= 25 -> Color(0xFF4CAF50); v >= 15 -> Color(0xFFFFC107); else -> Color(0xFFF44336) }
            rows.add(Triple("帧率", "%2.0f fps".format(v), c))
        }
        if (hudMbpsOn) {
            val v = hudMbps
            val c = when { v >= 6 -> Color(0xFF4CAF50); v >= 3 -> Color(0xFFFFC107); else -> Color(0xFFF44336) }
            rows.add(Triple("码率", "%4.1f Mbps".format(v), c))
        }
        if (hudJitterOn) {
            val v = hudJitter
            val c = when { v < 30 -> Color(0xFF4CAF50); v < 60 -> Color(0xFFFFC107); else -> Color(0xFFF44336) }
            rows.add(Triple("抖动", "%3.0f ms".format(v), c))
        }
        if (hudLossOn) {
            val (lost, total) = client?.receiverLossWindow ?: Pair(0, 0)
            val pct = if (total > 0) lost * 100.0 / total else 0.0
            val c = when { pct < 1 -> Color(0xFF4CAF50); pct < 5 -> Color(0xFFFFC107); else -> Color(0xFFF44336) }
            rows.add(Triple("丢包", "%4.1f%%".format(pct), c))
        }
        if (hudResOn) {
            val r = decoder?.streamSize?.value
            rows.add(Triple("分辨率", r?.let { "${it.first}×${it.second}" } ?: "—", Color(0xFFBDBDBD)))
        }
        return rows
    }

    fun toggleConnect() {
        if (client != null) {
            client?.stop()
            client = null
            statusText = "Idle"
            return
        }
        val parsed = address.split(":")
        if (parsed.size < 2) {
            statusText = "Error: address must be ip:port"
            return
        }
        prefs.edit()
            .putInt("tier_res", resIdx)
            .putInt("tier_br", brIdx)
            .putInt("tier_fps", fpsIdx)
            .apply()
        val tier = CaptureTier(resOptions[resIdx].first, resOptions[resIdx].second, brOptions[brIdx], fpsOptions[fpsIdx])
        // Deep link "ip:signalingPort[:pairingPort]" — the app talks TLS only
        // (device-cert pairing channel, default 9528). The second segment is
        // accepted for compatibility with browser-style addresses; pointing
        // TLS at the plain-HTTP 9527 listener fails WRONG_VERSION_NUMBER.
        val tlsPort = parsed.getOrNull(2)?.toIntOrNull() ?: 9528
        val newClient = DirectRtpClient(parsed[0], tlsPort)
        newClient.chaosLossPct = chaosLoss
        newClient.chaosDupPct = chaosDup
        newClient.chaosLagMs = chaosLag
        android.util.Log.d("DirectRtp", "chaos: loss=$chaosLoss% dup=$chaosDup% lag=${chaosLag}ms")
        newClient.pinnedFingerprint = prefs.getString("pinned_fp", null)
        android.util.Log.d("DirectRtp", "toggle: pinned=${newClient.pinnedFingerprint} tls=$tlsPort")
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
            transportMode = newClient.transportMode
            // WiFi 省电模式 RTT ~130ms 是无线投屏延迟的最大单项——STREAMING
            // 期间持有 HIGH_PERF 锁（R1，实测收益 ~120ms）。
            val wm = context.getSystemService(android.content.Context.WIFI_SERVICE)
                as android.net.wifi.WifiManager
            wifiLock?.release()
            wifiLock = wm.createWifiLock(
                android.net.wifi.WifiManager.WIFI_MODE_FULL_HIGH_PERF, "unilinker-direct")
                .apply { setReferenceCounted(false); acquire() }
            frameJob?.cancel()
            frameJob = scope.launch {
                if (newClient.transportIsTile) {
                    newClient.tileFrames.collect { tf ->
                        if (surfaceReady) {
                            val renderer = tileRenderer ?: run {
                                val dumpDir = (textureViewRef?.context as? android.app.Activity)
                                    ?.getExternalFilesDir(null)?.absolutePath
                                val r = com.unilinker.android.mediatier.TileRenderer(
                                    pendingSurface ?: return@collect, dumpDir)
                                tileRenderer = r
                                r
                            }
                            renderer.renderTileFrame(
                                tf.frameId, tf.gridCols, tf.gridRows, tf.frameW, tf.frameH, tf.tiles)
                            decodedCount++
                        }
                    }
                } else {
                    newClient.frames.collect { au ->
                        if (surfaceReady) {
                            val dec = decoder ?: run {
                                val st = pendingSurface ?: return@collect
                                val d = com.unilinker.android.mediatier.H264SurfaceDecoder(Surface(st))
                                decoder = d
                                d
                            }
                            dec.submitAnnexB(au, System.nanoTime() / 1000)
                            decodedCount++
                        }
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
                val tv = android.view.TextureView(ctx)
                tv.apply {
                    surfaceTextureListener = object : TextureView.SurfaceTextureListener {
                        override fun onSurfaceTextureAvailable(
                            st: SurfaceTexture,
                            width: Int,
                            height: Int,
                        ) {
                            // Store the surface; the mode-specific pipeline
                            // (MediaCodec OR tile canvas) is created LAZILY
                            // at first data. Attaching MediaCodec to the
                            // SurfaceTexture AND drawing tiles via canvas put
                            // two producers on one buffer queue = black screen.
                            pendingSurface = st
                            textureViewRef = tv
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
                            tileRenderer?.close()
                            tileRenderer = null
                            pendingSurface = null
                            textureViewRef = null
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
                // REAL output fps: the decoder's rendered-frame counter, NOT
                // the AU submission count — corrupted AUs that MediaCodec
                // discards must not show as frames (the HUD lied "30fps"
                // while the picture was mush). Zeros when the stream stops.
                var lastFrames = 0
                var lastBytes = 0L
                var lastTime = System.nanoTime()
                while (true) {
                    kotlinx.coroutines.delay(500)
                    val now = System.nanoTime()
                    val dtSec = (now - lastTime) / 1e9
                    val c = client
                    // Mode-aware: the tile renderer exists in BOTH modes
                    // (shared TextureView) — a blind Elvis chain would pin
                    // SIPSorcery sessions at 0 rendered frames.
                    val renderedNow = if (c?.transportIsTile == true)
                        (tileRenderer?.renderedFrames ?: 0)
                    else
                        (decoder?.decodedFrames ?: 0)
                    if (c == null || c.state.value != com.unilinker.android.mediatier.DirectRtpClient.State.STREAMING) {
                        hudFps = 0.0; hudMbps = 0.0
                        lastFrames = renderedNow
                        lastBytes = 0
                        lastTime = now
                        continue
                    }
                    val st = c.receiverStats ?: continue
                    val dFrames = renderedNow - lastFrames
                    val dBytes = st.third - lastBytes
                    lastFrames = renderedNow
                    lastBytes = st.third
                    lastTime = now
                    if (dtSec <= 0) continue
                    hudFps = dFrames / dtSec
                    hudMbps = dBytes * 8 / dtSec / 1e6
                    hudJitter = c.receiverJitter
                    android.util.Log.d("DirectRtp",
                        "hud: fps=${"%.1f".format(hudFps)} dFrames=$dFrames decoded=${decoder?.decodedFrames} decNull=${decoder == null} sReady=$surfaceReady subCount=$decodedCount state=${c.state.value} pkts=${st.first} assembled=${c.receiverAssembledAUs} corruptDropped=${c.receiverDroppedCorruptAUs} pendingNals=${c.receiverNalsBuffered}")
                }
            }
        }

        // HUD dashboard — default TOP-END, draggable, per-metric switches.
        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(10.dp)
                .offset { IntOffset(hudOffsetX, hudOffsetY) }
                .width(190.dp)
                .background(Color(0xAA101418), RoundedCornerShape(8.dp))
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
            hudRows().forEach { (label, value, color) ->
                Row(modifier = Modifier.fillMaxWidth()) {
                    Text(
                        label,
                        modifier = Modifier.weight(1f),
                        style = MaterialTheme.typography.labelSmall.copy(fontFeatureSettings = "tnum"),
                        color = Color(0xFF9E9E9E),
                    )
                    Text(
                        value,
                        style = MaterialTheme.typography.labelSmall.copy(fontFeatureSettings = "tnum"),
                        color = color,
                    )
                }
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
                resIdx = resIdx,
                brIdx = brIdx,
                fpsIdx = fpsIdx,
                streaming = client != null,
                transportMode = transportMode,
                hudFpsOn = hudFpsOn,
                hudMbpsOn = hudMbpsOn,
                hudJitterOn = hudJitterOn,
                hudLossOn = hudLossOn,
                hudResOn = hudResOn,
                onHudFps = { hudFpsOn = it; persistHud() },
                onHudMbps = { hudMbpsOn = it; persistHud() },
                onHudJitter = { hudJitterOn = it; persistHud() },
                onHudLoss = { hudLossOn = it; persistHud() },
                onHudRes = { hudResOn = it; persistHud() },
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
                    wifiLock?.release()
                    wifiLock = null
                    ballExpanded = false
                    statusText = "Idle"
                },
                onPreset = { r, b, f ->
                    resIdx = r; brIdx = b; fpsIdx = f
                    prefs.edit()
                        .putInt("tier_res", r).putInt("tier_br", b).putInt("tier_fps", f)
                        .apply()
                },
                onTransport = { mode ->
                    transportMode = mode
                    scope.launch {
                        val ok = client?.setTransport(mode) ?: false
                        statusText = if (ok) "传输方式 → $mode（重连生效）" else "传输方式设置失败"
                        if (ok) {
                            kotlinx.coroutines.delay(400)
                            client?.stop()
                            client = null
                            toggleConnect()
                        }
                    }
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

/** Translucent settings + parameters panel shown when the ball is expanded.
 *
 *  Redesign (M5): sectioned layout, high-contrast chips (selected = accent
 *  border + tinted fill), quality presets, remote transport switching over
 *  the TLS pairing channel, and honest status line. Everything scrollable —
 *  the panel outgrew a single screen.
 */
@Composable
private fun FloatingPanel(
    statusText: String,
    resIdx: Int,
    brIdx: Int,
    fpsIdx: Int,
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
    // Palette: dark card, blue accent, high-contrast text.
    val panelBg = Color(0xF5141618)
    val sectionTitle = Color(0xFF7FC4FF)
    val textPrimary = Color.White
    val textSecondary = Color(0xFFB8BEC5)
    val chipBg = Color(0xFF1E2228)
    val chipText = Color(0xFFD5DAE0)
    val chipSelectedBg = Color(0xFF173A5E)
    val accentBorder = Color(0xFF64B5F6)
    val statusGreen = Color(0xFF7BE3A0)
    val statusAmber = Color(0xFFFFC66B)
    val statusRed = Color(0xFFFF8A80)
    val dangerBg = Color(0xFF3A1B1B)
    val dangerText = Color(0xFFFF8A80)
    val okBg = Color(0xFF1B3A2A)
    val okText = Color(0xFF7BE3A0)

    val statusColor = when {
        statusText.startsWith("Streaming") || statusText.contains("传输") -> statusGreen
        statusText.startsWith("Error") || statusText.contains("失败") -> statusRed
        statusText.startsWith("Connecting") -> statusAmber
        else -> textSecondary
    }

    Box(modifier = Modifier.fillMaxSize().zIndex(2f)) {
        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(12.dp)
                .fillMaxWidth(0.58f)
                .clip(RoundedCornerShape(14.dp))
                .background(panelBg)
                .border(1.dp, Color(0xFF2A2F36), RoundedCornerShape(14.dp))
                .verticalScroll(rememberScrollState())
                .padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            // Header
            Row(verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
                Text(
                    "uniLinker",
                    style = MaterialTheme.typography.titleMedium,
                    color = textPrimary,
                    fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                )
                Spacer(Modifier.width(8.dp))
                Text("Direct", style = MaterialTheme.typography.labelMedium, color = sectionTitle)
            }
            Row(verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
                Box(Modifier.size(8.dp).clip(CircleShape).background(statusColor))
                Spacer(Modifier.width(6.dp))
                Text(statusText, style = MaterialTheme.typography.bodySmall, color = statusColor)
            }

            // Quality presets
            SectionLabel("画质预设", sectionTitle)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                PresetChip("流畅", resIdx == 0 && brIdx == 0 && fpsIdx == 0, chipBg, chipText,
                    chipSelectedBg, accentBorder, Modifier.weight(1f)) { onPreset(0, 0, 0) }
                PresetChip("均衡", resIdx == 0 && brIdx == 1 && fpsIdx == 0, chipBg, chipText,
                    chipSelectedBg, accentBorder, Modifier.weight(1f)) { onPreset(0, 1, 0) }
                PresetChip("高清", resIdx == 1 && brIdx == 2 && fpsIdx == 0, chipBg, chipText,
                    chipSelectedBg, accentBorder, Modifier.weight(1f)) { onPreset(1, 2, 0) }
            }

            // Advanced tiers
            SectionLabel("高级档位", sectionTitle)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TierChip("${resOptions[resIdx].first}x${resOptions[resIdx].second}", chipBg, chipText,
                    Modifier.weight(1f), onCycleRes)
                TierChip("${brOptions[brIdx] / 1000}M", chipBg, chipText,
                    Modifier.weight(1f), onCycleBr)
                TierChip("${fpsOptions[fpsIdx]}fps", chipBg, chipText,
                    Modifier.weight(1f), onCycleFps)
            }

            // Transport (remote switch, auto-reconnect applies it)
            SectionLabel("传输方式", sectionTitle)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TransportChip(
                    "SIPSorcery", transportMode == "sipsorcery",
                    chipSelectedBg, accentBorder, chipBg, chipText, Modifier.weight(1f),
                ) { onTransport("sipsorcery") }
                TransportChip(
                    "Native RTX", transportMode == "rtx",
                    chipSelectedBg, accentBorder, chipBg, chipText, Modifier.weight(1f),
                ) { onTransport("rtx") }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TransportChip(
                    "Tile（USB 直连）", transportMode == "tile",
                    chipSelectedBg, accentBorder, chipBg, chipText, Modifier.fillMaxWidth(),
                ) { onTransport("tile") }
            }
            Text(
                "切换后自动重连生效 · SIPSorcery 稳定 / RTX 抗丢包 / Tile 低延迟高画质",
                style = MaterialTheme.typography.labelSmall, color = textSecondary,
            )

            // HUD visibility
            SectionLabel("HUD 显示项", sectionTitle)
            HudToggleRow("帧率", hudFpsOn, onHudFps, textPrimary)
            HudToggleRow("码率", hudMbpsOn, onHudMbps, textPrimary)
            HudToggleRow("抖动", hudJitterOn, onHudJitter, textPrimary)
            HudToggleRow("丢包", hudLossOn, onHudLoss, textPrimary)
            HudToggleRow("分辨率", hudResOn, onHudRes, textPrimary)

            // Connection
            SectionLabel("连接", sectionTitle)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                Button(
                    onClick = onReconnect,
                    modifier = Modifier.weight(1f).height(40.dp),
                    shape = RoundedCornerShape(10.dp),
                    colors = androidx.compose.material3.ButtonDefaults.buttonColors(
                        containerColor = okBg, contentColor = okText),
                ) { Text("重连", fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold) }
                Button(
                    onClick = onDisconnect,
                    modifier = Modifier.weight(1f).height(40.dp),
                    enabled = streaming,
                    shape = RoundedCornerShape(10.dp),
                    colors = androidx.compose.material3.ButtonDefaults.buttonColors(
                        containerColor = dangerBg, contentColor = dangerText,
                        disabledContainerColor = Color(0xFF1A1D21),
                        disabledContentColor = Color(0xFF5A6068)),
                ) { Text("断开", fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold) }
            }
        }
    }
}

/** Section header: small accent-blue label with a leading tick. */
@Composable
private fun SectionLabel(text: String, color: Color) {
    Row(verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
        Box(Modifier.size(3.dp, 12.dp).background(color))
        Spacer(Modifier.width(6.dp))
        Text(text, style = MaterialTheme.typography.labelMedium, color = color,
            fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold)
    }
}

/** Quality-preset chip: filled when selected, outlined when not. */
@Composable
private fun PresetChip(
    label: String,
    selected: Boolean,
    bg: Color,
    fg: Color,
    selectedBg: Color,
    accentBorder: Color,
    modifier: Modifier,
    onClick: () -> Unit,
) {
    Button(
        onClick = onClick,
        modifier = modifier.height(36.dp),
        shape = RoundedCornerShape(10.dp),
        border = if (selected) androidx.compose.foundation.BorderStroke(1.5.dp, accentBorder) else null,
        colors = androidx.compose.material3.ButtonDefaults.buttonColors(
            containerColor = if (selected) selectedBg else bg,
            contentColor = if (selected) Color.White else fg),
    ) {
        Text(label, fontWeight = if (selected) androidx.compose.ui.text.font.FontWeight.Bold
        else androidx.compose.ui.text.font.FontWeight.Normal)
    }
}

/** Advanced tier cycle chip. */
@Composable
private fun TierChip(label: String, bg: Color, fg: Color, modifier: Modifier, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        modifier = modifier.height(36.dp),
        shape = RoundedCornerShape(10.dp),
        colors = androidx.compose.material3.ButtonDefaults.buttonColors(
            containerColor = bg, contentColor = fg),
    ) { Text(label) }
}

/** Transport chip: strong selected state (accent border + tint). */
@Composable
private fun TransportChip(
    label: String,
    selected: Boolean,
    selectedBg: Color,
    accentBorder: Color,
    bg: Color,
    fg: Color,
    modifier: Modifier,
    onClick: () -> Unit,
) {
    Button(
        onClick = onClick,
        modifier = modifier.height(38.dp),
        shape = RoundedCornerShape(10.dp),
        border = if (selected) androidx.compose.foundation.BorderStroke(1.5.dp, accentBorder) else null,
        colors = androidx.compose.material3.ButtonDefaults.buttonColors(
            containerColor = if (selected) selectedBg else bg,
            contentColor = if (selected) Color.White else fg),
    ) {
        Text(label, fontWeight = if (selected) androidx.compose.ui.text.font.FontWeight.Bold
        else androidx.compose.ui.text.font.FontWeight.Normal)
    }
}

/** One HUD-metric visibility row: label + Switch, dark-styled. */
@Composable
private fun HudToggleRow(label: String, state: Boolean, onToggle: (Boolean) -> Unit, textColor: Color) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(8.dp))
            .background(Color(0xFF1A1D22))
            .padding(horizontal = 10.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
    ) {
        Text(label, style = MaterialTheme.typography.bodySmall, color = textColor)
        androidx.compose.material3.Switch(
            checked = state,
            onCheckedChange = { onToggle(it) },
            modifier = Modifier.size(34.dp, 20.dp),
        )
    }
}
