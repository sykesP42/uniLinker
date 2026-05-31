package com.unilinker.android.core.strategies

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import com.unilinker.android.core.WebRTCService
import com.unilinker.android.sdk.*
import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.*

/**
 * 局域网 mDNS 自动发现策略。
 * 同一子网内零配置，打开 App 即可看到其他设备。
 */
class LanMdnsStrategy(context: Context) : IConnectionStrategy {

    override val id = "lan-mdns"
    override val name = "局域网自动发现"
    override val icon = "🏠"
    override val description = "自动发现同一WiFi下的设备，无需任何配置"
    override val autoDiscover = true
    override val needsRelay = false

    private val nsdManager: NsdManager =
        context.getSystemService(Context.NSD_SERVICE) as NsdManager

    private val _incomingConnections = MutableSharedFlow<ConnectionRequest>(
        extraBufferCapacity = 10,
    )
    override val incomingConnections: SharedFlow<ConnectionRequest> = _incomingConnections

    private var discoveryListener: NsdManager.DiscoveryListener? = null

    companion object {
        private const val SERVICE_TYPE = "_unilinker._tcp.local."
    }

    override suspend fun start() {
        // mDNS just listens passively for broadcasts
    }

    override suspend fun stop() {
        discoveryListener?.let { nsdManager.stopServiceDiscovery(it) }
    }

    override fun discover(): Flow<List<PeerDevice>> = callbackFlow {
        val devices = mutableMapOf<String, PeerDevice>()

        val resolveListener = object : NsdManager.ResolveListener {
            override fun onResolveFailed(info: NsdServiceInfo, code: Int) {}
            override fun onServiceResolved(info: NsdServiceInfo) {
                val ip = info.host?.hostAddress ?: return
                val d = PeerDevice(
                    id = info.serviceName,
                    name = info.serviceName,
                    ipAddress = ip,
                    port = if (info.port > 0) info.port else 9527,
                )
                devices[d.id] = d
                trySend(devices.values.toList())
            }
        }

        discoveryListener = object : NsdManager.DiscoveryListener {
            override fun onDiscoveryStarted(type: String) {}
            override fun onDiscoveryStopped(type: String) {}
            override fun onServiceFound(info: NsdServiceInfo) {
                nsdManager.resolveService(info, resolveListener)
            }
            override fun onServiceLost(info: NsdServiceInfo) {
                devices.remove(info.serviceName)
                trySend(devices.values.toList())
            }
            override fun onStartDiscoveryFailed(type: String, code: Int) {}
            override fun onStopDiscoveryFailed(type: String, code: Int) {}
        }

        nsdManager.discoverServices(SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, discoveryListener)
        awaitClose { discoveryListener?.let { nsdManager.stopServiceDiscovery(it) } }
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

    override suspend fun reject(request: ConnectionRequest) {
        // No explicit rejection in mDNS mode
    }

    override fun getShareInfo() = ShareInfo(
        type = ShareType.IP_PORT,
        value = "",
        displayText = "让其他设备打开 App 即可自动发现",
    )
}
