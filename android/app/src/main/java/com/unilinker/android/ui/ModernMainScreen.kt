package com.unilinker.android.ui

import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.unilinker.android.sdk.PeerConnectionState

/**
 * 现代化主屏幕 UI
 * Material 3 设计，支持动画和深色主题
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ModernMainScreen(
    connectionState: PeerConnectionState,
    isScanning: Boolean,
    isConnecting: Boolean,
    devices: List<com.unilinker.android.sdk.models.PeerDevice>,
    selectedStrategy: String,
    errorMessage: String?,
    onStrategyChange: (String) -> Unit,
    onConnect: (com.unilinker.android.sdk.models.PeerDevice) -> Unit,
    onManualConnect: (String) -> Unit,
    onDisconnect: () -> Unit,
    onRefresh: () -> Unit,
    onClearError: () -> Unit,
    content: @Composable () -> Unit
) {
    val isConnected = connectionState == PeerConnectionState.CONNECTED

    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        topBar = {
            TopAppBar(
                title = {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(
                            imageVector = Icons.Default.Cast,
                            contentDescription = null,
                            modifier = Modifier.size(28.dp)
                        )
                        Spacer(modifier = Modifier.width(12.dp))
                        Text(
                            "UniLinker",
                            fontWeight = FontWeight.Bold,
                            fontSize = 20.sp
                        )
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = Color.Transparent
                ),
                actions = {
                    if (isConnected) {
                        FilledTonalButton(
                            onClick = onDisconnect,
                            colors = ButtonDefaults.filledTonalButtonColors(
                                containerColor = MaterialTheme.colorScheme.errorContainer,
                                contentColor = MaterialTheme.colorScheme.onErrorContainer
                            )
                        ) {
                            Icon(Icons.Default.Close, contentDescription = null, modifier = Modifier.size(18.dp))
                            Spacer(modifier = Modifier.width(4.dp))
                            Text("断开")
                        }
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
        ) {
            // Error Banner
            AnimatedVisibility(
                visible = errorMessage != null,
                enter = slideInVertically() + fadeIn(),
                exit = slideOutVertically() + fadeOut()
            ) {
                errorMessage?.let { error ->
                    Card(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 16.dp, vertical = 8.dp),
                        colors = CardDefaults.cardColors(
                            containerColor = MaterialTheme.colorScheme.errorContainer
                        ),
                        shape = RoundedCornerShape(12.dp)
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Icon(
                                Icons.Default.Warning,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.error
                            )
                            Spacer(modifier = Modifier.width(12.dp))
                            Text(
                                error,
                                modifier = Modifier.weight(1f),
                                color = MaterialTheme.colorScheme.onErrorContainer
                            )
                            IconButton(onClick = onClearError) {
                                Icon(Icons.Default.Close, contentDescription = "关闭")
                            }
                        }
                    }
                }
            }

            if (isConnected) {
                // Connected state - show video stream
                content()
            } else {
                // Connection UI
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(horizontal = 16.dp)
                ) {
                    // Status Card
                    StatusCard(
                        connectionState = connectionState,
                        isConnecting = isConnecting,
                        modifier = Modifier.padding(vertical = 8.dp)
                    )

                    Spacer(modifier = Modifier.height(16.dp))

                    // Connection Methods
                    Text(
                        "连接方式",
                        fontWeight = FontWeight.Medium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Spacer(modifier = Modifier.height(8.dp))

                    ConnectionMethodTabs(
                        selectedStrategy = selectedStrategy,
                        onStrategyChange = onStrategyChange
                    )

                    Spacer(modifier = Modifier.height(24.dp))

                    // Quick Connect
                    QuickConnectCard(
                        onConnect = onManualConnect,
                        isConnecting = isConnecting
                    )

                    Spacer(modifier = Modifier.height(24.dp))

                    // Device List
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            "发现的设备",
                            fontWeight = FontWeight.Medium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        if (isScanning) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                CircularProgressIndicator(
                                    modifier = Modifier.size(16.dp),
                                    strokeWidth = 2.dp
                                )
                                Spacer(modifier = Modifier.width(8.dp))
                                Text(
                                    "搜索中...",
                                    fontSize = 12.sp,
                                    color = MaterialTheme.colorScheme.primary
                                )
                            }
                        } else {
                            TextButton(onClick = onRefresh) {
                                Icon(Icons.Default.Refresh, contentDescription = null, modifier = Modifier.size(18.dp))
                                Spacer(modifier = Modifier.width(4.dp))
                                Text("刷新")
                            }
                        }
                    }

                    Spacer(modifier = Modifier.height(12.dp))

                    DeviceListContent(
                        devices = devices,
                        isScanning = isScanning,
                        isConnecting = isConnecting,
                        onConnect = onConnect
                    )
                }
            }
        }
    }
}

@Composable
fun StatusCard(
    connectionState: PeerConnectionState,
    isConnecting: Boolean,
    modifier: Modifier = Modifier
) {
    val (statusText, statusColor, icon) = when (connectionState) {
        PeerConnectionState.CONNECTED -> Triple("已连接", Color(0xFF4CAF50), Icons.Default.CheckCircle)
        PeerConnectionState.CONNECTING -> Triple("连接中...", Color(0xFFFF9800), Icons.Default.Refresh)
        PeerConnectionState.ERROR -> Triple("连接失败", Color(0xFFF44336), Icons.Default.Warning)
        else -> Triple("未连接", Color(0xFF9E9E9E), Icons.Outlined.CastConnected)
    }

    Card(
        modifier = modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)
        ),
        shape = RoundedCornerShape(16.dp)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(20.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(CircleShape)
                    .background(statusColor.copy(alpha = 0.15f)),
                contentAlignment = Alignment.Center
            ) {
                if (isConnecting || connectionState == PeerConnectionState.CONNECTING) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(24.dp),
                        color = statusColor,
                        strokeWidth = 2.dp
                    )
                } else {
                    Icon(
                        icon,
                        contentDescription = null,
                        tint = statusColor,
                        modifier = Modifier.size(24.dp)
                    )
                }
            }
            Spacer(modifier = Modifier.width(16.dp))
            Column {
                Text(
                    statusText,
                    fontWeight = FontWeight.SemiBold,
                    fontSize = 16.sp
                )
                Text(
                    "点击下方设备或输入地址连接",
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}

@Composable
fun ConnectionMethodTabs(
    selectedStrategy: String,
    onStrategyChange: (String) -> Unit
) {
    val methods = listOf(
        "lan-mdns" to ("自动发现" to Icons.Default.Wifi),
        "manual-ip" to ("手动输入" to Icons.Default.Edit),
        "wifi-p2p" to ("Wi-Fi 直连" to Icons.Default.Devices)
    )

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        methods.forEach { (id, data) ->
            val (name, icon) = data
            val isSelected = selectedStrategy == id

            FilterChip(
                selected = isSelected,
                onClick = { onStrategyChange(id) },
                label = {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(icon, contentDescription = null, modifier = Modifier.size(16.dp))
                        Spacer(modifier = Modifier.width(6.dp))
                        Text(name)
                    }
                },
                modifier = Modifier.weight(1f)
            )
        }
    }
}

@Composable
fun QuickConnectCard(
    onConnect: (String) -> Unit,
    isConnecting: Boolean
) {
    var address by remember { mutableStateOf("") }

    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp)
    ) {
        Column(
            modifier = Modifier.padding(20.dp)
        ) {
            Text(
                "快速连接",
                fontWeight = FontWeight.Medium,
                fontSize = 14.sp
            )
            Spacer(modifier = Modifier.height(12.dp))
            OutlinedTextField(
                value = address,
                onValueChange = { address = it },
                label = { Text("输入 IP:端口 或 localhost:9527") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                trailingIcon = {
                    IconButton(
                        onClick = {
                            if (address.isNotBlank()) {
                                onConnect(address)
                            }
                        },
                        enabled = address.isNotBlank() && !isConnecting
                    ) {
                        if (isConnecting) {
                            CircularProgressIndicator(
                                modifier = Modifier.size(20.dp),
                                strokeWidth = 2.dp
                            )
                        } else {
                            Icon(Icons.Default.ArrowForward, contentDescription = "连接")
                        }
                    }
                }
            )
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                "提示: 使用 ADB 转发时输入 localhost:9527",
                fontSize = 11.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f)
            )
        }
    }
}

@Composable
fun DeviceListContent(
    devices: List<com.unilinker.android.sdk.models.PeerDevice>,
    isScanning: Boolean,
    isConnecting: Boolean,
    onConnect: (com.unilinker.android.sdk.models.PeerDevice) -> Unit
) {
    if (devices.isEmpty()) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(200.dp),
            contentAlignment = Alignment.Center
        ) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Icon(
                    Icons.Outlined.Devices,
                    contentDescription = null,
                    modifier = Modifier.size(64.dp),
                    tint = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.3f)
                )
                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    if (isScanning) "搜索设备中..." else "暂无发现设备",
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                if (!isScanning) {
                    TextButton(onClick = { /* refresh */ }) {
                        Text("点击刷新")
                    }
                }
            }
        }
    } else {
        Column {
            devices.forEach { device ->
                DeviceItem(
                    device = device,
                    isConnecting = isConnecting,
                    onConnect = { onConnect(device) }
                )
                Spacer(modifier = Modifier.height(8.dp))
            }
        }
    }
}

@Composable
fun DeviceItem(
    device: com.unilinker.android.sdk.models.PeerDevice,
    isConnecting: Boolean,
    onConnect: () -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surface
        ),
        onClick = onConnect,
        enabled = !isConnecting
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Device Icon
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(MaterialTheme.colorScheme.primaryContainer),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    Icons.Outlined.Computer,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onPrimaryContainer,
                    modifier = Modifier.size(24.dp)
                )
            }

            Spacer(modifier = Modifier.width(16.dp))

            // Device Info
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    device.name,
                    fontWeight = FontWeight.Medium,
                    fontSize = 15.sp
                )
                Text(
                    device.ipAddress,
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Row(
                    modifier = Modifier.padding(top = 4.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Box(
                        modifier = Modifier
                            .size(6.dp)
                            .clip(CircleShape)
                            .background(Color(0xFF4CAF50))
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        "在线",
                        fontSize = 11.sp,
                        color = Color(0xFF4CAF50)
                    )
                }
            }

            // Connect Button
            FilledTonalButton(
                onClick = onConnect,
                enabled = !isConnecting
            ) {
                if (isConnecting) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(16.dp),
                        strokeWidth = 2.dp
                    )
                } else {
                    Text("连接")
                }
            }
        }
    }
}