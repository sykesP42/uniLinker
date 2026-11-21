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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.zIndex
import com.unilinker.android.mediatier.CaptureTier
import com.unilinker.android.mediatier.DirectRtpClient
import com.unilinker.android.mediatier.H264SurfaceDecoder
import com.unilinker.android.ui.FloatingBall
import com.unilinker.android.ui.HudDashboard
import com.unilinker.android.ui.RollingSamples
import com.unilinker.android.ui.FloatingPanel
import com.unilinker.android.ui.PanelState
import com.unilinker.android.ui.friendlyError
import com.unilinker.android.ui.theme.StatusError
import com.unilinker.android.ui.theme.StatusSuccess
import com.unilinker.android.ui.theme.StatusWarning
import com.unilinker.android.ui.theme.TextSecondary
import com.unilinker.android.ui.theme.TextTertiary
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
    // Address lives at activity scope so onNewIntent can update it — the
    // single-instance launch delivers re-`am start` here, not onCreate.
    private val addressState = mutableStateOf("")
    private val autoConnectTick = mutableIntStateOf(0)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        val prefs = getPreferences(android.content.Context.MODE_PRIVATE)
        val deepLink = intent?.getStringExtra("connect")
        addressState.value = deepLink ?: prefs.getString("last_address", "") ?: ""
        if (!deepLink.isNullOrBlank()) autoConnectTick.intValue = 1

        setContent {
            UniLinkerTheme {
                DirectRtpScreen(
                    addressState,
                    autoConnectTick.intValue,
                    intent.getIntExtra("chaos_loss", 0),
                    intent.getIntExtra("chaos_dup", 0),
                    intent.getIntExtra("chaos_lag", 0),
                    intent.getIntExtra("fps", 0),
                    intent.getIntExtra("br", 0),
                )
            }
        }
    }

    override fun onNewIntent(intent: android.content.Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        val addr = intent.getStringExtra("connect")
        if (!addr.isNullOrBlank()) {
            addressState.value = addr
            autoConnectTick.intValue++ // re-fire the deep-link auto connect
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
    address: androidx.compose.runtime.MutableState<String>,
    autoConnectTick: Int,
    chaosLoss: Int = 0,
    chaosDup: Int = 0,
    chaosLag: Int = 0,
    // NOTE: the call site passes the chaos extras positionally (a swap here
    // silently fed `--ei fps 60` into the chaos-lag injection and froze the
    // pipeline, 2026-11-20) — keep the positional order in sync with onCreate.
    fpsOverride: Int = 0,
    brOverride: Int = 0,
) {
    val context = LocalContext.current
    var client by remember { mutableStateOf<DirectRtpClient?>(null) }
    var decoder by remember { mutableStateOf<H264SurfaceDecoder?>(null) }
    var surfaceReady by remember { mutableStateOf(false) }
    var panelState by remember { mutableStateOf(PanelState.Idle) }
    var statusDetail by remember { mutableStateOf("") }
    var transientMsg by remember { mutableStateOf<String?>(null) }
    var decodedCount by remember { mutableIntStateOf(0) }
    val scope = rememberCoroutineScope()
    var frameJob by remember { mutableStateOf<Job?>(null) }

    val prefs = remember { (context as android.app.Activity).getPreferences(android.content.Context.MODE_PRIVATE) }
    var resIdx by remember { mutableIntStateOf(prefs.getInt("tier_res", 0)) }
    // Deep-link `--ei br 32000` overrides the persisted tier bitrate the same
    // way fpsOverride does (60fps tile pairs with the 32M budget; 8M carries
    // only ~5fps of heavy content on the paced wire).
    var brIdx by remember {
        mutableIntStateOf(
            if (brOverride in brOptions) brOptions.indexOf(brOverride)
            else prefs.getInt("tier_br", 1)
        )
    }
    // Deep-link `--ei fps 60` overrides the persisted tier fps for one launch
    // (tier verification without tapping the panel; 0 = use the persisted one).
    var fpsIdx by remember {
        mutableIntStateOf(
            if (fpsOverride in fpsOptions) fpsOptions.indexOf(fpsOverride)
            else prefs.getInt("tier_fps", 0)
        )
    }
    var ballExpanded by remember { mutableStateOf(false) }

    var hudFps by remember { mutableStateOf(0.0) }
    var hudMbps by remember { mutableStateOf(0.0) }
    var hudJitter by remember { mutableStateOf(0.0) }
    var liveLossPct by remember { mutableStateOf(0.0) }

    // HUD dashboard: per-metric switches (persisted) + draggable position.
    var hudFpsOn by remember { mutableStateOf(prefs.getBoolean("hud_fps", true)) }
    var hudMbpsOn by remember { mutableStateOf(prefs.getBoolean("hud_mbps", true)) }
    var hudJitterOn by remember { mutableStateOf(prefs.getBoolean("hud_jitter", true)) }
    var hudLossOn by remember { mutableStateOf(prefs.getBoolean("hud_loss", true)) }
    var hudResOn by remember { mutableStateOf(prefs.getBoolean("hud_res", true)) }
    var hudEnabled by remember { mutableStateOf(prefs.getBoolean("hud_enabled", true)) }
    var sampleTick by remember { mutableIntStateOf(0) }
    val panelScroll = androidx.compose.foundation.rememberScrollState()
    var hudChartOn by remember { mutableStateOf(prefs.getBoolean("hud_chart", true)) }
    var hudMediumOn by remember { mutableStateOf(prefs.getBoolean("hud_medium", false)) }
    val samples = remember { RollingSamples(60) }
    var transportMode by remember { mutableStateOf("sipsorcery") }
    var chaosLossState by remember { mutableIntStateOf(chaosLoss) }
    var chaosDupState by remember { mutableIntStateOf(chaosDup) }
    var chaosLagState by remember { mutableIntStateOf(chaosLag) }
    var tileRenderer by remember { mutableStateOf<com.unilinker.android.mediatier.TileRenderer?>(null) }
    var wifiLock by remember { mutableStateOf<android.net.wifi.WifiManager.WifiLock?>(null) }
    var pendingSurface by remember { mutableStateOf<android.graphics.SurfaceTexture?>(null) }
    var textureViewRef by remember { mutableStateOf<android.view.TextureView?>(null) }
    var hudOffsetX by remember { mutableIntStateOf(prefs.getInt("hud_x", 0)) }
    var hudOffsetY by remember { mutableIntStateOf(prefs.getInt("hud_y", 0)) }
    // Pending pairing ask: (sasCode, fingerprint) or null; the deferred is
    // completed by the Compose dialog's buttons.
    var pairingAsk by remember { mutableStateOf<Pair<String, String>?>(null) }
    var pairingDeferred by remember { mutableStateOf<kotlinx.coroutines.CompletableDeferred<Boolean>?>(null) }
    var ballOffsetX by remember { mutableIntStateOf(prefs.getInt("ball_x", 0)) }
    var ballOffsetY by remember { mutableIntStateOf(prefs.getInt("ball_y", 0)) }
    fun persistBall() {
        prefs.edit().putInt("ball_x", ballOffsetX).putInt("ball_y", ballOffsetY).apply()
    }
    // Transient status note (transport switch, chaos tweaks): amber for 3s.
    LaunchedEffect(transientMsg) {
        if (transientMsg != null) {
            kotlinx.coroutines.delay(3000)
            transientMsg = null
        }
    }

    fun persistHud() {
        prefs.edit()
            .putBoolean("hud_fps", hudFpsOn)
            .putBoolean("hud_mbps", hudMbpsOn)
            .putBoolean("hud_jitter", hudJitterOn)
            .putBoolean("hud_loss", hudLossOn)
            .putBoolean("hud_res", hudResOn)
            .putBoolean("hud_enabled", hudEnabled)
            .putBoolean("hud_chart", hudChartOn)
            .putBoolean("hud_medium", hudMediumOn)
            .putInt("hud_x", hudOffsetX)
            .putInt("hud_y", hudOffsetY)
            .apply()
    }
    // HUD metric rows: (label, value, color). Value colors encode health:
    // success = good, warning = degraded, error = bad. Fixed-width numeric
    // fields plus tabular numbers keep the panel from wobbling on refresh.
    fun hudRows(): List<Triple<String, String, Color>> {
        val rows = mutableListOf<Triple<String, String, Color>>()
        if (hudFpsOn) {
            val v = hudFps
            val c = when { v >= 25 -> StatusSuccess; v >= 15 -> StatusWarning; else -> StatusError }
            rows.add(Triple("帧率", "%2.0f fps".format(v), c))
        }
        if (hudMbpsOn) {
            val v = hudMbps
            val c = when { v >= 6 -> StatusSuccess; v >= 3 -> StatusWarning; else -> StatusError }
            rows.add(Triple("码率", "%4.1f Mbps".format(v), c))
        }
        if (hudJitterOn) {
            val v = hudJitter
            val c = when { v < 30 -> StatusSuccess; v < 60 -> StatusWarning; else -> StatusError }
            rows.add(Triple("抖动", "%3.0f ms".format(v), c))
        }
        if (hudLossOn) {
            val (lost, total) = client?.receiverLossWindow ?: Pair(0, 0)
            val pct = if (total > 0) lost * 100.0 / total else 0.0
            val c = when { pct < 1 -> StatusSuccess; pct < 5 -> StatusWarning; else -> StatusError }
            rows.add(Triple("丢包", "%4.1f%%".format(pct), c))
        }
        if (hudResOn) {
            val r = decoder?.streamSize?.value
            rows.add(Triple("分辨率", r?.let { "${it.first}×${it.second}" } ?: "—", TextSecondary))
        }
        return rows
    }

    fun toggleConnect() {
        if (client != null) {
            client?.stop()
            client = null
            panelState = PanelState.Idle
            return
        }
        val addr = address.value.trim()
        val parsed = addr.split(":")
        if (parsed.size < 2) {
            panelState = PanelState.Error
            statusDetail = "地址格式应为 IP:端口（如 192.168.42.39:9527）"
            return
        }
        prefs.edit()
            .putInt("tier_res", resIdx)
            .putInt("tier_br", brIdx)
            .putInt("tier_fps", fpsIdx)
            .putString("last_address", addr) // remember for input-free relaunch
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
        panelState = PanelState.Connecting
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
                    panelState = PanelState.Error
                    statusDetail = newClient.error.value ?: ""
                    client = null
                    return@launch
                }
                val (sasCode, fingerprint) = probe
                pairingDeferred = kotlinx.coroutines.CompletableDeferred<Boolean>()
                pairingAsk = Pair(sasCode, fingerprint)
                val confirmed = pairingDeferred!!.await()
                pairingAsk = null
                newClient.pinFingerprint(fingerprint)
                prefs.edit().putString("pinned_fp", fingerprint).apply()
            }
            val ok = newClient.connect(tier)
            if (!ok) {
                panelState = PanelState.Error
                statusDetail = newClient.error.value ?: ""
                client = null
                return@launch
            }
            panelState = PanelState.Streaming
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
            // Cross-mode teardown: the SurfaceTexture buffer queue (~3 slots)
            // supports ONE producer. A live RTX→tile switch left the MediaCodec
            // decoder holding a producer slot while the tile canvas path took
            // another — the consumer starved and hwui aborted the process
            // (EGL_BAD_ALLOC in setSurface, tombstone 2026-11-21). Tear down
            // the other mode's producer before starting the new pipeline.
            if (newClient.transportIsTile) {
                decoder?.close()
                decoder = null
            } else {
                tileRenderer?.close()
                tileRenderer = null
            }
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
                            renderer.renderTile(
                                tf.tileIdx, tf.tileCount, tf.isLastOfFrame, tf.gridCols, tf.frameW, tf.frameH, tf.jpeg)
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

    // Deep-link auto-connect: fires on the onCreate deep link and again on
    // every onNewIntent re-`am start` (the single-instance resume quirk).
    LaunchedEffect(autoConnectTick) {
        if (autoConnectTick > 0) toggleConnect()
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
                // Media-stall auto-recovery: a WiFi blip (hotspot roam, user
                // toggling, power-save) stops media AND keepalives at once —
                // the sender's watchdog correctly releases the session, but
                // this app used to sit frozen in STREAMING forever. 5s of
                // zero bytes on the wire → reconnect (≥15s between attempts).
                var stallPolls = 0
                var lastAutoReconnectMs = 0L
                while (true) {
                    kotlinx.coroutines.delay(500)
                    sampleTick++
                    val now = System.nanoTime()
                    val dtSec = (now - lastTime) / 1e9
                    val c = client
                    // Mode-aware: the tile renderer exists in BOTH modes
                    // (shared TextureView) — a blind Elvis chain would pin
                    // SIPSorcery sessions at 0 rendered frames.
                    val renderedNow = if (c?.transportIsTile == true)
                        (tileRenderer?.presentedFrames ?: 0)
                    else
                        (decoder?.decodedFrames ?: 0)
                    if (c == null || c.state.value != com.unilinker.android.mediatier.DirectRtpClient.State.STREAMING) {
                        hudFps = 0.0; hudMbps = 0.0
                        lastFrames = renderedNow
                        lastBytes = 0
                        lastTime = now
                        // A stall-triggered reconnect FAILED (sender down at the
                        // attempt): the client is in ERROR and the STREAMING
                        // stall counter below never runs again — retry here on
                        // the same 15s schedule until the sender returns.
                        if (c?.state?.value == com.unilinker.android.mediatier.DirectRtpClient.State.ERROR &&
                            now - lastAutoReconnectMs > 15_000_000_000) {
                            lastAutoReconnectMs = now
                            android.util.Log.w("DirectRtp", "reconnect failed earlier — retrying (${c.error?.value})")
                            client?.stop()
                            client = null
                            toggleConnect()
                        }
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
                    val lw = c.receiverLossWindow ?: Pair(0, 0)
                    liveLossPct = if (lw.second > 0) lw.first * 100.0 / lw.second else 0.0
                    val (lostN, totalN) = c.receiverLossWindow ?: Pair(0, 0)
                    samples.push(
                        hudFps.toFloat(),
                        hudMbps.toFloat(),
                        (if (totalN > 0) lostN * 100f / totalN else 0f),
                    )
                    android.util.Log.d("DirectRtp",
                        "hud: fps=${"%.1f".format(hudFps)} dFrames=$dFrames decoded=${decoder?.decodedFrames} decNull=${decoder == null} sReady=$surfaceReady subCount=$decodedCount state=${c.state.value} pkts=${st.first} assembled=${c.receiverAssembledAUs} corruptDropped=${c.receiverDroppedCorruptAUs} pendingNals=${c.receiverNalsBuffered}")

                    if (dBytes > 0) {
                        stallPolls = 0
                    } else if (++stallPolls >= 10 && now - lastAutoReconnectMs > 15_000_000_000) {
                        stallPolls = 0
                        lastAutoReconnectMs = now
                        android.util.Log.w("DirectRtp", "media stalled (0 bytes) for 5s — auto reconnect")
                        lastFrames = 0; lastBytes = 0
                        client?.stop()
                        client = null
                        toggleConnect()
                    }
                }
            }
        }

        // HUD dashboard — default TOP-END, draggable, per-metric switches.
        // Hidden while the panel is open (both anchor TopEnd; the header
        // stats strip keeps the numbers visible while adjusting). Master
        // switch off, or every row+chart off, renders nothing.
        val hudHasContent = hudRows().isNotEmpty() || hudChartOn || hudMediumOn
        if (!ballExpanded && hudEnabled && hudHasContent) HudDashboard(
            modifier = Modifier.align(Alignment.TopEnd),
            rows = hudRows(),
            samples = samples,
            chartOn = hudChartOn,
            mediumOn = hudMediumOn,
            medium = client?.medium ?: "",
            offsetX = hudOffsetX,
            offsetY = hudOffsetY,
            sampleTick = sampleTick,
            onDrag = { dx, dy -> hudOffsetX += dx.toInt(); hudOffsetY += dy.toInt() },
            onDragEnd = { persistHud() },
        )

        if (client == null) {
            // Idle card: status + address input + connect — the no-adb path.
            // The address persists (last_address), so relaunches prefill it.
            Column(
                modifier = Modifier
                    .align(Alignment.Center)
                    .width(360.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(com.unilinker.android.ui.theme.BackgroundSurface.copy(alpha = 0.92f))
                    .border(1.dp, com.unilinker.android.ui.theme.BorderStrong, RoundedCornerShape(12.dp))
                    .padding(20.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                val centerColor = when (panelState) {
                    PanelState.Error -> StatusError
                    PanelState.Connecting -> StatusWarning
                    else -> TextSecondary
                }
                Text(
                    text = if (panelState == PanelState.Error)
                        "连接失败：${friendlyError(statusDetail)}" else "未连接",
                    style = MaterialTheme.typography.bodyMedium,
                    color = centerColor,
                )
                androidx.compose.material3.OutlinedTextField(
                    value = address.value,
                    onValueChange = { address.value = it },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    label = { Text("分享端地址（IP:端口）") },
                    placeholder = { Text("192.168.42.39:9527") },
                    shape = RoundedCornerShape(8.dp),
                    colors = androidx.compose.material3.OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = com.unilinker.android.ui.theme.Copper,
                        cursorColor = com.unilinker.android.ui.theme.Copper,
                        focusedLabelColor = com.unilinker.android.ui.theme.Copper,
                    ),
                )
                Button(
                    onClick = { toggleConnect() },
                    modifier = Modifier.fillMaxWidth().height(44.dp),
                    enabled = panelState != PanelState.Connecting,
                    shape = RoundedCornerShape(8.dp),
                    colors = androidx.compose.material3.ButtonDefaults.buttonColors(
                        containerColor = com.unilinker.android.ui.theme.Copper,
                        contentColor = com.unilinker.android.ui.theme.OnCopper,
                    ),
                ) {
                    Text(
                        if (panelState == PanelState.Connecting) "连接中…" else "连接投屏",
                        fontWeight = FontWeight.SemiBold,
                    )
                }
            }
        }

        // Pairing confirmation (Compose, not the platform AlertDialog —
        // visual consistency with the copper screen).
        pairingAsk?.let { (sasCode, fingerprint) ->
            androidx.compose.material3.AlertDialog(
                onDismissRequest = { pairingDeferred?.complete(false) },
                title = { Text("配对确认") },
                text = {
                    Text(
                        "请在分享端核对配对码：\n\n$sasCode\n\n设备指纹：\n\n$fingerprint\n\n两端一致后确认建立信任。",
                        color = TextSecondary,
                    )
                },
                confirmButton = {
                    Button(onClick = { pairingDeferred?.complete(true) }) {
                        Text("确认一致")
                    }
                },
                dismissButton = {
                    Button(
                        onClick = { pairingDeferred?.complete(false) },
                        colors = androidx.compose.material3.ButtonDefaults.buttonColors(
                            containerColor = com.unilinker.android.ui.ChipBg, contentColor = TextSecondary),
                    ) { Text("不一致") }
                },
            )
        }

        // Tier changes (quality presets / advanced cycles) apply on the NEXT
        // session — the sender builds capture+encoder from the tier at
        // session start. Auto-reconnect with a debounce so rapid cycling
        // fires exactly one reconnect (transport switching uses the same
        // pattern; without this the panel read as "nothing changes").
        var tierJob by remember { mutableStateOf<Job?>(null) }
        fun applyTierChange(note: String) {
            tierJob?.cancel()
            if (client == null) return // idle: tier applies at next connect
            tierJob = scope.launch {
                kotlinx.coroutines.delay(600)
                transientMsg = note
                client?.stop()
                client = null
                toggleConnect()
            }
        }

        // Floating ball: drag anywhere, auto-dims after 3 s, tap toggles panel.
        FloatingBall(
            expanded = ballExpanded,
            streaming = client != null,
            initialOffsetX = ballOffsetX,
            initialOffsetY = ballOffsetY,
            onPosition = { x, y -> ballOffsetX = x; ballOffsetY = y; persistBall() },
            onToggle = { ballExpanded = !ballExpanded },
        )

        if (ballExpanded) {
            FloatingPanel(
                state = panelState,
                statusDetail = statusDetail,
                transient = transientMsg,
                scrollState = panelScroll,
                sampleTick = sampleTick,
                liveFps = hudFps,
                liveMbps = hudMbps,
                liveLossPct = liveLossPct,
                hudEnabled = hudEnabled,
                onHudEnabled = { hudEnabled = it; persistHud() },
                onResetPositions = {
                    hudOffsetX = 0; hudOffsetY = 0
                    ballOffsetX = 0; ballOffsetY = 0
                    persistHud(); persistBall()
                },
                resIdx = resIdx,
                brIdx = brIdx,
                fpsIdx = fpsIdx,
                resLabel = "${resOptions[resIdx].first}×${resOptions[resIdx].second}",
                brLabel = "${brOptions[brIdx] / 1000} Mbps",
                fpsLabel = "${fpsOptions[fpsIdx]} fps",
                streaming = client != null,
                transportMode = transportMode,
                diagProvider = {
                    val c = client
                    val st = c?.receiverStats
                    if (c == null || st == null) null
                    else com.unilinker.android.ui.DiagStats(
                        medium = c.medium,
                        received = st.first,
                        lost = st.second,
                        tileAssembled = c.tileAssembled,
                        tileDropped = c.tileDropped,
                        tileOrphans = c.tileOrphans,
                        authFailures = c.receiverAuthFailures,
                    )
                },
                onRefresh = {
                    scope.launch { client?.requestKeyFrame() }
                },
                samples = samples,
                chaosLoss = chaosLossState,
                chaosDup = chaosDupState,
                chaosLagMs = chaosLagState,
                onChaos = { l, d, lag ->
                    chaosLossState = l; chaosDupState = d; chaosLagState = lag
                    client?.setChaos(l, d, lag)
                },
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
                onHudChart = { hudChartOn = it; persistHud() },
                onHudMedium = { hudMediumOn = it; persistHud() },
                onCycleRes = {
                    resIdx = (resIdx + 1) % resOptions.size
                    prefs.edit().putInt("tier_res", resIdx).apply()
                    applyTierChange("档位已更新——自动重连生效")
                },
                onCycleBr = {
                    brIdx = (brIdx + 1) % brOptions.size
                    prefs.edit().putInt("tier_br", brIdx).apply()
                    applyTierChange("码率已更新——自动重连生效")
                },
                onCycleFps = {
                    fpsIdx = (fpsIdx + 1) % fpsOptions.size
                    prefs.edit().putInt("tier_fps", fpsIdx).apply()
                    applyTierChange("帧率已更新——自动重连生效")
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
                    panelState = PanelState.Idle
                },
                onPreset = { r, b, f ->
                    resIdx = r; brIdx = b; fpsIdx = f
                    prefs.edit()
                        .putInt("tier_res", r).putInt("tier_br", b).putInt("tier_fps", f)
                        .apply()
                    applyTierChange("画质已切换——自动重连生效")
                },
                onTransport = { mode ->
                    transportMode = mode
                    scope.launch {
                        val ok = client?.setTransport(mode) ?: false
                        transientMsg = if (ok) "传输方式 → $mode（重连生效）" else "传输方式设置失败"
                        if (ok) panelState = PanelState.Connecting
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
