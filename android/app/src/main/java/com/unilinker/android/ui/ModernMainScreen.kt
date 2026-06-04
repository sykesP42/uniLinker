package com.unilinker.android.ui

import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.unilinker.android.sdk.PeerConnectionState
import com.unilinker.android.sdk.models.PeerDevice
import com.unilinker.android.ui.theme.*

/**
 * UniLinker Industrial UI
 *
 * 极简工业风主屏幕：
 * - 功能分区明确（Header、Status、Control、Devices）
 * - 扁平化设计，无 Card 背景
 * - 统一 4.dp 微圆角
 * - 线性图标，无 emoji
 * - 暖灰基调 + 铜金色功能色
 */
@OptIn(ExperimentalMaterial3Api::class, ExperimentalAnimationApi::class)
@Composable
fun ModernMainScreen(
    connectionState: PeerConnectionState,
    isScanning: Boolean,
    isConnecting: Boolean,
    devices: List<PeerDevice>,
    selectedStrategy: String,
    errorMessage: String?,
    onStrategyChange: (String) -> Unit,
    onConnect: (PeerDevice) -> Unit,
    onManualConnect: (String) -> Unit,
    onDisconnect: () -> Unit,
    onRefresh: () -> Unit,
    onClearError: () -> Unit,
    content: @Composable () -> Unit
) {
    val isConnected = connectionState == PeerConnectionState.CONNECTED

    Scaffold(
        containerColor = BackgroundDeep,
        topBar = {
            IndustrialHeader(
                isConnected = isConnected,
                onDisconnect = onDisconnect
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
        ) {
            // Error Banner (顶部固定)
            AnimatedVisibility(
                visible = errorMessage != null,
                enter = slideInVertically(
                    animationSpec = tween(IndustrialDesign.ANIM_FAST)
                ) + fadeIn(tween(IndustrialDesign.ANIM_FAST)),
                exit = slideOutVertically(
                    animationSpec = tween(IndustrialDesign.ANIM_FAST)
                ) + fadeOut(tween(IndustrialDesign.ANIM_FAST))
            ) {
                errorMessage?.let { error ->
                    IndustrialErrorBanner(
                        message = error,
                        onDismiss = onClearError
                    )
                }
            }

            if (isConnected) {
                // Connected state - show video stream
                content()
            } else {
                // Connection UI
                IndustrialConnectionPanel(
                    connectionState = connectionState,
                    isScanning = isScanning,
                    isConnecting = isConnecting,
                    devices = devices,
                    selectedStrategy = selectedStrategy,
                    onStrategyChange = onStrategyChange,
                    onConnect = onConnect,
                    onManualConnect = onManualConnect,
                    onRefresh = onRefresh
                )
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// HEADER COMPONENT
// ═══════════════════════════════════════════════════════════════

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun IndustrialHeader(
    isConnected: Boolean,
    onDisconnect: () -> Unit
) {
    TopAppBar(
        title = {
            Row(verticalAlignment = Alignment.CenterVertically) {
                // Logo: 线性图标，无文字
                Icon(
                    imageVector = Icons.Outlined.Cast,
                    contentDescription = "UniLinker",
                    modifier = Modifier.size(24.dp),
                    tint = Copper
                )
                Spacer(modifier = Modifier.width(12.dp))
                Text(
                    "UNILINKER",
                    fontWeight = FontWeight.Bold,
                    fontSize = 14.sp,
                    letterSpacing = 2.sp,
                    color = TextPrimary
                )
            }
        },
        colors = TopAppBarDefaults.topAppBarColors(
            containerColor = BackgroundDeep
        ),
        actions = {
            if (isConnected) {
                // Disconnect button: 铜金色文字按钮
                TextButton(
                    onClick = onDisconnect,
                    colors = ButtonDefaults.textButtonColors(
                        contentColor = StatusError
                    )
                ) {
                    Icon(
                        Icons.Outlined.Close,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("断开", fontWeight = FontWeight.Medium)
                }
            }
        }
    )
}

// ═══════════════════════════════════════════════════════════════
// ERROR BANNER
// ═══════════════════════════════════════════════════════════════

@Composable
private fun IndustrialErrorBanner(
    message: String,
    onDismiss: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(StatusError)
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(
            Icons.Outlined.Warning,
            contentDescription = null,
            tint = TextPrimary,
            modifier = Modifier.size(20.dp)
        )
        Spacer(modifier = Modifier.width(12.dp))
        Text(
            message,
            color = TextPrimary,
            fontWeight = FontWeight.Medium,
            fontSize = 13.sp,
            modifier = Modifier.weight(1f)
        )
        IconButton(onClick = onDismiss) {
            Icon(
                Icons.Outlined.Close,
                contentDescription = "关闭",
                tint = TextPrimary,
                modifier = Modifier.size(18.dp)
            )
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// CONNECTION PANEL (Main Layout)
// ═══════════════════════════════════════════════════════════════

@Composable
private fun IndustrialConnectionPanel(
    connectionState: PeerConnectionState,
    isScanning: Boolean,
    isConnecting: Boolean,
    devices: List<PeerDevice>,
    selectedStrategy: String,
    onStrategyChange: (String) -> Unit,
    onConnect: (PeerDevice) -> Unit,
    onManualConnect: (String) -> Unit,
    onRefresh: () -> Unit
) {
    Column(modifier = Modifier.fillMaxSize()) {
        // ZONE 1: STATUS INDICATOR
        IndustrialStatusIndicator(
            connectionState = connectionState,
            isScanning = isScanning,
            isConnecting = isConnecting
        )

        // Divider
        HorizontalDivider(
            thickness = 1.dp,
            color = BorderWeak,
            modifier = Modifier.padding(horizontal = 16.dp)
        )

        // ZONE 2: CONTROL
        IndustrialControlZone(
            selectedStrategy = selectedStrategy,
            onStrategyChange = onStrategyChange,
            onManualConnect = onManualConnect,
            isConnecting = isConnecting
        )

        // Divider
        HorizontalDivider(
            thickness = 1.dp,
            color = BorderWeak,
            modifier = Modifier.padding(horizontal = 16.dp)
        )

        // ZONE 3: DEVICE LIST
        IndustrialDeviceList(
            devices = devices,
            isScanning = isScanning,
            isConnecting = isConnecting,
            onConnect = onConnect,
            onRefresh = onRefresh
        )
    }
}

// ═══════════════════════════════════════════════════════════════
// STATUS INDICATOR
// ═══════════════════════════════════════════════════════════════

@Composable
private fun IndustrialStatusIndicator(
    connectionState: PeerConnectionState,
    isScanning: Boolean,
    isConnecting: Boolean
) {
    val (statusColor, statusText, showPulse) = when (connectionState) {
        PeerConnectionState.CONNECTED -> Triple(Copper, "LINKED", false)
        PeerConnectionState.CONNECTING -> Triple(StatusWarning, "CONNECTING", true)
        PeerConnectionState.ERROR -> Triple(StatusError, "ERROR", false)
        else -> Triple(TextTertiary, "STANDBY", false)
    }

    val activeStatusText = if (isScanning) "SCANNING" else statusText
    val activeColor = if (isScanning) StatusWarning else statusColor
    val activePulse = isScanning || isConnecting || showPulse

    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 32.dp),
        contentAlignment = Alignment.Center
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            // Status circle
            Box(
                modifier = Modifier.size(48.dp),
                contentAlignment = Alignment.Center
            ) {
                // Pulse animation - only animate when active
                if (activePulse) {
                    val transition = rememberInfiniteTransition(label = "pulse")
                    val alpha by transition.animateFloat(
                        initialValue = 0.4f,
                        targetValue = 1f,
                        animationSpec = infiniteRepeatable(
                            animation = tween(800, easing = LinearEasing),
                            repeatMode = RepeatMode.Reverse
                        ),
                        label = "alpha"
                    )
                    val scale by transition.animateFloat(
                        initialValue = 0.8f,
                        targetValue = 1f,
                        animationSpec = infiniteRepeatable(
                            animation = tween(800, easing = LinearEasing),
                            repeatMode = RepeatMode.Reverse
                        ),
                        label = "scale"
                    )

                    Box(
                        modifier = Modifier
                            .size(48.dp)
                            .scale(scale)
                            .background(activeColor.copy(alpha = alpha * 0.3f), CircleShape)
                    )
                }

                // Core circle
                Box(
                    modifier = Modifier
                        .size(16.dp)
                        .background(activeColor, CircleShape)
                )

                // Rotating indicator for connecting
                if (isConnecting) {
                    val rotationTransition = rememberInfiniteTransition(label = "rotation")
                    val rotation by rotationTransition.animateFloat(
                        initialValue = 0f,
                        targetValue = 360f,
                        animationSpec = infiniteRepeatable(
                            animation = tween(1000, easing = LinearEasing),
                            repeatMode = RepeatMode.Restart
                        ),
                        label = "rotation"
                    )
                    Box(
                        modifier = Modifier
                            .size(32.dp)
                            .graphicsLayer { rotationZ = rotation }
                    ) {
                        // Arc indicator
                        Box(
                            modifier = Modifier
                                .align(Alignment.TopCenter)
                                .size(4.dp)
                                .background(StatusWarning, CircleShape)
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            // Status text
            Text(
                activeStatusText,
                fontWeight = FontWeight.Bold,
                fontSize = 14.sp,
                letterSpacing = 2.sp,
                color = activeColor
            )
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// CONTROL ZONE
// ═══════════════════════════════════════════════════════════════

@Composable
private fun IndustrialControlZone(
    selectedStrategy: String,
    onStrategyChange: (String) -> Unit,
    onManualConnect: (String) -> Unit,
    isConnecting: Boolean
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(16.dp)
    ) {
        // Connection method tabs
        IndustrialConnectionTabs(
            selectedStrategy = selectedStrategy,
            onStrategyChange = onStrategyChange
        )

        Spacer(modifier = Modifier.height(16.dp))

        // Strategy-specific content
        when (selectedStrategy) {
            "lan-mdns" -> {
                // Auto-discover: 提示文字
                Text(
                    "自动扫描局域网设备",
                    fontSize = 12.sp,
                    color = TextTertiary,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth()
                )
            }
            "manual-ip" -> {
                // Manual input: 单行输入框
                IndustrialAddressInput(
                    onConnect = onManualConnect,
                    isConnecting = isConnecting
                )
            }
            "wifi-p2p" -> {
                // Wi-Fi Direct
                Text(
                    "Wi-Fi 直连模式",
                    fontSize = 12.sp,
                    color = TextTertiary,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth()
                )
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// CONNECTION TABS
// ═══════════════════════════════════════════════════════════════

@Composable
private fun IndustrialConnectionTabs(
    selectedStrategy: String,
    onStrategyChange: (String) -> Unit
) {
    val methods = listOf(
        Triple("lan-mdns", Icons.Outlined.Wifi, "自动"),
        Triple("manual-ip", Icons.Outlined.Edit, "手动"),
        Triple("wifi-p2p", Icons.Outlined.Devices, "直连")
    )

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        methods.forEach { (id, icon, label) ->
            val isSelected = selectedStrategy == id

            IndustrialTabButton(
                icon = icon,
                label = label,
                selected = isSelected,
                onClick = { onStrategyChange(id) },
                modifier = Modifier.weight(1f)
            )
        }
    }
}

@Composable
private fun IndustrialTabButton(
    icon: ImageVector,
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    val interactionSource = remember { MutableInteractionSource() }
    val isPressed by interactionSource.collectIsPressedAsState()

    val backgroundColor = if (selected) {
        Copper.copy(alpha = 0.15f)
    } else {
        BackgroundVariant
    }

    val borderColor = if (selected) {
        Copper
    } else {
        BorderWeak
    }

    val contentColor = if (selected) {
        Copper
    } else {
        TextTertiary
    }

    Row(
        modifier = modifier
            .height(40.dp)
            .background(backgroundColor, RoundedCornerShape(4.dp))
            .border(1.dp, borderColor, RoundedCornerShape(4.dp))
            .clickable(
                interactionSource = interactionSource,
                indication = null,
                onClick = onClick
            )
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.Center
    ) {
        Icon(
            icon,
            contentDescription = null,
            tint = contentColor,
            modifier = Modifier.size(18.dp)
        )
        Spacer(modifier = Modifier.width(8.dp))
        Text(
            label,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
            fontSize = 13.sp,
            color = contentColor
        )
    }
}

// ═══════════════════════════════════════════════════════════════
// ADDRESS INPUT
// ═══════════════════════════════════════════════════════════════

@Composable
private fun IndustrialAddressInput(
    onConnect: (String) -> Unit,
    isConnecting: Boolean
) {
    var address by remember { mutableStateOf("") }

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically
    ) {
        // Input field: 无边框，只有底部线
        Box(
            modifier = Modifier.weight(1f)
        ) {
            BasicTextField(
                value = address,
                onValueChange = { address = it },
                singleLine = true,
                textStyle = LocalTextStyle.current.copy(
                    color = TextPrimary,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Normal
                ),
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Uri,
                    imeAction = ImeAction.Go
                ),
                keyboardActions = KeyboardActions(
                    onGo = {
                        if (address.isNotBlank()) {
                            onConnect(address)
                        }
                    }
                ),
                decorationBox = { innerTextField ->
                    Column {
                        innerTextField()
                        Spacer(modifier = Modifier.height(8.dp))
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(1.dp)
                                .background(BorderStrong)
                        )
                    }
                },
                modifier = Modifier.fillMaxWidth()
            )

            // Placeholder
            if (address.isEmpty()) {
                Text(
                    "IP:PORT 或 localhost:9527",
                    fontSize = 12.sp,
                    color = TextTertiary,
                    modifier = Modifier.padding(bottom = 9.dp)
                )
            }
        }

        Spacer(modifier = Modifier.width(12.dp))

        // Connect button: 铜金色箭头
        IconButton(
            onClick = {
                if (address.isNotBlank()) {
                    onConnect(address)
                }
            },
            enabled = address.isNotBlank() && !isConnecting,
            colors = IconButtonDefaults.iconButtonColors(
                contentColor = Copper,
                disabledContentColor = TextTertiary
            )
        ) {
            if (isConnecting) {
                CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    strokeWidth = 2.dp,
                    color = StatusWarning
                )
            } else {
                Icon(
                    Icons.Outlined.ArrowForward,
                    contentDescription = "连接",
                    modifier = Modifier.size(24.dp)
                )
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// DEVICE LIST
// ═══════════════════════════════════════════════════════════════

@Composable
private fun IndustrialDeviceList(
    devices: List<PeerDevice>,
    isScanning: Boolean,
    isConnecting: Boolean,
    onConnect: (PeerDevice) -> Unit,
    onRefresh: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 16.dp)
    ) {
        // Header row
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 8.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                "设备",
                fontWeight = FontWeight.Bold,
                fontSize = 14.sp,
                color = TextPrimary
            )

            if (isScanning) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(14.dp),
                        strokeWidth = 2.dp,
                        color = StatusWarning
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        "扫描中",
                        fontSize = 12.sp,
                        color = StatusWarning
                    )
                }
            } else {
                TextButton(
                    onClick = onRefresh,
                    colors = ButtonDefaults.textButtonColors(
                        contentColor = Copper
                    )
                ) {
                    Icon(
                        Icons.Outlined.Refresh,
                        contentDescription = null,
                        modifier = Modifier.size(16.dp)
                    )
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("刷新", fontSize = 12.sp)
                }
            }
        }

        // Divider
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(1.dp)
                .background(BorderWeak)
        )

        // List content
        if (devices.isEmpty()) {
            IndustrialEmptyState(
                isScanning = isScanning,
                onRefresh = onRefresh
            )
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxSize()
            ) {
                items(
                    items = devices,
                    key = { device -> device.id }
                ) { device ->
                    IndustrialDeviceItem(
                        device = device,
                        isConnecting = isConnecting,
                        onClick = { onConnect(device) }
                    )
                }
            }
        }
    }
}

@Composable
private fun IndustrialEmptyState(
    isScanning: Boolean,
    onRefresh: () -> Unit
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .padding(32.dp),
        contentAlignment = Alignment.Center
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Icon(
                Icons.Outlined.Devices,
                contentDescription = null,
                modifier = Modifier.size(48.dp),
                tint = TextTertiary.copy(alpha = 0.5f)
            )
            Spacer(modifier = Modifier.height(16.dp))
            Text(
                if (isScanning) "正在扫描..." else "未发现设备",
                fontSize = 13.sp,
                color = TextTertiary
            )
            if (!isScanning) {
                Spacer(modifier = Modifier.height(8.dp))
                TextButton(
                    onClick = onRefresh,
                    colors = ButtonDefaults.textButtonColors(
                        contentColor = Copper
                    )
                ) {
                    Text("点击刷新", fontSize = 12.sp)
                }
            }
        }
    }
}

@Composable
private fun IndustrialDeviceItem(
    device: PeerDevice,
    isConnecting: Boolean,
    onClick: () -> Unit
) {
    val interactionSource = remember { MutableInteractionSource() }

    Column(
        modifier = Modifier.clickable(
            interactionSource = interactionSource,
            indication = null,
            enabled = !isConnecting,
            onClick = onClick
        )
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Status indicator: 铜金方块
            Box(
                modifier = Modifier
                    .size(8.dp)
                    .background(Copper, RoundedCornerShape(2.dp))
            )

            Spacer(modifier = Modifier.width(12.dp))

            // Device info
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    device.name,
                    fontWeight = FontWeight.Bold,
                    fontSize = 14.sp,
                    color = TextPrimary
                )
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    device.ipAddress,
                    fontSize = 12.sp,
                    color = TextTertiary,
                    fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace
                )
            }

            // Connect arrow: 铜金色
            Icon(
                Icons.Outlined.ArrowForward,
                contentDescription = "连接",
                tint = if (isConnecting) TextTertiary else Copper,
                modifier = Modifier.size(20.dp)
            )
        }

        // Divider
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(1.dp)
                .background(BorderWeak)
        )
    }
}