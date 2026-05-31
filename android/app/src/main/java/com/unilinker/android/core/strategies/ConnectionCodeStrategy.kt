package com.unilinker.android.core.strategies

import com.unilinker.android.core.WebRTCService
import com.unilinker.android.sdk.*
import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress

/**
 * 连接码策略。
 * A 生成一个 6 位码，B 输入该码即可连接。
 * 实现：A 在局域网广播"我有码 XYZ，我的地址是 IP:PORT"，
 * B 用该码查找并直连。
 */
class ConnectionCodeStrategy : IConnectionStrategy {

    override val id = "connection-code"
    override val name = "连接码"
    override val icon = "🔗"
    override val description = "输入对方屏幕上显示的 6 位连接码"
    override val autoDiscover = false
    override val needsRelay = false

    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private val _incomingConnections = MutableSharedFlow<ConnectionRequest>(
        extraBufferCapacity = 10,
    )
    override val incomingConnections: SharedFlow<ConnectionRequest> = _incomingConnections

    private val _discoveredPeers = MutableStateFlow<List<PeerDevice>>(emptyList())
    private var myCode: String? = null
    private var broadcastJob: Job? = null
    private var listenJob: Job? = null
    private val port = 9528

    companion object {
        private const val CODE_BROADCAST_PREFIX = "UNILINKER_CODE:"
        private const val CODE_LENGTH = 6
    }

    /** 生成一个新的 6 位连接码 */
    fun generateCode(): String {
        val code = (100000..999999).random().toString()
        myCode = code
        startBroadcast()
        return code
    }

    /** 输入对方的连接码来查找设备 */
    fun enterCode(code: String) {
        // Start listening for broadcasts matching this code
        listenForCode(code)
    }

    private fun startBroadcast() {
        broadcastJob?.cancel()
        broadcastJob = scope.launch {
            val code = myCode ?: return@launch
            val localIp = ManualIpStrategy.getLocalIpAddress()
            val message = "$CODE_BROADCAST_PREFIX$code|$localIp:9527"

            DatagramSocket().use { socket ->
                socket.broadcast = true
                val data = message.toByteArray()
                while (isActive) {
                    try {
                        val packet = DatagramPacket(
                            data, data.size,
                            InetAddress.getByName("255.255.255.255"), port,
                        )
                        socket.send(packet)
                    } catch (_: Exception) {}
                    delay(1000)
                }
            }
        }
    }

    fun listenForCode(targetCode: String) {
        listenJob?.cancel()
        listenJob = scope.launch {
            DatagramSocket(null).use { socket ->
                socket.reuseAddress = true
                socket.bind(InetSocketAddress(port))
                socket.soTimeout = 5000

                val buf = ByteArray(256)
                while (isActive) {
                    try {
                        val packet = DatagramPacket(buf, buf.size)
                        socket.receive(packet)
                        val msg = String(packet.data, 0, packet.length)
                        if (msg.startsWith(CODE_BROADCAST_PREFIX)) {
                            val payload = msg.removePrefix(CODE_BROADCAST_PREFIX)
                            val parts = payload.split("|")
                            if (parts.size == 2 && parts[0] == targetCode) {
                                val (ip, portStr) = parts[1].split(":")
                                val peer = PeerDevice(
                                    id = parts[0],
                                    name = "设备 ($targetCode)",
                                    ipAddress = ip,
                                    port = portStr.toIntOrNull() ?: 9527,
                                )
                                _discoveredPeers.value = listOf(peer)
                            }
                        }
                    } catch (_: Exception) {
                        // timeout, retry
                    }
                }
            }
        }
    }

    override suspend fun start() {}
    override suspend fun stop() {
        scope.cancel()
    }

    override fun discover(): Flow<List<PeerDevice>> = _discoveredPeers

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
        val code = myCode ?: generateCode()
        return ShareInfo(
            type = ShareType.CONNECTION_CODE,
            value = code,
            displayText = "连接码: $code\n告诉对方输入此码",
        )
    }
}
