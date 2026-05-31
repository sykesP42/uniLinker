package com.unilinker.android.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.unilinker.android.sdk.models.PeerDevice

@Composable
fun DeviceListView(
    devices: List<PeerDevice>,
    isScanning: Boolean,
    onConnect: (PeerDevice) -> Unit,
) {
    Column(modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
        if (devices.isEmpty()) {
            Box(
                modifier = Modifier.fillMaxWidth().padding(vertical = 32.dp),
                contentAlignment = Alignment.Center,
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(
                        text = if (isScanning) "🔍" else "📭",
                        fontSize = 36.sp,
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                    Text(
                        text = if (isScanning) "搜索设备中…"
                        else "未发现设备\n尝试切换连接方式",
                        fontSize = 13.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        } else {
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                items(devices) { device ->
                    Card(
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = CardDefaults.cardColors(
                            containerColor = MaterialTheme.colorScheme.surface,
                        ),
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth().padding(16.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text("🖥️", fontSize = 24.sp)
                            Spacer(modifier = Modifier.width(12.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = device.name,
                                    fontSize = 14.sp,
                                    fontWeight = FontWeight.Medium,
                                )
                                Text(
                                    text = device.ipAddress,
                                    fontSize = 12.sp,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                            Button(
                                onClick = { onConnect(device) },
                                shape = RoundedCornerShape(8.dp),
                            ) { Text("连接") }
                        }
                    }
                }
            }
        }
    }
}
