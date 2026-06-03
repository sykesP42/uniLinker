package com.unilinker.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import androidx.lifecycle.viewmodel.compose.viewModel
import com.unilinker.android.core.Platform
import com.unilinker.android.plugins.screenmirror.ScreenMirrorPlugin
import com.unilinker.android.plugins.screenmirror.ScreenMirrorTab
import com.unilinker.android.sdk.PeerConnectionState
import com.unilinker.android.ui.MainViewModel
import com.unilinker.android.ui.ModernMainScreen
import com.unilinker.android.ui.theme.UniLinkerTheme
import kotlinx.coroutines.MainScope
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
                val viewModel: MainViewModel = viewModel {
                    MainViewModel(platform, screenMirror)
                }

                val uiState by viewModel.uiState.collectAsState()
                val connectionState by screenMirror.connectionState.collectAsState()
                val errorMessage by screenMirror.error.collectAsState()

                ModernMainScreen(
                    connectionState = connectionState,
                    isScanning = uiState.isScanning,
                    isConnecting = uiState.isConnecting,
                    devices = uiState.devices,
                    selectedStrategy = uiState.activeStrategyId,
                    errorMessage = errorMessage ?: uiState.error,
                    onStrategyChange = { viewModel.selectStrategy(it) },
                    onConnect = { device -> viewModel.connectToDevice(device) },
                    onManualConnect = { address -> viewModel.connectToAddress(address) },
                    onDisconnect = { viewModel.disconnect() },
                    onRefresh = { viewModel.selectStrategy(uiState.activeStrategyId) },
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