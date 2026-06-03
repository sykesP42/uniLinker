package com.unilinker.android.plugins.screenmirror

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.unilinker.android.sdk.PeerConnectionState
import com.unilinker.android.sdk.models.PeerDevice
import com.unilinker.android.sdk.models.StreamStats
import org.webrtc.RendererCommon
import org.webrtc.SurfaceViewRenderer
import org.webrtc.VideoTrack

@Composable
fun ScreenMirrorTab(plugin: ScreenMirrorPlugin) {
    val devices by remember { mutableStateOf<List<PeerDevice>>(emptyList()) }
    val isScanning = remember { mutableStateOf(true) }
    val connectionState by plugin.connectionState.collectAsState()
    val videoTrack by plugin.videoTrack.collectAsState()
    val stats by plugin.stats.collectAsState()
    val isConnected = connectionState == PeerConnectionState.CONNECTED
    var connectedDeviceId by remember { mutableStateOf<String?>(null) }

    // Discovery (from platform)
    LaunchedEffect(Unit) {
        // Discovery is handled by Platform, devices come from another source
        // For now, simulate — in production, pass through plugin context
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background),
    ) {
        if (isConnected) {
            // Full-screen stream view
            StreamView(
                connectionState = connectionState,
                stats = stats,
                videoTrack = videoTrack,
                onDisconnect = {
                    plugin.disconnect()
                    connectedDeviceId = null
                },
            )
        } else {
            // Device list + share controls
            DeviceListWithControls(
                devices = devices,
                isScanning = isScanning.value,
                connectedDeviceId = connectedDeviceId,
                connectionState = connectionState,
                onConnect = { device ->
                    connectedDeviceId = device.id
                    plugin.connectTo(device)
                },
                onDisconnect = {
                    plugin.disconnect()
                    connectedDeviceId = null
                },
            )
        }
    }
}

@Composable
private fun StreamView(
    connectionState: PeerConnectionState,
    stats: StreamStats,
    videoTrack: VideoTrack?,
    onDisconnect: () -> Unit,
) {
    Box(modifier = Modifier.fillMaxSize()) {
        when (connectionState) {
            PeerConnectionState.CONNECTING -> {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        CircularProgressIndicator(
                            color = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.size(40.dp),
                        )
                        Spacer(modifier = Modifier.height(12.dp))
                        Text("连接中…", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }

            PeerConnectionState.CONNECTED -> {
                if (videoTrack != null) {
                    AndroidView(
                        modifier = Modifier.fillMaxSize(),
                        factory = { ctx ->
                            SurfaceViewRenderer(ctx).apply {
                                setEnableHardwareScaler(true)
                                setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FIT)
                            }
                        },
                        update = { renderer -> videoTrack.addSink(renderer) },
                    )
                }

                // Stats overlay
                if (stats.width > 0) {
                    Surface(
                        modifier = Modifier
                            .align(Alignment.TopEnd)
                            .padding(12.dp),
                        shape = RoundedCornerShape(8.dp),
                        color = MaterialTheme.colorScheme.surface.copy(alpha = 0.8f),
                    ) {
                        Column(modifier = Modifier.padding(8.dp)) {
                            Text(
                                "${stats.width}×${stats.height}",
                                fontSize = 11.sp,
                                color = MaterialTheme.colorScheme.onSurface,
                            )
                            Text(
                                "${stats.fps} fps",
                                fontSize = 11.sp,
                                color = MaterialTheme.colorScheme.onSurface,
                            )
                            if (stats.decodeMs > 0) {
                                Text(
                                    "${"%.1f".format(stats.decodeMs)}ms decode",
                                    fontSize = 11.sp,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        }
                    }
                }
            }

            PeerConnectionState.ERROR -> {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text("⚠️", fontSize = 48.sp)
                        Spacer(modifier = Modifier.height(8.dp))
                        Text("连接失败", color = MaterialTheme.colorScheme.error)
                    }
                }
            }

            else -> {}
        }

        // Bottom disconnect button
        Surface(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth(),
            color = MaterialTheme.colorScheme.surface,
        ) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(16.dp),
                horizontalArrangement = Arrangement.Center,
            ) {
                OutlinedButton(
                    onClick = onDisconnect,
                    colors = ButtonDefaults.outlinedButtonColors(
                        contentColor = MaterialTheme.colorScheme.error,
                    ),
                ) { Text("断开连接") }
            }
        }
    }
}

@Composable
private fun DeviceListWithControls(
    devices: List<PeerDevice>,
    isScanning: Boolean,
    connectedDeviceId: String?,
    connectionState: PeerConnectionState,
    onConnect: (PeerDevice) -> Unit,
    onDisconnect: () -> Unit,
) {
    Column(
        modifier = Modifier.fillMaxSize().padding(16.dp),
    ) {
        // Header
        Text(
            text = "🔍 局域网设备",
            fontSize = 18.sp,
            fontWeight = FontWeight.SemiBold,
            color = MaterialTheme.colorScheme.onSurface,
        )
        Spacer(modifier = Modifier.height(4.dp))
        Text(
            text = if (isScanning) "搜索中…" else "已发现 ${devices.size} 台设备",
            fontSize = 13.sp,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(modifier = Modifier.height(16.dp))

        // Device list
        if (devices.isEmpty()) {
            Box(
                modifier = Modifier.fillMaxWidth().padding(32.dp),
                contentAlignment = Alignment.Center,
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("🔍", fontSize = 48.sp)
                    Spacer(modifier = Modifier.height(12.dp))
                    Text("等待设备上线…", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        } else {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                items(devices) { device ->
                    DeviceCard(
                        device = device,
                        isConnecting = connectedDeviceId == device.id &&
                                connectionState == PeerConnectionState.CONNECTING,
                        isConnected = connectedDeviceId == device.id &&
                                connectionState == PeerConnectionState.CONNECTED,
                        onTap = { onConnect(device) },
                        onDisconnect = onDisconnect,
                    )
                }
            }
        }
    }
}

@Composable
private fun DeviceCard(
    device: PeerDevice,
    isConnecting: Boolean,
    isConnected: Boolean,
    onTap: () -> Unit,
    onDisconnect: () -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (isConnected)
                MaterialTheme.colorScheme.primaryContainer
            else
                MaterialTheme.colorScheme.surface,
        ),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("🖥️", fontSize = 28.sp)
            Spacer(modifier = Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = device.name,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.Medium,
                )
                Text(
                    text = device.ipAddress,
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            when {
                isConnecting -> {
                    CircularProgressIndicator(
                        modifier = Modifier.size(24.dp),
                        strokeWidth = 2.dp,
                    )
                }
                isConnected -> {
                    Button(
                        onClick = onDisconnect,
                        colors = ButtonDefaults.buttonColors(
                            containerColor = MaterialTheme.colorScheme.error,
                        ),
                    ) { Text("断开") }
                }
                else -> {
                    Button(onClick = onTap) { Text("连接") }
                }
            }
        }
    }
}
