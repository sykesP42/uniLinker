package com.unilinker.android.core.strategies

import com.unilinker.android.core.WebRTCService
import com.unilinker.android.sdk.*
import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.flow.*

/**
 * 手动 IP:Port 连接策略。
 * 跨子网或 mDNS 不可用时，直接输入对方地址。
 */
class ManualIpStrategy : IConnectionStrategy {

    override val id = "manual-ip"
    override val name = "手动输入地址"
    override val icon = "🔢"
    override val description = "输入对方的 IP 地址和端口直接连接"
    override val autoDiscover = false
    override val needsRelay = false

    private val _incomingConnections = MutableSharedFlow<ConnectionRequest>(
        extraBufferCapacity = 10,
    )
    override val incomingConnections: SharedFlow<ConnectionRequest> = _incomingConnections

    override suspend fun start() {}
    override suspend fun stop() {}

    override fun discover(): Flow<List<PeerDevice>> = flowOf(emptyList())

    /**
     * 通过 IP:Port 解析为 PeerDevice 并连接
     */
    suspend fun connectToAddress(address: String): Result<IPeerMesh> {
        val (ip, port) = parseAddress(address)
            ?: return Result.failure(IllegalArgumentException("无效地址: $address"))

        val peer = PeerDevice(
            id = "$ip:$port",
            name = ip,
            ipAddress = ip,
            port = port,
        )
        return connect(peer)
    }

    override suspend fun connect(peer: PeerDevice): Result<IPeerMesh> {
        return try {
            val svc = WebRTCService("http://${peer.ipAddress}:${peer.port}")
            svc.initialize()
            svc.connect(peer)
            Result.success(svc)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun accept(request: ConnectionRequest): Result<IPeerMesh> {
        return connect(request.fromDevice)
    }

    override suspend fun reject(request: ConnectionRequest) {}

    override fun getShareInfo(): ShareInfo {
        val ip = getLocalIpAddress()
        return ShareInfo(
            type = ShareType.IP_PORT,
            value = "$ip:9527",
            displayText = "告诉对方你的地址: $ip:9527",
        )
    }

    companion object {
        fun parseAddress(input: String): Pair<String, Int>? {
            val trimmed = input.trim()
            // Try "ip:port" format
            val colonIdx = trimmed.lastIndexOf(':')
            if (colonIdx > 0) {
                val ip = trimmed.substring(0, colonIdx)
                val port = trimmed.substring(colonIdx + 1).toIntOrNull() ?: 9527
                return ip to port
            }
            // Try bare IP
            return trimmed to 9527
        }

        fun getLocalIpAddress(): String {
            try {
                val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
                while (interfaces.hasMoreElements()) {
                    val iface = interfaces.nextElement()
                    if (iface.isLoopback || !iface.isUp) continue
                    val addrs = iface.inetAddresses
                    while (addrs.hasMoreElements()) {
                        val addr = addrs.nextElement()
                        if (addr is java.net.Inet4Address && !addr.isLoopbackAddress) {
                            return addr.hostAddress ?: continue
                        }
                    }
                }
            } catch (_: Exception) {}
            return "unknown"
        }
    }
}
