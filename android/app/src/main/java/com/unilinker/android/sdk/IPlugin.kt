package com.unilinker.android.sdk

import com.unilinker.android.sdk.models.PluginInfo

interface IPlugin {
    val id: String
    val name: String
    val version: String
    val icon: String
    val capabilities: List<String>

    suspend fun initialize(context: IPluginContext): Boolean
    suspend fun shutdown()
}
