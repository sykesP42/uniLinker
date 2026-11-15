package com.unilinker.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import com.unilinker.android.core.Platform
import com.unilinker.android.plugins.screenmirror.ScreenMirrorPlugin
import com.unilinker.android.plugins.screenmirror.ScreenMirrorTab
import com.unilinker.android.ui.MainViewModel
import com.unilinker.android.ui.ModernMainScreen
import com.unilinker.android.ui.theme.UniLinkerTheme
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch
import org.webrtc.ContextUtils

class MainActivity : ComponentActivity() {

    private val platform by lazy { Platform(applicationContext) }
    private val screenMirror = ScreenMirrorPlugin()
    private val mainViewModel by lazy { MainViewModel(platform, screenMirror) }
    private var connectFired = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ContextUtils.initialize(applicationContext)

        platform.registerPlugin(screenMirror)

        MainScope().launch {
            platform.start()
        }

        // Debug/discovery deep link: `am start --es connect "ip:port"` starts
        // the receive flow directly, bypassing manual UI input. Fired exactly
        // once, outside composition, after the platform settles.
        val connectAddress = intent?.getStringExtra("connect")
        if (connectAddress != null && !connectFired) {
            connectFired = true
            MainScope().launch {
                kotlinx.coroutines.delay(2500)
                mainViewModel.connectToAddress(connectAddress)
            }
        }

        setContent {
            UniLinkerTheme {
                val uiState by mainViewModel.uiState.collectAsState()
                val connectionState by screenMirror.connectionState.collectAsState()
                val errorMessage by screenMirror.error.collectAsState()

                ModernMainScreen(
                    connectionState = connectionState,
                    isScanning = uiState.isScanning,
                    isConnecting = uiState.isConnecting,
                    devices = uiState.devices,
                    selectedStrategy = uiState.activeStrategyId,
                    errorMessage = errorMessage ?: uiState.error,
                    onStrategyChange = { mainViewModel.selectStrategy(it) },
                    onConnect = { device -> mainViewModel.connectToDevice(device) },
                    onManualConnect = { address -> mainViewModel.connectToAddress(address) },
                    onDisconnect = { mainViewModel.disconnect() },
                    onRefresh = { mainViewModel.selectStrategy(uiState.activeStrategyId) },
                    onClearError = { screenMirror.clearError() }
                ) {
                    // Connected content - show video stream
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
