package com.unilinker.android.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
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
        // Strategy chips
        Text(
            text = "连接方式",
            fontSize = 12.sp,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(bottom = 8.dp),
        )

        LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items(strategies) { strategy ->
                val isActive = strategy.id == activeStrategyId
                FilterChip(
                    selected = isActive,
                    onClick = { onStrategySelected(strategy) },
                    label = {
                        Text(
                            "${strategy.icon} ${strategy.name}",
                            fontSize = 13.sp,
                        )
                    },
                    colors = FilterChipDefaults.filterChipColors(
                        selectedContainerColor = MaterialTheme.colorScheme.primaryContainer,
                    ),
                    shape = RoundedCornerShape(20.dp),
                )
            }
        }

        Spacer(modifier = Modifier.height(4.dp))

        // Description of active strategy
        val active = strategies.firstOrNull { it.id == activeStrategyId }
        Text(
            text = active?.description ?: "",
            fontSize = 12.sp,
            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f),
        )

        Spacer(modifier = Modifier.height(12.dp))

        // Per-strategy input UI
        when (active?.id) {
            "lan-mdns" -> {
                // Auto-discover, nothing to input
                Box(
                    modifier = Modifier.fillMaxWidth(),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = "正在自动扫描局域网设备…",
                        fontSize = 13.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }

            "manual-ip" -> {
                var address by remember { mutableStateOf("") }
                OutlinedTextField(
                    value = address,
                    onValueChange = { address = it },
                    label = { Text("IP:Port") },
                    placeholder = { Text("例如 192.168.1.5:9527") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(
                        keyboardType = KeyboardType.Uri,
                        imeAction = ImeAction.Go,
                    ),
                    keyboardActions = KeyboardActions(
                        onGo = { onConnectAddress(address) },
                    ),
                    trailingIcon = {
                        TextButton(onClick = { onConnectAddress(address) }) {
                            Text("连接")
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(8.dp),
                )
            }

            "connection-code" -> {
                var code by remember { mutableStateOf("") }
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    OutlinedTextField(
                        value = code,
                        onValueChange = {
                            if (it.length <= 6 && it.all { c -> c.isDigit() })
                                code = it
                        },
                        label = { Text("6 位连接码") },
                        placeholder = { Text("输入对方的码") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(
                            keyboardType = KeyboardType.Number,
                            imeAction = ImeAction.Done,
                        ),
                        keyboardActions = KeyboardActions(
                            onDone = { if (code.length == 6) onConnectCode(code) },
                        ),
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(8.dp),
                    )
                    Button(
                        onClick = { onConnectCode(code) },
                        enabled = code.length == 6,
                    ) {
                        Text("连接")
                    }
                }
            }
        }
    }
}

@Composable
fun ShareInfoCard(
    shareInfo: com.unilinker.android.sdk.ShareInfo?,
    onGenerateCode: () -> Unit,
    modifier: Modifier = Modifier,
) {
    if (shareInfo == null) return

    Card(
        modifier = modifier.fillMaxWidth().padding(horizontal = 16.dp),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.3f),
        ),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Text(
                text = "📡 分享给其他人",
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                color = MaterialTheme.colorScheme.onSurface,
            )

            Spacer(modifier = Modifier.height(8.dp))

            when (shareInfo.type) {
                com.unilinker.android.sdk.ShareType.CONNECTION_CODE -> {
                    Text(
                        text = shareInfo.value,
                        fontSize = 32.sp,
                        fontWeight = FontWeight.Bold,
                        letterSpacing = 8.sp,
                        color = MaterialTheme.colorScheme.primary,
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = "让对方在连接码模式下输入此码",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }

                com.unilinker.android.sdk.ShareType.IP_PORT -> {
                    Text(
                        text = shareInfo.value,
                        fontSize = 18.sp,
                        fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = shareInfo.displayText,
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }

                com.unilinker.android.sdk.ShareType.QR_CODE_URL -> {
                    Text(
                        text = "扫码连接",
                        fontSize = 14.sp,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                }
            }
        }
    }
}
