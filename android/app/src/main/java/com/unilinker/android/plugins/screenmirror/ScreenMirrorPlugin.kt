package com.unilinker.android.plugins.screenmirror

import com.unilinker.android.core.WebRTCService
import com.unilinker.android.sdk.*
import com.unilinker.android.sdk.models.PeerDevice
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

    private val _isConnected = MutableStateFlow(false)
    val isConnected: StateFlow<Boolean> = _isConnected

    private val _videoTrack = MutableStateFlow<VideoTrack?>(null)
    val videoTrack: StateFlow<VideoTrack?> = _videoTrack

    private val _connectionState = MutableStateFlow(PeerConnectionState.IDLE)
    val connectionState: StateFlow<PeerConnectionState> = _connectionState

    private val _stats = MutableStateFlow(com.unilinker.android.sdk.models.StreamStats())
    val stats: StateFlow<com.unilinker.android.sdk.models.StreamStats> = _stats

    override suspend fun initialize(context: IPluginContext): Boolean {
        this.context = context

        context.ui.registerTab(
            PluginTab(
                pluginId = id,
                title = name,
                icon = icon,
                content = {
                    ScreenMirrorTab(
                        plugin = this,
                    )
                },
            )
        )

        return true
    }

    fun connectTo(device: PeerDevice) {
        val url = "http://${device.ipAddress}:${device.port}"
        val service = WebRTCService(url)
        webRtcService = service
        service.initialize()

        service.onVideoTrackReady { track ->
            _videoTrack.value = track
        }

        // Mirror state
        _connectionState.value = PeerConnectionState.CONNECTING
        scope.launch {
            service.connectionState.collect { state ->
                _connectionState.value = state
                _isConnected.value = state == PeerConnectionState.CONNECTED
            }
        }
        scope.launch {
            service.stats.collect { _stats.value = it }
        }

        service.connect(device)
    }

    fun disconnect() {
        webRtcService?.disconnect()
        webRtcService = null
        _videoTrack.value = null
        _connectionState.value = PeerConnectionState.IDLE
        _isConnected.value = false
    }

    override suspend fun shutdown() {
        disconnect()
        webRtcService?.dispose()
    }

    companion object {
        private val scope = kotlinx.coroutines.CoroutineScope(
            kotlinx.coroutines.Dispatchers.Default + kotlinx.coroutines.SupervisorJob()
        )
    }
}
