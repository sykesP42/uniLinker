package com.unilinker.android.sdk.models

data class PeerDevice(
    val id: String,
    val name: String,
    val ipAddress: String,
    val port: Int,
    val version: String = "",
    val capabilities: List<String> = emptyList(),
)

data class PluginInfo(
    val id: String,
    val name: String,
    val version: String,
    val capabilities: List<String> = emptyList(),
    val icon: String = "📦",
)

enum class ChannelType { MediaTrack, DataChannel }

enum class ChannelState { Connecting, Open, Closing, Closed }

enum class DeviceRole { Sender, Receiver, Both }

data class StreamStats(
    val width: Int = 0,
    val height: Int = 0,
    val fps: Int = 0,
    val bitrateKbps: Int = 0,
    val decodeMs: Float = 0f,
    val rttMs: Long = 0,
)
