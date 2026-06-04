package com.unilinker.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.unilinker.android.sdk.IConnectionStrategy
import com.unilinker.android.sdk.ShareInfo
import com.unilinker.android.sdk.ShareType
import com.unilinker.android.ui.theme.*

/**
 * 工业风连接方式选择器
 *
 * - 扁平化设计
 * - 无 Card 背景
 * - 线性图标
 */
@Composable
fun ConnectionMethodPicker(
    strategies: List<IConnectionStrategy>,
    activeStrategyId: String,
    onStrategySelected: (IConnectionStrategy) -> Unit,
    onConnectCode: (String) -> Unit,
    onConnectAddress: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(modifier = modifier.padding(horizontal = 16.dp)) {
        // Strategy tabs
        Text(
            text = "连接方式",
            fontWeight = FontWeight.Bold,
            fontSize = 14.sp,
            color = TextPrimary,
            modifier = Modifier.padding(bottom = 12.dp),
        )

        // Tab buttons
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            strategies.forEach { strategy ->
                val isActive = strategy.id == activeStrategyId

                IndustrialStrategyTab(
                    label = strategy.name,
                    selected = isActive,
                    onClick = { onStrategySelected(strategy) },
                    modifier = Modifier.weight(1f)
                )
            }
        }

        Spacer(modifier = Modifier.height(12.dp))

        // Description of active strategy
        val active = strategies.firstOrNull { it.id == activeStrategyId }
        Text(
            text = active?.description ?: "",
            fontSize = 12.sp,
            color = TextTertiary,
        )

        Spacer(modifier = Modifier.height(16.dp))

        // Per-strategy input UI
        when (active?.id) {
            "lan-mdns" -> {
                // Auto-discover: 提示文字
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Icon(
                        Icons.Outlined.Wifi,
                        contentDescription = null,
                        tint = TextTertiary,
                        modifier = Modifier.size(16.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = "正在自动扫描局域网设备...",
                        fontSize = 13.sp,
                        color = TextTertiary,
                    )
                }
            }

            "manual-ip" -> {
                IndustrialAddressInputField(
                    onConnect = onConnectAddress
                )
            }

            "connection-code" -> {
                IndustrialCodeInputField(
                    onConnect = onConnectCode
                )
            }
        }
    }
}

@Composable
private fun IndustrialStrategyTab(
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    val interactionSource = remember { MutableInteractionSource() }

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

    Box(
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
        contentAlignment = Alignment.Center
    ) {
        Text(
            label,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
            fontSize = 13.sp,
            color = contentColor
        )
    }
}

@Composable
private fun IndustrialAddressInputField(
    onConnect: (String) -> Unit
) {
    var address by remember { mutableStateOf("") }

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(modifier = Modifier.weight(1f)) {
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

        IconButton(
            onClick = {
                if (address.isNotBlank()) {
                    onConnect(address)
                }
            },
            enabled = address.isNotBlank(),
            colors = IconButtonDefaults.iconButtonColors(
                contentColor = Copper,
                disabledContentColor = TextTertiary
            )
        ) {
            Icon(
                Icons.Outlined.ArrowForward,
                contentDescription = "连接",
                modifier = Modifier.size(24.dp)
            )
        }
    }
}

@Composable
private fun IndustrialCodeInputField(
    onConnect: (String) -> Unit
) {
    var code by remember { mutableStateOf("") }

    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(modifier = Modifier.weight(1f)) {
            BasicTextField(
                value = code,
                onValueChange = {
                    if (it.length <= 6 && it.all { c -> c.isDigit() })
                        code = it
                },
                singleLine = true,
                textStyle = LocalTextStyle.current.copy(
                    color = TextPrimary,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 4.sp
                ),
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Number,
                    imeAction = ImeAction.Done,
                ),
                keyboardActions = KeyboardActions(
                    onDone = { if (code.length == 6) onConnect(code) },
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

            if (code.isEmpty()) {
                Text(
                    "6 位连接码",
                    fontSize = 12.sp,
                    color = TextTertiary,
                    modifier = Modifier.padding(bottom = 9.dp)
                )
            }
        }

        // Connect button
        Button(
            onClick = { onConnect(code) },
            enabled = code.length == 6,
            colors = ButtonDefaults.buttonColors(
                containerColor = Copper,
                contentColor = OnCopper,
                disabledContainerColor = BackgroundVariant,
                disabledContentColor = TextTertiary
            ),
            shape = RoundedCornerShape(4.dp),
            contentPadding = PaddingValues(horizontal = 24.dp, vertical = 12.dp)
        ) {
            Text(
                "连接",
                fontWeight = FontWeight.Bold,
                fontSize = 13.sp
            )
        }
    }
}

@Composable
fun ShareInfoCard(
    shareInfo: ShareInfo?,
    onGenerateCode: () -> Unit,
    modifier: Modifier = Modifier,
) {
    if (shareInfo == null) return

    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp)
            .background(BackgroundVariant)
            .padding(16.dp)
    ) {
        Text(
            text = "分享给其他人",
            fontSize = 13.sp,
            fontWeight = FontWeight.Bold,
            color = TextPrimary,
        )

        Spacer(modifier = Modifier.height(12.dp))

        when (shareInfo.type) {
            ShareType.CONNECTION_CODE -> {
                Text(
                    text = shareInfo.value,
                    fontSize = 28.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 8.sp,
                    color = Copper,
                )
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = "让对方在连接码模式下输入此码",
                    fontSize = 12.sp,
                    color = TextTertiary,
                )
            }

            ShareType.IP_PORT -> {
                Text(
                    text = shareInfo.value,
                    fontSize = 16.sp,
                    fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                    color = TextPrimary,
                )
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = shareInfo.displayText,
                    fontSize = 12.sp,
                    color = TextTertiary,
                )
            }

            ShareType.QR_CODE_URL -> {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        Icons.Outlined.QrCode2,
                        contentDescription = null,
                        tint = Copper,
                        modifier = Modifier.size(24.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = "扫码连接",
                        fontSize = 14.sp,
                        color = TextPrimary,
                    )
                }
            }
        }
    }
}
