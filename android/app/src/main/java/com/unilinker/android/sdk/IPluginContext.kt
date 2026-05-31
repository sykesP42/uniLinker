package com.unilinker.android.sdk

import com.unilinker.android.sdk.models.PluginInfo

interface IPluginContext {
    val peers: IPeerMesh
    val discovery: IDeviceDiscovery
    val config: IConfigStore
    val ui: IUIProvider
    val self: PluginInfo
}
