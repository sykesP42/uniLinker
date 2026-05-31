package com.unilinker.android.sdk

import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.StateFlow

interface IDeviceDiscovery {
    val isScanning: StateFlow<Boolean>
    fun discover(): Flow<List<PeerDevice>>
    fun stop()
}
