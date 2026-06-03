package com.unilinker.android.core.strategies

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.wifi.p2p.WifiP2pConfig
import android.net.wifi.p2p.WifiP2pDevice
import android.net.wifi.p2p.WifiP2pDeviceList
import android.net.wifi.p2p.WifiP2pInfo
import android.net.wifi.p2p.WifiP2pManager
import android.util.Log
import com.unilinker.android.sdk.*
import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * Wi-Fi Direct (P2P) 连接策略
 * 不需要路由器，设备之间直接连接
 */
class WifiP2pStrategy(
    private val context: Context,
    private val peerMeshFactory: (String) -> IPeerMesh
) : IConnectionStrategy {

    override val id = "wifi-p2p"
    override val name = "Wi-Fi 直连"
    override val icon = "📡"
    override val description = "无需路由器，设备间直接连接"
    override val autoDiscover = true
    override val needsRelay = false

    private val manager: WifiP2pManager? by lazy {
        context.getSystemService(Context.WIFI_P2P_SERVICE) as? WifiP2pManager
    }

    private var channel: WifiP2pManager.Channel? = null
    private var isStarted = false

    // Device list state
    private val _devices = MutableStateFlow<List<PeerDevice>>(emptyList())
    private val _connectionState = MutableStateFlow<P2pState>(P2pState.Idle)

    // Incoming connections
    private val _incomingConnections = MutableSharedFlow<ConnectionRequest>()

    // P2P device list
    private var p2pDevices: List<WifiP2pDevice> = emptyList()
    private var thisDevice: WifiP2pDevice? = null
    private var groupInfo: WifiP2pInfo? = null

    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            when (intent?.action) {
                WifiP2pManager.WIFI_P2P_STATE_CHANGED_ACTION -> {
                    val state = intent.getIntExtra(WifiP2pManager.EXTRA_WIFI_STATE, -1)
                    if (state == WifiP2pManager.WIFI_P2P_STATE_ENABLED) {
                        _connectionState.value = P2pState.Enabled
                    } else {
                        _connectionState.value = P2pState.Disabled
                    }
                }

                WifiP2pManager.WIFI_P2P_PEERS_CHANGED_ACTION -> {
                    manager?.requestPeers(channel) { peers: WifiP2pDeviceList? ->
                        peers?.let { updateDeviceList(it) }
                    }
                }

                WifiP2pManager.WIFI_P2P_CONNECTION_CHANGED_ACTION -> {
                    manager?.requestConnectionInfo(channel) { info: WifiP2pInfo? ->
                        groupInfo = info
                        handleConnectionChange(info)
                    }
                }

                WifiP2pManager.WIFI_P2P_THIS_DEVICE_CHANGED_ACTION -> {
                    thisDevice = intent.getParcelableExtra(WifiP2pManager.EXTRA_WIFI_P2P_DEVICE)
                }
            }
        }
    }

    override suspend fun start() {
        if (isStarted) return

        channel = manager?.initialize(context, context.mainLooper, null)
        if (channel == null) {
            Log.e(TAG, "Wi-Fi P2P not supported on this device")
            return
        }

        // Register broadcast receiver
        val intentFilter = IntentFilter().apply {
            addAction(WifiP2pManager.WIFI_P2P_STATE_CHANGED_ACTION)
            addAction(WifiP2pManager.WIFI_P2P_PEERS_CHANGED_ACTION)
            addAction(WifiP2pManager.WIFI_P2P_CONNECTION_CHANGED_ACTION)
            addAction(WifiP2pManager.WIFI_P2P_THIS_DEVICE_CHANGED_ACTION)
        }
        context.registerReceiver(receiver, intentFilter)

        isStarted = true
        discoverPeers()
    }

    override suspend fun stop() {
        if (!isStarted) return

        try {
            context.unregisterReceiver(receiver)
        } catch (_: Exception) {}

        manager?.stopPeerDiscovery(channel, null)
        manager?.removeGroup(channel, null)

        channel?.close()
        channel = null
        isStarted = false
    }

    override fun discover(): Flow<List<PeerDevice>> = _devices.asStateFlow()

    fun discoverPeers() {
        manager?.discoverPeers(channel, object : WifiP2pManager.ActionListener {
            override fun onSuccess() {
                Log.d(TAG, "Peer discovery started")
                _connectionState.value = P2pState.Discovering
            }

            override fun onFailure(reason: Int) {
                Log.e(TAG, "Peer discovery failed: $reason")
                _connectionState.value = P2pState.Error("Discovery failed: $reason")
            }
        })
    }

    private fun updateDeviceList(peers: WifiP2pDeviceList) {
        p2pDevices = peers.deviceList.toList()
        _devices.value = p2pDevices.map { device ->
            PeerDevice(
                id = device.deviceAddress,
                name = device.deviceName.ifEmpty { "Unknown Device" },
                ipAddress = device.deviceAddress, // P2P uses MAC as address
                port = 9527,
            )
        }
        Log.d(TAG, "Found ${p2pDevices.size} P2P devices")
    }

    override suspend fun connect(peer: PeerDevice): Result<IPeerMesh> = suspendCancellableCoroutine { cont ->
        val device = p2pDevices.find { it.deviceAddress == peer.id }
        if (device == null) {
            cont.resume(Result.failure(Exception("Device not found")))
            return@suspendCancellableCoroutine
        }

        val config = WifiP2pConfig().apply {
            this.deviceAddress = device.deviceAddress
        }

        manager?.connect(channel, config, object : WifiP2pManager.ActionListener {
            override fun onSuccess() {
                Log.d(TAG, "Connecting to ${device.deviceName}")
                _connectionState.value = P2pState.Connecting(peer.name)
            }

            override fun onFailure(reason: Int) {
                val error = "Connection failed: $reason"
                Log.e(TAG, error)
                _connectionState.value = P2pState.Error(error)
                cont.resume(Result.failure(Exception(error)))
            }
        })

        // Connection result will be handled in handleConnectionChange
    }

    private fun handleConnectionChange(info: WifiP2pInfo?) {
        if (info == null) return

        if (info.groupFormed && info.isGroupOwner) {
            // This device is the group owner (server)
            Log.d(TAG, "Group owner - waiting for connections on ${info.groupOwnerAddress}")
            _connectionState.value = P2pState.ConnectedAsServer(info.groupOwnerAddress?.hostAddress ?: "")
        } else if (info.groupFormed) {
            // This device is a client
            Log.d(TAG, "Connected to group owner: ${info.groupOwnerAddress}")
            _connectionState.value = P2pState.ConnectedAsClient(info.groupOwnerAddress?.hostAddress ?: "")

            // Create WebRTC connection to the server
            val serverAddress = "${info.groupOwnerAddress?.hostAddress}:9527"
            // Trigger connection to server via the mesh
        }
    }

    override val incomingConnections: SharedFlow<ConnectionRequest> = _incomingConnections.asSharedFlow()

    override suspend fun accept(request: ConnectionRequest): Result<IPeerMesh> {
        val peerMesh = peerMeshFactory("p2p-${request.fromDevice.id}")
        return Result.success(peerMesh)
    }

    override suspend fun reject(request: ConnectionRequest) {
        manager?.cancelConnect(channel, null)
    }

    override fun getShareInfo(): ShareInfo {
        val device = thisDevice
        return if (device != null && groupInfo?.groupFormed == true) {
            ShareInfo(
                type = ShareType.IP_PORT,
                value = "${groupInfo?.groupOwnerAddress?.hostAddress}:9527",
                displayText = "Wi-Fi 直连: ${device.deviceName}"
            )
        } else {
            ShareInfo(
                type = ShareType.IP_PORT,
                value = "搜索中...",
                displayText = "Wi-Fi 直连模式 - 点击搜索附近设备"
            )
        }
    }

    /**
     * Create a P2P group (become group owner)
     */
    fun createGroup() {
        manager?.createGroup(channel, object : WifiP2pManager.ActionListener {
            override fun onSuccess() {
                Log.d(TAG, "Group created successfully")
            }

            override fun onFailure(reason: Int) {
                Log.e(TAG, "Group creation failed: $reason")
            }
        })
    }

    companion object {
        private const val TAG = "WifiP2pStrategy"
    }
}

sealed class P2pState {
    data object Idle : P2pState()
    data object Disabled : P2pState()
    data object Enabled : P2pState()
    data object Discovering : P2pState()
    data class Connecting(val deviceName: String) : P2pState()
    data class ConnectedAsServer(val ipAddress: String) : P2pState()
    data class ConnectedAsClient(val serverAddress: String) : P2pState()
    data class Error(val message: String) : P2pState()
}