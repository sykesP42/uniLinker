package com.unilinker.android.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.unilinker.android.core.Platform
import com.unilinker.android.core.strategies.ConnectionCodeStrategy
import com.unilinker.android.core.strategies.ManualIpStrategy
import com.unilinker.android.plugins.screenmirror.ScreenMirrorPlugin
import com.unilinker.android.sdk.IConnectionStrategy
import com.unilinker.android.sdk.PeerConnectionState
import com.unilinker.android.sdk.ShareInfo
import com.unilinker.android.sdk.ShareType
import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch

class MainViewModel(
    private val platform: Platform,
    private val screenMirror: ScreenMirrorPlugin
) : ViewModel() {

    // UI State
    private val _uiState = MutableStateFlow(MainUiState())
    val uiState: StateFlow<MainUiState> = _uiState.asStateFlow()

    // Connection state
    val connectionState: StateFlow<PeerConnectionState> = screenMirror.connectionState

    init {
        loadActiveStrategy()
    }

    private fun loadActiveStrategy() {
        val savedStrategy = platform.config.get("active_strategy", "lan-mdns")
        _uiState.value = _uiState.value.copy(activeStrategyId = savedStrategy)
    }

    fun selectStrategy(strategyId: String) {
        platform.config.set("active_strategy", strategyId)
        _uiState.value = _uiState.value.copy(
            activeStrategyId = strategyId,
            devices = emptyList(),
            isScanning = false,
            error = null
        )

        val strategy = platform.strategies.firstOrNull { it.id == strategyId } ?: return

        if (strategy.autoDiscover) {
            startDiscovery(strategy)
        }
    }

    private fun startDiscovery(strategy: IConnectionStrategy) {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isScanning = true, error = null)

            strategy.start()
            strategy.discover().collectLatest { devices ->
                _uiState.value = _uiState.value.copy(
                    devices = devices,
                    isScanning = false
                )
            }
        }
    }

    fun connectToDevice(device: PeerDevice) {
        _uiState.value = _uiState.value.copy(
            connectedDeviceName = device.name,
            isConnecting = true,
            error = null
        )

        screenMirror.connectTo(device)

        viewModelScope.launch {
            connectionState.collectLatest { state ->
                when (state) {
                    PeerConnectionState.CONNECTED -> {
                        _uiState.value = _uiState.value.copy(isConnecting = false)
                    }
                    PeerConnectionState.ERROR -> {
                        _uiState.value = _uiState.value.copy(
                            isConnecting = false,
                            error = "连接失败",
                            connectedDeviceName = null
                        )
                    }
                    else -> {}
                }
            }
        }
    }

    fun disconnect() {
        screenMirror.disconnect()
        _uiState.value = _uiState.value.copy(
            connectedDeviceName = null,
            error = null
        )
    }

    fun connectWithCode(code: String) {
        val codeStrategy = platform.strategies.firstOrNull {
            it.id == "connection-code"
        } as? ConnectionCodeStrategy ?: return

        viewModelScope.launch {
            codeStrategy.listenForCode(code)
            codeStrategy.discover().collectLatest { devices ->
                _uiState.value = _uiState.value.copy(devices = devices)
                if (devices.isNotEmpty()) {
                    connectToDevice(devices.first())
                }
            }
        }
    }

    fun connectToAddress(address: String) {
        _uiState.value = _uiState.value.copy(isConnecting = true, error = null)

        // Parse address
        val (ip, port) = ManualIpStrategy.parseAddress(address) ?: run {
            _uiState.value = _uiState.value.copy(
                isConnecting = false,
                error = "无效地址格式"
            )
            return
        }

        // Create device and connect
        val device = com.unilinker.android.sdk.models.PeerDevice(
            id = "$ip:$port",
            name = ip,
            ipAddress = ip,
            port = port
        )

        _uiState.value = _uiState.value.copy(connectedDeviceName = address)
        screenMirror.connectTo(device)
    }

    fun generateConnectionCode(): String {
        val codeStrategy = platform.strategies.firstOrNull {
            it.id == "connection-code"
        } as? ConnectionCodeStrategy ?: return ""

        return codeStrategy.generateCode()
    }

    fun getShareInfo(): ShareInfo {
        val activeStrategyId = _uiState.value.activeStrategyId
        val strategy = platform.strategies.firstOrNull { it.id == activeStrategyId }
            ?: return ShareInfo(ShareType.IP_PORT, "", "")
        return strategy.getShareInfo()
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(error = null)
    }

    override fun onCleared() {
        super.onCleared()
        platform.onDestroy()
    }
}

data class MainUiState(
    val activeStrategyId: String = "lan-mdns",
    val devices: List<PeerDevice> = emptyList(),
    val isScanning: Boolean = false,
    val isConnecting: Boolean = false,
    val connectedDeviceName: String? = null,
    val error: String? = null
)