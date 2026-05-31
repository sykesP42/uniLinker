package com.unilinker.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import com.unilinker.android.core.Platform
import com.unilinker.android.plugins.screenmirror.ScreenMirrorPlugin
import com.unilinker.android.sdk.PluginTab
import com.unilinker.android.ui.MainScreen
import com.unilinker.android.ui.theme.UniLinkerTheme
import kotlinx.coroutines.flow.collectLatest
import org.webrtc.ContextUtils

class MainActivity : ComponentActivity() {

    private val platform by lazy { Platform(applicationContext) }
    private val screenMirror = ScreenMirrorPlugin()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ContextUtils.initialize(applicationContext)

        // Register plugins
        platform.registerPlugin(screenMirror)

        // Start platform (initializes all plugins)
        kotlinx.coroutines.MainScope().launch {
            platform.start()
        }

        setContent {
            UniLinkerTheme {
                val tabs = remember { mutableStateListOf<PluginTab>() }

                LaunchedEffect(Unit) {
                    // Collect tabs registered by plugins
                    val registered = platform.getRegisteredTabs()
                    tabs.addAll(registered.map { it.tab })
                }

                LaunchedEffect(Unit) {
                    platform.discovery.discover().collectLatest { devices ->
                        // Devices are pushed to screen mirror plugin for display
                    }
                }

                MainScreen(
                    tabs = tabs,
                    connectedDeviceName = null,
                    onBack = { /* handled by plugin disconnect */ },
                )
            }
        }
    }

    override fun onDestroy() {
        platform.onDestroy()
        super.onDestroy()
    }
}
