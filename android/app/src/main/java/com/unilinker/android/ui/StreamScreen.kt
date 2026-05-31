package com.unilinker.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
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
import com.unilinker.android.webrtc.ConnectionState
import com.unilinker.android.webrtc.StreamStats
import org.webrtc.RendererCommon
import org.webrtc.SurfaceViewRenderer
import org.webrtc.VideoTrack

@Composable
fun StreamScreen(
    connectionState: ConnectionState,
    stats: StreamStats,
    videoTrack: VideoTrack?,
    onDisconnect: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background),
    ) {
        // Video area
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f)
                .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.5f)),
            contentAlignment = Alignment.Center,
        ) {
            when (connectionState) {
                ConnectionState.IDLE, ConnectionState.DISCOVERING -> {
                    Text(
                        text = "📺",
                        fontSize = 64.sp,
                    )
                }

                ConnectionState.CONNECTING -> {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        CircularProgressIndicator(
                            color = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.size(40.dp),
                        )
                        Spacer(modifier = Modifier.height(12.dp))
                        Text(
                            text = "连接中…",
                            fontSize = 14.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }

                ConnectionState.CONNECTED -> {
                    if (videoTrack != null) {
                        VideoRenderer(videoTrack = videoTrack)
                    }
                }

                ConnectionState.DISCONNECTED -> {
                    Text(
                        text = "已断开",
                        fontSize = 14.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }

                ConnectionState.ERROR -> {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(text = "⚠️", fontSize = 48.sp)
                        Spacer(modifier = Modifier.height(8.dp))
                        Text(
                            text = "连接失败",
                            fontSize = 14.sp,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                }
            }

            // Stats overlay
            if (connectionState == ConnectionState.CONNECTED && stats.width > 0) {
                Surface(
                    modifier = Modifier
                        .align(Alignment.TopEnd)
                        .padding(12.dp),
                    shape = RoundedCornerShape(8.dp),
                    color = MaterialTheme.colorScheme.surface.copy(alpha = 0.8f),
                ) {
                    Column(modifier = Modifier.padding(8.dp)) {
                        Text(
                            text = "${stats.width}×${stats.height}",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurface,
                        )
                        Text(
                            text = "${stats.fps} fps",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurface,
                        )
                        if (stats.decodeMs > 0) {
                            Text(
                                text = "${"%.1f".format(stats.decodeMs)}ms decode",
                                fontSize = 11.sp,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
            }
        }

        // Bottom controls
        Surface(
            modifier = Modifier.fillMaxWidth(),
            color = MaterialTheme.colorScheme.surface,
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(16.dp),
                horizontalArrangement = Arrangement.Center,
            ) {
                OutlinedButton(
                    onClick = onDisconnect,
                    colors = ButtonDefaults.outlinedButtonColors(
                        contentColor = MaterialTheme.colorScheme.error,
                    ),
                    shape = RoundedCornerShape(8.dp),
                ) {
                    Text("断开连接")
                }
            }
        }
    }
}

@Composable
private fun VideoRenderer(videoTrack: VideoTrack) {
    val context = androidx.compose.ui.platform.LocalContext.current

    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx ->
            SurfaceViewRenderer(ctx).apply {
                setEnableHardwareScaler(true)
                setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FIT)
            }
        },
        update = { renderer ->
            videoTrack.addSink(renderer)
        },
    )
}
