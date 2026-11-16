package com.unilinker.android

import android.os.Bundle
import android.view.SurfaceHolder
import android.view.SurfaceView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
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
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
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
        val newClient = DirectRtpClient(parsed[0], parsed[1].toIntOrNull() ?: 9527)
        client = newClient
        statusText = "Connecting…"
        scope.launch {
            val ok = newClient.connect()
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
                        statusText = "Streaming · ${decodedCount} frames decoded"
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
        AndroidView(
            modifier = Modifier.fillMaxWidth().weight(1f),
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

        Text(
            text = statusText,
            style = MaterialTheme.typography.bodyMedium,
            color = if (statusText.startsWith("Error")) Color.Red else Color.Unspecified,
        )

        OutlinedTextField(
            value = address,
            onValueChange = { address = it },
            label = { Text("Host IP:signaling-port (e.g. 192.168.42.39:9527)") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
        )

        Button(
            onClick = { toggleConnect() },
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(if (client == null) "Connect (direct RTP)" else "Disconnect")
        }
    }
}
