package com.unilinker.android.sdk

import androidx.compose.runtime.Composable

data class PluginTab(
    val pluginId: String,
    val title: String,
    val icon: String,
    val content: @Composable () -> Unit,
)

interface IUIProvider {
    fun registerTab(tab: PluginTab)
}
