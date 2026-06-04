package com.unilinker.android.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import com.unilinker.android.sdk.PeerConnectionState
import com.unilinker.android.sdk.models.PeerDevice
import com.unilinker.android.ui.theme.TextPrimary
import com.unilinker.android.ui.theme.UniLinkerTheme

/**
 * UI Previews for Industrial Design
 *
 * Run these previews in Android Studio to see the new design.
 */

// ═══════════════════════════════════════════════════════════════
// PREVIEW DATA
// ═══════════════════════════════════════════════════════════════

val previewDevices = listOf(
    PeerDevice(
        id = "1",
        name = "Desktop-PC",
        ipAddress = "192.168.1.100:9527",
        port = 9527
    ),
    PeerDevice(
        id = "2",
        name = "MacBook-Pro",
        ipAddress = "192.168.1.101:9527",
        port = 9527
    ),
    PeerDevice(
        id = "3",
        name = "Gaming-Rig",
        ipAddress = "192.168.1.102:9527",
        port = 9527
    )
)

// ═══════════════════════════════════════════════════════════════
// MAIN SCREEN PREVIEWS
// ═══════════════════════════════════════════════════════════════

@Preview(
    name = "Main Screen - Standby (No Devices)",
    showBackground = true,
    backgroundColor = 0xFF121210
)
@Composable
fun PreviewMainScreen_Standby() {
    UniLinkerTheme {
        ModernMainScreen(
            connectionState = PeerConnectionState.DISCONNECTED,
            isScanning = false,
            isConnecting = false,
            devices = emptyList(),
            selectedStrategy = "lan-mdns",
            errorMessage = null,
            onStrategyChange = {},
            onConnect = {},
            onManualConnect = {},
            onDisconnect = {},
            onRefresh = {},
            onClearError = {}
        ) {
            // Empty content for preview
        }
    }
}

@Preview(
    name = "Main Screen - Scanning",
    showBackground = true,
    backgroundColor = 0xFF121210
)
@Composable
fun PreviewMainScreen_Scanning() {
    UniLinkerTheme {
        ModernMainScreen(
            connectionState = PeerConnectionState.DISCONNECTED,
            isScanning = true,
            isConnecting = false,
            devices = emptyList(),
            selectedStrategy = "lan-mdns",
            errorMessage = null,
            onStrategyChange = {},
            onConnect = {},
            onManualConnect = {},
            onDisconnect = {},
            onRefresh = {},
            onClearError = {}
        ) {
        }
    }
}

@Preview(
    name = "Main Screen - With Devices",
    showBackground = true,
    backgroundColor = 0xFF121210
)
@Composable
fun PreviewMainScreen_WithDevices() {
    UniLinkerTheme {
        ModernMainScreen(
            connectionState = PeerConnectionState.DISCONNECTED,
            isScanning = false,
            isConnecting = false,
            devices = previewDevices,
            selectedStrategy = "lan-mdns",
            errorMessage = null,
            onStrategyChange = {},
            onConnect = {},
            onManualConnect = {},
            onDisconnect = {},
            onRefresh = {},
            onClearError = {}
        ) {
        }
    }
}

@Preview(
    name = "Main Screen - Connecting",
    showBackground = true,
    backgroundColor = 0xFF121210
)
@Composable
fun PreviewMainScreen_Connecting() {
    UniLinkerTheme {
        ModernMainScreen(
            connectionState = PeerConnectionState.CONNECTING,
            isScanning = false,
            isConnecting = true,
            devices = previewDevices,
            selectedStrategy = "lan-mdns",
            errorMessage = null,
            onStrategyChange = {},
            onConnect = {},
            onManualConnect = {},
            onDisconnect = {},
            onRefresh = {},
            onClearError = {}
        ) {
        }
    }
}

@Preview(
    name = "Main Screen - Connected",
    showBackground = true,
    backgroundColor = 0xFF121210
)
@Composable
fun PreviewMainScreen_Connected() {
    UniLinkerTheme {
        ModernMainScreen(
            connectionState = PeerConnectionState.CONNECTED,
            isScanning = false,
            isConnecting = false,
            devices = emptyList(),
            selectedStrategy = "lan-mdns",
            errorMessage = null,
            onStrategyChange = {},
            onConnect = {},
            onManualConnect = {},
            onDisconnect = {},
            onRefresh = {},
            onClearError = {}
        ) {
            // Connected content placeholder
            Box(
                modifier = Modifier.fillMaxSize(),
                contentAlignment = Alignment.Center
            ) {
                androidx.compose.material3.Text("Video Stream", color = TextPrimary)
            }
        }
    }
}

@Preview(
    name = "Main Screen - Error",
    showBackground = true,
    backgroundColor = 0xFF121210
)
@Composable
fun PreviewMainScreen_Error() {
    UniLinkerTheme {
        ModernMainScreen(
            connectionState = PeerConnectionState.ERROR,
            isScanning = false,
            isConnecting = false,
            devices = emptyList(),
            selectedStrategy = "lan-mdns",
            errorMessage = "连接失败：无法连接到服务器",
            onStrategyChange = {},
            onConnect = {},
            onManualConnect = {},
            onDisconnect = {},
            onRefresh = {},
            onClearError = {}
        ) {
        }
    }
}

@Preview(
    name = "Main Screen - Manual IP Mode",
    showBackground = true,
    backgroundColor = 0xFF121210
)
@Composable
fun PreviewMainScreen_ManualIP() {
    UniLinkerTheme {
        ModernMainScreen(
            connectionState = PeerConnectionState.DISCONNECTED,
            isScanning = false,
            isConnecting = false,
            devices = emptyList(),
            selectedStrategy = "manual-ip",
            errorMessage = null,
            onStrategyChange = {},
            onConnect = {},
            onManualConnect = {},
            onDisconnect = {},
            onRefresh = {},
            onClearError = {}
        ) {
        }
    }
}
