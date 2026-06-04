package com.unilinker.android.ui

import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.unilinker.android.sdk.models.PeerDevice
import com.unilinker.android.ui.theme.*

/**
 * Industrial Device List View
 *
 * 扁平化设计：
 * - 无 Card 背景
 * - 依靠分割线区分项目
 * - 统一 4.dp 微圆角
 */
@Composable
fun DeviceListView(
    devices: List<PeerDevice>,
    isScanning: Boolean,
    isConnecting: Boolean = false,
    onConnect: (PeerDevice) -> Unit,
) {
    Column(modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
        AnimatedVisibility(
            visible = devices.isEmpty(),
            enter = fadeIn(tween(IndustrialDesign.ANIM_FAST)) + slideInVertically(
                animationSpec = tween(IndustrialDesign.ANIM_FAST)
            ),
            exit = fadeOut(tween(IndustrialDesign.ANIM_FAST)) + slideOutVertically(
                animationSpec = tween(IndustrialDesign.ANIM_FAST)
            )
        ) {
            IndustrialEmptyDeviceState(isScanning = isScanning)
        }

        AnimatedVisibility(
            visible = devices.isNotEmpty(),
            enter = fadeIn(tween(IndustrialDesign.ANIM_NORMAL)),
            exit = fadeOut(tween(IndustrialDesign.ANIM_NORMAL))
        ) {
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(0.dp),
            ) {
                items(
                    items = devices,
                    key = { device -> device.id }
                ) { device ->
                    IndustrialDeviceListItem(
                        device = device,
                        isConnecting = isConnecting,
                        onClick = { onConnect(device) },
                    )
                }
            }
        }
    }
}

@Composable
private fun IndustrialEmptyDeviceState(
    isScanning: Boolean
) {
    Box(
        modifier = Modifier.fillMaxWidth().padding(vertical = 48.dp),
        contentAlignment = Alignment.Center,
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            // Icon: 线性图标，无 emoji
            Icon(
                imageVector = Icons.Outlined.Devices,
                contentDescription = null,
                modifier = Modifier.size(48.dp),
                tint = TextTertiary.copy(alpha = 0.5f)
            )
            Spacer(modifier = Modifier.height(16.dp))
            Text(
                text = if (isScanning) "正在扫描..." else "未发现设备",
                fontSize = 13.sp,
                color = TextTertiary,
            )
            if (isScanning) {
                Spacer(modifier = Modifier.height(16.dp))
                LinearProgressIndicator(
                    modifier = Modifier.width(120.dp),
                    color = StatusWarning,
                    trackColor = BorderWeak,
                )
            }
        }
    }
}

@Composable
private fun IndustrialDeviceListItem(
    device: PeerDevice,
    isConnecting: Boolean,
    onClick: () -> Unit,
) {
    val interactionSource = remember { MutableInteractionSource() }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(
                interactionSource = interactionSource,
                indication = null,
                enabled = !isConnecting,
                onClick = onClick
            )
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 12.dp)
                .alpha(if (isConnecting) 0.6f else 1f),
            verticalAlignment = Alignment.CenterVertically,
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
                    text = device.name,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary,
                )
                Text(
                    text = device.ipAddress,
                    fontSize = 12.sp,
                    color = TextTertiary,
                    fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                )
            }

            // Connect indicator
            if (isConnecting) {
                CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    strokeWidth = 2.dp,
                    color = StatusWarning,
                )
            } else {
                Icon(
                    imageVector = Icons.Outlined.ArrowForward,
                    contentDescription = "连接",
                    tint = Copper,
                    modifier = Modifier.size(20.dp),
                )
            }
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
