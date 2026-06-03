package com.unilinker.android.core

import android.content.Context
import com.unilinker.android.core.strategies.ConnectionCodeStrategy
import com.unilinker.android.core.strategies.LanMdnsStrategy
import com.unilinker.android.core.strategies.ManualIpStrategy
import com.unilinker.android.core.strategies.WifiP2pStrategy
import com.unilinker.android.sdk.*
import com.unilinker.android.sdk.models.PluginInfo
import kotlinx.coroutines.runBlocking

class Platform(
    private val context: Context,
) {
    // Connection strategies
    val lanStrategy = LanMdnsStrategy(context)
    val manualIpStrategy = ManualIpStrategy()
    val codeStrategy = ConnectionCodeStrategy()
    val wifiP2pStrategy = WifiP2pStrategy(context) { peerId ->
        WebRTCService("http://localhost:9527", peerId)
    }

    val strategies: List<IConnectionStrategy> = listOf(
        lanStrategy,
        wifiP2pStrategy,
        manualIpStrategy,
        codeStrategy,
    )

    val activeStrategy: IConnectionStrategy
        get() = strategies.firstOrNull { it.id == config.get("active_strategy", "lan-mdns") }
            ?: lanStrategy

    val config = ConfigStore(context)

    private val plugins = mutableMapOf<String, IPlugin>()
    val loadedPlugins: Map<String, IPlugin> get() = plugins

    private val pendingTabs = mutableListOf<PluginTab>()

    val uiProvider = object : IUIProvider {
        override fun registerTab(tab: PluginTab) {
            pendingTabs.add(tab)
        }
    }

    fun registerPlugin(plugin: IPlugin) {
        plugins[plugin.id] = plugin
    }

    suspend fun start() {
        // Start the active connection strategy
        activeStrategy.start()

        // Initialize all plugins
        for ((id, plugin) in plugins) {
            val info = PluginInfo(
                id = plugin.id,
                name = plugin.name,
                version = plugin.version,
                capabilities = plugin.capabilities,
            )

            val ctx = object : IPluginContext {
                override val self = info
                override val peers get() = throw UnsupportedOperationException(
                    "Peers managed by plugin")
                override val discovery = this@Platform.lanStrategy.let {
                    object : IDeviceDiscovery {
                        override val isScanning = kotlinx.coroutines.flow.MutableStateFlow(false)
                        override fun discover() = it.discover()
                        override fun stop() = runBlocking { it.stop() }
                    }
                }
                override val config = this@Platform.config
                override val ui = uiProvider
            }

            runCatching { plugin.initialize(ctx) }
        }
    }

    fun getRegisteredTabs(): List<PluginTab> = pendingTabs.toList()

    fun onDestroy() {
        runBlocking {
            strategies.forEach { runCatching { it.stop() } }
            plugins.values.forEach { runCatching { it.shutdown() } }
        }
    }
}
