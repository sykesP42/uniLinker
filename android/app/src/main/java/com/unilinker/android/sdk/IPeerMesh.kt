package com.unilinker.android.sdk

import com.unilinker.android.sdk.models.PeerDevice
import com.unilinker.android.sdk.models.ChannelState
import kotlinx.coroutines.flow.StateFlow

enum class PeerConnectionState {
    IDLE, CONNECTING, CONNECTED, DISCONNECTED, ERROR
}

interface IPeerMesh {
    val connectedPeers: StateFlow<List<PeerDevice>>
    val connectionState: StateFlow<PeerConnectionState>
    val stats: StateFlow<com.unilinker.android.sdk.models.StreamStats>

    fun connect(peer: PeerDevice)
    fun disconnect()
    fun dispose()
}
