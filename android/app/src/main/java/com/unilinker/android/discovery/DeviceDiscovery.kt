package com.unilinker.android.discovery

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow

data class PeerDevice(
    val id: String,
    val name: String,
    val ipAddress: String,
    val port: Int,
    val capabilities: List<String> = emptyList(),
)

class DeviceDiscovery(private val context: Context) {

    private val nsdManager: NsdManager =
        context.getSystemService(Context.NSD_SERVICE) as NsdManager

    companion object {
        private const val SERVICE_TYPE = "_unilinker._tcp.local."
        private const val TAG = "UniLinkerDiscovery"
    }

    fun discover(): Flow<List<PeerDevice>> = callbackFlow {
        val discoveredDevices = mutableMapOf<String, PeerDevice>()
        var discoveryListener: NsdManager.DiscoveryListener? = null

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
                Log.d(TAG, "Device resolved: $device")
            }
        }

        discoveryListener = object : NsdManager.DiscoveryListener {
            override fun onDiscoveryStarted(serviceType: String) {
                Log.d(TAG, "Discovery started: $serviceType")
            }

            override fun onDiscoveryStopped(serviceType: String) {
                Log.d(TAG, "Discovery stopped: $serviceType")
            }

            override fun onServiceFound(serviceInfo: NsdServiceInfo) {
                Log.d(TAG, "Service found: ${serviceInfo.serviceName}")
                nsdManager.resolveService(serviceInfo, resolveListener)
            }

            override fun onServiceLost(serviceInfo: NsdServiceInfo) {
                discoveredDevices.remove(serviceInfo.serviceName)
                trySend(discoveredDevices.values.toList())
                Log.d(TAG, "Service lost: ${serviceInfo.serviceName}")
            }

            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
                Log.e(TAG, "Discovery start failed: $serviceType, code=$errorCode")
                nsdManager.stopServiceDiscovery(this)
            }

            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {
                Log.e(TAG, "Discovery stop failed: code=$errorCode")
            }
        }

        nsdManager.discoverServices(
            SERVICE_TYPE,
            NsdManager.PROTOCOL_DNS_SD,
            discoveryListener,
        )

        awaitClose {
            discoveryListener?.let { nsdManager.stopServiceDiscovery(it) }
        }
    }
}
