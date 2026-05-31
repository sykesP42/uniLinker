package com.unilinker.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import com.unilinker.android.discovery.DeviceDiscovery
import com.unilinker.android.discovery.PeerDevice
import com.unilinker.android.ui.DeviceListScreen
import com.unilinker.android.ui.StreamScreen
import com.unilinker.android.ui.theme.UniLinkerTheme
import com.unilinker.android.webrtc.ConnectionState
import com.unilinker.android.webrtc.WebRTCClient
import kotlinx.coroutines.flow.collectLatest
import org.webrtc.ContextUtils
import org.webrtc.VideoTrack

class MainActivity : ComponentActivity() {

    private val deviceDiscovery by lazy { DeviceDiscovery(this) }
    private var webRtcClient: WebRTCClient? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ContextUtils.initialize(applicationContext)

        setContent {
            UniLinkerTheme {
                UniLinkerApp()
            }
        }
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    private fun UniLinkerApp() {
        var devices by remember { mutableStateOf<List<PeerDevice>>(emptyList()) }
        var connectedDevice by remember { mutableStateOf<PeerDevice?>(null) }
        var connectionState by remember { mutableStateOf(ConnectionState.IDLE) }
        var videoTrack by remember { mutableStateOf<VideoTrack?>(null) }
        var streamStats by remember { mutableStateOf(
            com.unilinker.android.webrtc.StreamStats()
        ) }
        var isScanning by remember { mutableStateOf(true) }

        // Start device discovery
        LaunchedEffect(Unit) {
            deviceDiscovery.discover().collectLatest { deviceList ->
                devices = deviceList
                isScanning = false
            }
        }

        // Collect WebRTC events
        LaunchedEffect(webRtcClient) {
            webRtcClient?.let { client ->
                launch {
                    client.connectionState.collect { state ->
                        connectionState = state
                        if (state == ConnectionState.DISCONNECTED ||
                            state == ConnectionState.ERROR
                        ) {
                            connectedDevice = null
                        }
                    }
                }
                launch {
                    client.stats.collect { stats ->
                        streamStats = stats
                    }
                }
                launch {
                    client.remoteVideoTrack.collectLatest { track ->
                        videoTrack = track
                    }
                }
            }
        }

        Scaffold(
            topBar = {
                TopAppBar(
                    title = {
                        Text(
                            text = if (connectedDevice != null) connectedDevice!!.name
                            else "UniLinker",
                        )
                    },
                    colors = TopAppBarDefaults.topAppBarColors(
                        containerColor = MaterialTheme.colorScheme.surface,
                        titleContentColor = MaterialTheme.colorScheme.onSurface,
                    ),
                )
            },
        ) { padding ->
            Box(modifier = Modifier.padding(padding)) {
                if (connectedDevice != null && connectionState == ConnectionState.CONNECTED) {
                    StreamScreen(
                        connectionState = connectionState,
                        stats = streamStats,
                        videoTrack = videoTrack,
                        onDisconnect = {
                            webRtcClient?.disconnect()
                            webRtcClient = null
                            connectedDevice = null
                        },
                    )
                } else {
                    DeviceListScreen(
                        devices = devices,
                        connectedDeviceId = connectedDevice?.id,
                        isScanning = isScanning,
                        onConnect = { device ->
                            connectedDevice = device
                            connectionState = ConnectionState.CONNECTING

                            val url = "http://${device.ipAddress}:${device.port}"
                            val client = WebRTCClient(url)
                            webRtcClient = client
                            client.initialize()
                            client.connect()
                        },
                        onDisconnect = {
                            webRtcClient?.disconnect()
                            webRtcClient = null
                            connectedDevice = null
                        },
                    )
                }
            }
        }
    }

    override fun onDestroy() {
        webRtcClient?.dispose()
        super.onDestroy()
    }
}
