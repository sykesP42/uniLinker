package com.unilinker.android.plugins.screenmirror

import com.unilinker.android.core.WebRTCService
import com.unilinker.android.sdk.*
import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.webrtc.VideoTrack

class ScreenMirrorPlugin : IPlugin {

    override val id = "com.unilinker.screen-mirror"
    override val name = "屏幕投屏"
    override val version = "1.0.0"
    override val icon = "📺"
    override val capabilities = listOf("screen-capture", "video-stream")

    private var context: IPluginContext? = null
    private var webRtcService: WebRTCService? = null

    private val _connectionState = MutableStateFlow(PeerConnectionState.IDLE)
    val connectionState: StateFlow<PeerConnectionState> = _connectionState

    private val _videoTrack = MutableStateFlow<VideoTrack?>(null)
    val videoTrack: StateFlow<VideoTrack?> = _videoTrack

    private val _stats = MutableStateFlow(com.unilinker.android.sdk.models.StreamStats())
    val stats: StateFlow<com.unilinker.android.sdk.models.StreamStats> = _stats

    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error

    private val scope = CoroutineScope(Dispatchers.Main + SupervisorJob())

    override suspend fun initialize(context: IPluginContext): Boolean {
        this.context = context

        context.ui.registerTab(
            PluginTab(
                pluginId = id,
                title = name,
                icon = icon,
                content = { ScreenMirrorTab(plugin = this) },
            )
        )

        return true
    }

    fun connectTo(device: PeerDevice) {
        _connectionState.value = PeerConnectionState.CONNECTING
        _error.value = null

        val url = "http://${device.ipAddress}:${device.port}"
        val service = WebRTCService(url, "android-${System.currentTimeMillis()}")
        webRtcService = service

        try {
            service.initialize()

            service.onVideoTrackReady { track ->
                _videoTrack.value = track
            }

            // Collect connection state
            scope.launch {
                service.connectionState.collect { state ->
                    _connectionState.value = state
                    when (state) {
                        PeerConnectionState.CONNECTED -> {
                            _error.value = null
                        }
                        PeerConnectionState.ERROR -> {
                            _error.value = "连接失败"
                        }
                        else -> {}
                    }
                }
            }

            // Collect stats
            scope.launch {
                service.stats.collect { _stats.value = it }
            }

            // Start connection
            service.connect(device)

        } catch (e: Exception) {
            _connectionState.value = PeerConnectionState.ERROR
            _error.value = e.message ?: "初始化失败"
        }
    }

    fun disconnect() {
        webRtcService?.disconnect()
        webRtcService = null
        _videoTrack.value = null
        _connectionState.value = PeerConnectionState.IDLE
        _error.value = null
    }

    fun clearError() {
        _error.value = null
    }

    override suspend fun shutdown() {
        disconnect()
        webRtcService?.dispose()
        scope.cancel()
    }
}