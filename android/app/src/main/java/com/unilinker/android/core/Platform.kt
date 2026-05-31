package com.unilinker.android.core

import android.content.Context
import com.unilinker.android.sdk.*
import com.unilinker.android.sdk.models.PluginInfo

class Platform(
    private val context: Context,
) {
    val discovery = DiscoveryService(context)
    val config = ConfigStore(context)

    private val plugins = mutableMapOf<String, IPlugin>()
    val loadedPlugins: Map<String, IPlugin> get() = plugins

    private data class RegisteredTab(
        val pluginId: String,
        val tab: PluginTab,
    )

    private val pendingTabs = mutableListOf<RegisteredTab>()

    val uiProvider = object : IUIProvider {
        override fun registerTab(tab: PluginTab) {
            pendingTabs.add(RegisteredTab(tab.pluginId, tab))
        }
    }

    fun registerPlugin(plugin: IPlugin) {
        plugins[plugin.id] = plugin
    }

    suspend fun start() {
        for ((id, plugin) in plugins) {
            val info = PluginInfo(
                id = plugin.id,
                name = plugin.name,
                version = plugin.version,
                capabilities = plugin.capabilities,
            )

            val context = object : IPluginContext {
                override val self = info
                // Note: peers is created per-connection by the plugin itself
                override val peers get() = throw UnsupportedOperationException(
                    "Peers are managed by plugin implementation")
                override val discovery = this@Platform.discovery
                override val config = this@Platform.config
                override val ui = uiProvider
            }

            val ok = plugin.initialize(context)
            if (!ok) {
                plugins.remove(id)
            }
        }
    }

    fun getRegisteredTabs(): List<RegisteredTab> = pendingTabs.toList()

    fun onDestroy() {
        plugins.values.forEach { runCatching { it.shutdown() } }
    }
}
