package com.unilinker.android.core

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log
import com.unilinker.android.sdk.IDeviceDiscovery
import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.callbackFlow

class DiscoveryService(context: Context) : IDeviceDiscovery {

    private val nsdManager: NsdManager =
        context.getSystemService(Context.NSD_SERVICE) as NsdManager

    private val _isScanning = MutableStateFlow(false)
    override val isScanning: StateFlow<Boolean> = _isScanning

    private var discoveryListener: NsdManager.DiscoveryListener? = null

    companion object {
        private const val SERVICE_TYPE = "_unilinker._tcp.local."
        private const val TAG = "UniLinkerDiscovery"
    }

    override fun discover(): Flow<List<PeerDevice>> = callbackFlow {
        val discoveredDevices = mutableMapOf<String, PeerDevice>()
        _isScanning.value = true

        val resolveListener = object : NsdManager.ResolveListener {
            override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {
                Log.w(TAG, "Resolve failed: ${serviceInfo.serviceName}, code=$errorCode")
            }

            override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                val deviceId = serviceInfo.serviceName
                val ip = serviceInfo.host?.hostAddress ?: return
                val port = serviceInfo.port

                val device = PeerDevice(
                    id = deviceId,
                    name = serviceInfo.serviceName,
                    ipAddress = ip,
                    port = if (port > 0) port else 9527,
                )

                discoveredDevices[deviceId] = device
                trySend(discoveredDevices.values.toList())
            }
        }

        discoveryListener = object : NsdManager.DiscoveryListener {
            override fun onDiscoveryStarted(serviceType: String) {
                Log.d(TAG, "Discovery started")
            }

            override fun onDiscoveryStopped(serviceType: String) {
                Log.d(TAG, "Discovery stopped")
            }

            override fun onServiceFound(serviceInfo: NsdServiceInfo) {
                nsdManager.resolveService(serviceInfo, resolveListener)
            }

            override fun onServiceLost(serviceInfo: NsdServiceInfo) {
                discoveredDevices.remove(serviceInfo.serviceName)
                trySend(discoveredDevices.values.toList())
            }

            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
                Log.e(TAG, "Discovery start failed, code=$errorCode")
                _isScanning.value = false
            }

            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {
                Log.e(TAG, "Discovery stop failed, code=$errorCode")
            }
        }

        nsdManager.discoverServices(
            SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, discoveryListener,
        )

        awaitClose {
            discoveryListener?.let { nsdManager.stopServiceDiscovery(it) }
            _isScanning.value = false
        }
    }

    override fun stop() {
        discoveryListener?.let { nsdManager.stopServiceDiscovery(it) }
        _isScanning.value = false
    }
}
