package com.unilinker.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import com.unilinker.android.core.Platform
import com.unilinker.android.plugins.screenmirror.ScreenMirrorPlugin
import com.unilinker.android.plugins.screenmirror.ScreenMirrorTab
import com.unilinker.android.sdk.PeerConnectionState
import com.unilinker.android.sdk.models.PeerDevice
import com.unilinker.android.ui.ConnectionMethodPicker
import com.unilinker.android.ui.DeviceListView
import com.unilinker.android.ui.MainScreen
import com.unilinker.android.ui.theme.UniLinkerTheme
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch
import org.webrtc.ContextUtils

class MainActivity : ComponentActivity() {

    private val platform by lazy { Platform(applicationContext) }
    private val screenMirror = ScreenMirrorPlugin()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ContextUtils.initialize(applicationContext)

        platform.registerPlugin(screenMirror)

        kotlinx.coroutines.MainScope().launch {
            platform.start()
        }

        setContent {
            UniLinkerTheme {
                UniLinkerApp(platform, screenMirror)
            }
        }
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    private fun UniLinkerApp(platform: Platform, screenMirror: ScreenMirrorPlugin) {
        var activeStrategyId by remember {
            mutableStateOf(platform.config.get("active_strategy", "lan-mdns"))
        }
        var devices by remember { mutableStateOf<List<PeerDevice>>(emptyList()) }
        var isScanning by remember { mutableStateOf(true) }
        var connectedDeviceName by remember { mutableStateOf<String?>(null) }

        val connectionState by screenMirror.connectionState.collectAsState()
        val isConnected = connectionState == PeerConnectionState.CONNECTED

        // Auto-discover when on auto-discover strategies
        LaunchedEffect(activeStrategyId) {
            val strategy = platform.strategies.firstOrNull { it.id == activeStrategyId }
            if (strategy != null && strategy.autoDiscover) {
                isScanning = true
                platform.config.set("active_strategy", activeStrategyId)
                strategy.start()
                strategy.discover().collectLatest { list ->
                    devices = list
                    isScanning = false
                }
            } else {
                devices = emptyList()
                isScanning = false
            }
        }

        Scaffold(
            topBar = {
                TopAppBar(
                    title = { Text(connectedDeviceName ?: "UniLinker") },
                    actions = {
                        if (connectedDeviceName != null) {
                            TextButton(onClick = {
                                screenMirror.disconnect()
                                connectedDeviceName = null
                            }) { Text("断开") }
                        }
                    },
                    colors = TopAppBarDefaults.topAppBarColors(
                        containerColor = MaterialTheme.colorScheme.surface,
                    ),
                )
            },
        ) { padding ->
            Column(modifier = Modifier.padding(padding)) {
                if (!isConnected) {
                    // Connection method picker
                    ConnectionMethodPicker(
                        strategies = platform.strategies,
                        activeStrategyId = activeStrategyId,
                        onStrategySelected = { strategy ->
                            activeStrategyId = strategy.id
                            platform.config.set("active_strategy", strategy.id)
                            kotlinx.coroutines.MainScope().launch {
                                strategy.start()
                            }
                        },
                        onConnectCode = { code ->
                            val codeStrategy = platform.strategies.firstOrNull {
                                it.id == "connection-code"
                            } as? com.unilinker.android.core.strategies.ConnectionCodeStrategy
                            codeStrategy?.let { s ->
                                s.listenForCode(code)
                                kotlinx.coroutines.MainScope().launch {
                                    s.discover().collectLatest { list ->
                                        devices = list
                                        if (list.isNotEmpty()) {
                                            val peer = list.first()
                                            connectedDeviceName = peer.name
                                            screenMirror.connectTo(peer)
                                        }
                                    }
                                }
                            }
                        },
                        onConnectAddress = { address ->
                            val ipStrategy = platform.strategies.firstOrNull {
                                it.id == "manual-ip"
                            } as? com.unilinker.android.core.strategies.ManualIpStrategy
                            ipStrategy?.let { s ->
                                kotlinx.coroutines.MainScope().launch {
                                    s.connectToAddress(address).onSuccess { mesh ->
                                        connectedDeviceName = address
                                    }
                                }
                            }
                        },
                    )

                    // Share my info
                    val activeStrategy = platform.strategies.firstOrNull { it.id == activeStrategyId }
                    if (activeStrategy != null) {
                        com.unilinker.android.ui.ShareInfoCard(
                            shareInfo = activeStrategy.getShareInfo(),
                            onGenerateCode = {
                                val cs = platform.strategies.firstOrNull {
                                    it.id == "connection-code"
                                } as? com.unilinker.android.core.strategies.ConnectionCodeStrategy
                                cs?.generateCode()
                            },
                        )
                    }

                    Spacer(modifier = Modifier.weight(1f))

                    // Device list
                    DeviceListView(
                        devices = devices,
                        isScanning = isScanning,
                        onConnect = { device ->
                            connectedDeviceName = device.name
                            screenMirror.connectTo(device)
                        },
                    )
                } else {
                    // Connected - show stream
                    ScreenMirrorTab(screenMirror)
                }
            }
        }
    }

    override fun onDestroy() {
        platform.onDestroy()
        super.onDestroy()
    }
}
