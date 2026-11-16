package com.unilinker.android

import android.os.Bundle
import android.view.SurfaceHolder
import android.view.SurfaceView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import com.unilinker.android.mediatier.CaptureTier
import com.unilinker.android.mediatier.DirectRtpClient
import com.unilinker.android.mediatier.H264SurfaceDecoder
import com.unilinker.android.ui.theme.UniLinkerTheme
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

/**
 * Direct-RTP receiver screen (M3, docs/spec/M3-direct-rtp.md): independent of
 * the WebRTC receive path so the two transports cannot interfere. Launch via
 * the app or `adb shell am start -n com.unilinker.android/.DirectRtpActivity
 * --es connect "192.168.42.39:9527"`.
 */
class DirectRtpActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val initialAddress = intent?.getStringExtra("connect")

        setContent {
            UniLinkerTheme {
                DirectRtpScreen(initialAddress)
            }
        }
    }
}

@Composable
fun DirectRtpScreen(initialAddress: String?) {
    var address by remember { mutableStateOf(initialAddress ?: "") }
    var client by remember { mutableStateOf<DirectRtpClient?>(null) }
    var decoder by remember { mutableStateOf<H264SurfaceDecoder?>(null) }
    var surfaceReady by remember { mutableStateOf(false) }
    var statusText by remember { mutableStateOf("Idle") }
    var decodedCount by remember { mutableStateOf(0) }
    val scope = rememberCoroutineScope()
    var frameJob by remember { mutableStateOf<Job?>(null) }

    // Capture tier (receiver-driven): persisted locally, sent in the rtp
    // request, honored by the sender.
    val context = androidx.compose.ui.platform.LocalContext.current
    val prefs = remember { (context as android.app.Activity)
        .getPreferences(android.content.Context.MODE_PRIVATE) }
    val resOptions = listOf(1280 to 800, 1920 to 1200, 2560 to 1600)
    val brOptions = listOf(4000, 8000, 16000)
    val fpsOptions = listOf(30, 60)
    var resIdx by remember { mutableIntStateOf(prefs.getInt("tier_res", 0)) }
    var brIdx by remember { mutableIntStateOf(prefs.getInt("tier_br", 1)) }
    var fpsIdx by remember { mutableIntStateOf(prefs.getInt("tier_fps", 0)) }
    var hudText by remember { mutableStateOf("") }

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
        val newClient = DirectRtpClient(parsed[0], parsed[1].toIntOrNull() ?: 9527)
        client = newClient
        statusText = "Connecting…"
        scope.launch {
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
        if (!initialAddress.isNullOrBlank()) {
            toggleConnect()
        }
    }

    Column(
        modifier = Modifier.fillMaxSize().padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        // Aspect-fit the video surface to the ACTUAL stream size reported by
        // the decoder (INFO_OUTPUT_FORMAT_CHANGED) — no stretching, no crops.
        val streamSize by decoder?.streamSize?.collectAsState()
            ?: remember { mutableStateOf<Pair<Int, Int>?>(null) }
        Box(
            modifier = Modifier.fillMaxWidth().weight(1f),
            contentAlignment = androidx.compose.ui.Alignment.Center,
        ) {
        AndroidView(
            modifier = Modifier
                .fillMaxWidth()
                .then(
                    streamSize?.let { (w, h) ->
                        Modifier.aspectRatio(w.toFloat() / h.toFloat())
                    } ?: Modifier.fillMaxSize()
                ),
            factory = { ctx ->
                SurfaceView(ctx).apply {
                    // Compose window backgrounds sit ABOVE the default SurfaceView
                    // layer — without this the video surface renders behind the
                    // Material background (uniform gray) even while decoding.
                    setZOrderOnTop(true)
                    holder.setFormat(android.graphics.PixelFormat.OPAQUE)
                    holder.addCallback(object : SurfaceHolder.Callback {
                        override fun surfaceCreated(holder: SurfaceHolder) {
                            decoder?.close()
                            decoder = H264SurfaceDecoder(holder.surface)
                            surfaceReady = true
                        }

                        override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {}

                        override fun surfaceDestroyed(holder: SurfaceHolder) {
                            surfaceReady = false
                            decoder?.close()
                            decoder = null
                        }
                    })
                }
            },
        )
        }

        Text(
            text = statusText,
            style = MaterialTheme.typography.bodyMedium,
            color = if (statusText.startsWith("Error")) Color.Red else Color.Unspecified,
        )

        // Live HUD: decoded fps, wire bitrate, packet loss, stream resolution.
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
                    val fps = dFrames / dtSec
                    val mbps = dBytes * 8 / dtSec / 1e6
                    val total = st.first + st.second
                    val lossPct = if (total > 0) st.second * 100.0 / total else 0.0
                    hudText = String.format(
                        "%.0f fps · %.1f Mbps · loss %.2f%% (%d/%d) · %d frames",
                        fps, mbps, lossPct, st.second, total, decodedCount,
                    )
                }
            }
            Text(
                text = hudText,
                style = MaterialTheme.typography.bodySmall,
                color = Color(0xFF8BC34A),
            )
        }

        Text(
            text = statusText,
            style = MaterialTheme.typography.bodyMedium,
            color = if (statusText.startsWith("Error")) Color.Red else Color.Unspecified,
        )

        // Hide the address field while streaming: removing the focused field
        // collapses the soft keyboard and frees the screen for video.
        if (client == null) {
            OutlinedTextField(
                value = address,
                onValueChange = { address = it },
                label = { Text("Host IP:signaling-port (e.g. 192.168.42.39:9527)") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
        }

        // Capture tier selector: tap a button to cycle through the tiers.
        // Persisted locally and sent with the rtp request (receiver-driven).
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Button(onClick = {
                resIdx = (resIdx + 1) % resOptions.size
                prefs.edit().putInt("tier_res", resIdx).apply()
            }, modifier = Modifier.weight(1f)) {
                Text("${resOptions[resIdx].first}×${resOptions[resIdx].second}")
            }
            Button(onClick = {
                brIdx = (brIdx + 1) % brOptions.size
                prefs.edit().putInt("tier_br", brIdx).apply()
            }, modifier = Modifier.weight(1f)) {
                Text("${brOptions[brIdx] / 1000} Mbps")
            }
            Button(onClick = {
                fpsIdx = (fpsIdx + 1) % fpsOptions.size
                prefs.edit().putInt("tier_fps", fpsIdx).apply()
            }, modifier = Modifier.weight(1f)) {
                Text("${fpsOptions[fpsIdx]} fps")
            }
        }

        Button(
            onClick = { toggleConnect() },
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(if (client == null) "Connect (direct RTP)" else "Disconnect")
        }
    }
}
